using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Ledgers;
using TechShop.Application.Features.Sales;
using TechShop.Application.Features.Stock;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Dashboard;

public sealed record KpiDto(
    decimal TodaySales,
    decimal PeriodSales,
    decimal GrossProfit,
    decimal Expenses,
    decimal Receivables,
    decimal Payables,
    decimal CashInHand,
    int LowStockItems);

public sealed record SeriesPointDto(string Label, decimal Value);

public sealed record DualSeriesPointDto(string Label, decimal First, decimal Second);

/// <summary>The window the figures cover, echoed back so the UI can label the cards.</summary>
public sealed record DashboardRangeDto(DateTime From, DateTime To, int Days, string Granularity);

public sealed record DashboardDto(
    DashboardRangeDto Range,
    KpiDto Kpi,
    IReadOnlyList<DualSeriesPointDto> SalesVsExpenses,
    IReadOnlyList<SeriesPointDto> DailySales,
    IReadOnlyList<SeriesPointDto> SalesByCategory,
    IReadOnlyList<SeriesPointDto> TopProducts,
    AgeingSummaryDto ReceivableAgeing,
    IReadOnlyList<SeriesPointDto> ShopSales,
    IReadOnlyList<InvoiceListDto> RecentInvoices,
    IReadOnlyList<StockLevelDto> LowStock);

public sealed class DashboardService(
    IAppDbContext db,
    IShopScope shopScope,
    ICurrentUser currentUser,
    ICacheService cache,
    IDateTimeProvider clock,
    LedgerService ledgers,
    StockService stock,
    SalesService sales)
{
    private static readonly TimeSpan CacheWindow = TimeSpan.FromSeconds(60);

    /// <summary>Longer than this and the charts bucket by month rather than by day.</summary>
    private const int DailyBucketLimit = 62;

    public async Task<DashboardDto> GetAsync(
        long? shopId,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Not signed in.");
        var (start, endExclusive) = ResolveRange(from, to);

        var key = $"{CacheKeys.Dashboards(shopId, userId)}:{start:yyyyMMdd}:{endExclusive:yyyyMMdd}";

        return await cache
            .GetOrCreateAsync(key, () => BuildAsync(shopId, start, endExclusive, ct), CacheWindow)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Defaults to the last 30 days ending today. A caller may send either end on its own,
    /// and a reversed pair is swapped rather than refused.
    /// </summary>
    private (DateTime Start, DateTime EndExclusive) ResolveRange(DateTime? from, DateTime? to)
    {
        var today = DateTime.SpecifyKind(clock.UtcNow.Date, DateTimeKind.Utc);

        var start = DateTime.SpecifyKind((from ?? today.AddDays(-29)).Date, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind((to ?? today).Date, DateTimeKind.Utc);

        if (start > end)
        {
            (start, end) = (end, start);
        }

        return (start, end.AddDays(1));
    }

    private async Task<DashboardDto> BuildAsync(
        long? shopId,
        DateTime start,
        DateTime endExclusive,
        CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);
        var today = DateTime.SpecifyKind(clock.UtcNow.Date, DateTimeKind.Utc);
        var days = (int)(endExclusive - start).TotalDays;
        var byMonth = days > DailyBucketLimit;

        // Balances are "as at" the end of the range, so a past range reads as it did then.
        var asAt = endExclusive;

        var invoices = await db.SalesInvoices
            .AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Void && shopIds.Contains(i.ShopId)
                && i.InvoiceDate < asAt)
            .Select(i => new
            {
                i.ShopId,
                i.InvoiceDate,
                i.Total,
                i.PaidAmount,
                Cogs = i.Items.Sum(x => x.Qty * x.UnitCost)
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var expenses = await db.Expenses
            .AsNoTracking()
            .Where(e => !e.IsDeleted && shopIds.Contains(e.ShopId) && e.ExpenseDate < asAt)
            .Select(e => new { e.ExpenseDate, e.Amount })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var inRange = invoices
            .Where(i => i.InvoiceDate >= start && i.InvoiceDate < endExclusive)
            .ToList();

        var expensesInRange = expenses
            .Where(e => e.ExpenseDate >= start && e.ExpenseDate < endExclusive)
            .ToList();

        var receivables = invoices.Sum(i => i.Total - i.PaidAmount);

        var payables = await db.PurchaseBills
            .AsNoTracking()
            .Where(b => !b.IsDeleted && shopIds.Contains(b.ShopId) && b.BillDate < asAt)
            .SumAsync(b => b.Total - b.PaidAmount, ct)
            .ConfigureAwait(false);

        var cashInHand = await db.PettyCashEntries
            .AsNoTracking()
            .Where(p => !p.IsDeleted && shopIds.Contains(p.ShopId) && p.EntryDate < asAt)
            .SumAsync(p => p.Direction == PettyCashDirection.In ? p.Amount : -p.Amount, ct)
            .ConfigureAwait(false);

        var lowStock = await stock.LowStockAsync(shopId, 10, ct).ConfigureAwait(false);

        var lowStockCount = await db.StockLevels
            .AsNoTracking()
            .CountAsync(s => shopIds.Contains(s.ShopId) && !s.Product.IsDeleted
                && s.Qty <= s.Product.ReorderLevel, ct)
            .ConfigureAwait(false);

        var kpi = new KpiDto(
            invoices.Where(i => i.InvoiceDate >= today).Sum(i => i.Total),
            inRange.Sum(i => i.Total),
            inRange.Sum(i => i.Total - i.Cogs),
            expensesInRange.Sum(e => e.Amount),
            receivables,
            payables,
            cashInHand,
            lowStockCount);

        // Buckets are shared by the first two charts so they line up with each other.
        var buckets = BuildBuckets(start, endExclusive, byMonth);

        DateTime KeyOf(DateTime value) => byMonth
            ? new DateTime(value.Year, value.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            : DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

        var salesVsExpenses = buckets
            .Select(bucket => new DualSeriesPointDto(
                Label(bucket, byMonth),
                inRange.Where(i => KeyOf(i.InvoiceDate) == bucket).Sum(i => i.Total),
                expensesInRange.Where(e => KeyOf(e.ExpenseDate) == bucket).Sum(e => e.Amount)))
            .ToList();

        var dailySales = buckets
            .Select(bucket => new SeriesPointDto(
                Label(bucket, byMonth),
                inRange.Where(i => KeyOf(i.InvoiceDate) == bucket).Sum(i => i.Total)))
            .ToList();

        var categoryTotals = await db.SalesItems
            .AsNoTracking()
            .Where(x => !x.SalesInvoice.IsDeleted && x.SalesInvoice.Status != InvoiceStatus.Void
                && shopIds.Contains(x.SalesInvoice.ShopId)
                && x.SalesInvoice.InvoiceDate >= start && x.SalesInvoice.InvoiceDate < endExclusive)
            .GroupBy(x => x.Product.Category.Name)
            .Select(g => new { Name = g.Key, Value = g.Sum(x => x.Qty * x.UnitPrice) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var salesByCategory = categoryTotals
            .OrderByDescending(c => c.Value)
            .Select(c => new SeriesPointDto(c.Name, c.Value))
            .ToList();

        var productTotals = await db.SalesItems
            .AsNoTracking()
            .Where(x => !x.SalesInvoice.IsDeleted && x.SalesInvoice.Status != InvoiceStatus.Void
                && shopIds.Contains(x.SalesInvoice.ShopId)
                && x.SalesInvoice.InvoiceDate >= start && x.SalesInvoice.InvoiceDate < endExclusive)
            .GroupBy(x => x.Product.Name)
            .Select(g => new { Name = g.Key, Value = g.Sum(x => x.Qty * x.UnitPrice) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var topProducts = productTotals
            .OrderByDescending(p => p.Value)
            .Take(5)
            .Select(p => new SeriesPointDto(p.Name, p.Value))
            .ToList();

        var ageing = await ledgers
            .CustomerAgeingAsync(
                new QueryRequest { ShopId = shopId, PageSize = 1, ToDate = endExclusive.AddDays(-1) },
                invoiceWise: false,
                ct)
            .ConfigureAwait(false);

        var shopNames = await db.Shops
            .AsNoTracking()
            .Where(s => shopIds.Contains(s.Id) && !s.IsDeleted)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var shopSales = shopNames
            .Select(s => new SeriesPointDto(s.Name, inRange.Where(i => i.ShopId == s.Id).Sum(i => i.Total)))
            .OrderByDescending(s => s.Value)
            .ToList();

        var recent = await sales
            .QueryAsync(
                new QueryRequest
                {
                    ShopId = shopId,
                    Page = 1,
                    PageSize = 8,
                    Sort = [new SortRule { Field = "invoiceDate", Desc = true }],
                    Filters =
                    [
                        new FilterRule
                        {
                            Field = "invoiceDate",
                            Operator = FilterOperator.Between,
                            Value = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            Value2 = endExclusive.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        },
                    ],
                },
                ct)
            .ConfigureAwait(false);

        return new DashboardDto(
            new DashboardRangeDto(start, endExclusive.AddDays(-1), days, byMonth ? "month" : "day"),
            kpi,
            salesVsExpenses,
            dailySales,
            salesByCategory,
            topProducts,
            ageing.Summary,
            shopSales,
            recent.Items,
            lowStock);
    }

    private static List<DateTime> BuildBuckets(DateTime start, DateTime endExclusive, bool byMonth)
    {
        var buckets = new List<DateTime>();

        if (byMonth)
        {
            var cursor = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            while (cursor < endExclusive)
            {
                buckets.Add(cursor);
                cursor = cursor.AddMonths(1);
            }

            return buckets;
        }

        for (var cursor = start; cursor < endExclusive; cursor = cursor.AddDays(1))
        {
            buckets.Add(cursor);
        }

        return buckets;
    }

    private static string Label(DateTime bucket, bool byMonth)
        => bucket.ToString(byMonth ? "MMM yy" : "dd MMM", CultureInfo.InvariantCulture);
}
