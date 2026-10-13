using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Reports;

public sealed record ReportFilter(long? ShopId, DateTime? From, DateTime? To);

public sealed record SummaryRowDto(
    string Period,
    DateTime PeriodStart,
    int InvoiceCount,
    decimal Sales,
    decimal Discount,
    decimal Cogs,
    decimal GrossProfit,
    decimal Expenses,
    decimal NetProfit,
    decimal Receipts,
    decimal Purchases);

public sealed record ProfitAndLossDto(
    DateTime From,
    DateTime To,
    decimal Revenue,
    decimal Discount,
    decimal Cogs,
    decimal GrossProfit,
    IReadOnlyList<NamedAmountDto> ExpenseLines,
    decimal Expenses,
    decimal NetProfit);

public sealed record NamedAmountDto(string Name, decimal Amount);

public sealed record BalanceSheetDto(
    DateTime AsAt,
    decimal CashAndBank,
    decimal Receivables,
    decimal Inventory,
    decimal TotalAssets,
    decimal Payables,
    decimal TaxPayable,
    decimal TotalLiabilities,
    decimal OwnerEquity);

public sealed record CashFlowDto(
    DateTime From,
    DateTime To,
    decimal OpeningCash,
    decimal CustomerReceipts,
    decimal OtherCashIn,
    decimal TotalInflow,
    decimal SupplierPayments,
    decimal ExpensePayments,
    decimal OtherCashOut,
    decimal TotalOutflow,
    decimal NetCashFlow,
    decimal ClosingCash);

/// <summary>
/// Every report is derived from the operational tables. There are no journal entries, so a
/// correction to a sale, a bill or an expense flows straight through to the statements.
/// </summary>
public sealed class ReportService(
    IAppDbContext db,
    IShopScope shopScope,
    IAuditService audit,
    IExcelExportService excel,
    IDateTimeProvider clock)
{
    public static QuerySpec<SummaryRowDto> SummarySpec { get; } = new QuerySpec<SummaryRowDto>()
        .Text("period", r => r.Period, "Period")
        .Map("periodStart", r => r.PeriodStart, "Starting")
        .Map("invoiceCount", r => r.InvoiceCount, "Invoices")
        .Map("sales", r => r.Sales, "Sales")
        .Map("discount", r => r.Discount, "Discount")
        .Map("cogs", r => r.Cogs, "COGS")
        .Map("grossProfit", r => r.GrossProfit, "Gross profit")
        .Map("expenses", r => r.Expenses, "Expenses")
        .Map("netProfit", r => r.NetProfit, "Net profit")
        .Map("receipts", r => r.Receipts, "Receipts")
        .Map("purchases", r => r.Purchases, "Purchases");

    public Task<List<SummaryRowDto>> DailyAsync(ReportFilter filter, CancellationToken ct = default)
        => SummaryAsync(filter, monthly: false, ct);

    public Task<List<SummaryRowDto>> MonthlyAsync(ReportFilter filter, CancellationToken ct = default)
        => SummaryAsync(filter, monthly: true, ct);

    public async Task<byte[]> ExportSummaryAsync(ReportFilter filter, bool monthly, CancellationToken ct = default)
    {
        var rows = await SummaryAsync(filter, monthly, ct).ConfigureAwait(false);
        await audit.LogAsync(
            Module.Reports,
            AuditAction.Export,
            monthly ? "MonthlySummary" : "DailySummary",
            summary: $"{rows.Count} rows",
            ct: ct).ConfigureAwait(false);

        return excel.Build(monthly ? "Monthly summary" : "Daily summary", rows, SummarySpec, []);
    }

    private async Task<List<SummaryRowDto>> SummaryAsync(ReportFilter filter, bool monthly, CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(filter.ShopId, ct).ConfigureAwait(false);
        var (from, to) = Range(filter, monthly ? 365 : 30);

        var invoices = await db.SalesInvoices
            .AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Void && shopIds.Contains(i.ShopId)
                && i.InvoiceDate >= from && i.InvoiceDate < to)
            .Select(i => new
            {
                i.InvoiceDate,
                i.Total,
                i.Discount,
                i.TaxTotal,
                Cogs = i.Items.Sum(x => x.Qty * x.UnitCost)
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var expenses = await db.Expenses
            .AsNoTracking()
            .Where(e => !e.IsDeleted && shopIds.Contains(e.ShopId) && e.ExpenseDate >= from && e.ExpenseDate < to)
            .Select(e => new { e.ExpenseDate, e.Amount })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var receipts = await db.CustomerPayments
            .AsNoTracking()
            .Where(p => !p.IsDeleted && shopIds.Contains(p.ShopId) && p.ReceivedAt >= from && p.ReceivedAt < to)
            .Select(p => new { p.ReceivedAt, p.Amount })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var purchases = await db.PurchaseBills
            .AsNoTracking()
            .Where(b => !b.IsDeleted && shopIds.Contains(b.ShopId) && b.BillDate >= from && b.BillDate < to)
            .Select(b => new { b.BillDate, b.Total })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var buckets = new List<DateTime>();
        if (monthly)
        {
            var cursor = new DateTime(from.Year, from.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            while (cursor < to)
            {
                buckets.Add(cursor);
                cursor = cursor.AddMonths(1);
            }
        }
        else
        {
            for (var cursor = from.Date; cursor < to; cursor = cursor.AddDays(1))
            {
                buckets.Add(DateTime.SpecifyKind(cursor, DateTimeKind.Utc));
            }
        }

        DateTime Key(DateTime value) => monthly
            ? new DateTime(value.Year, value.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            : DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

        return buckets
            .Select(bucket =>
            {
                var rows = invoices.Where(i => Key(i.InvoiceDate) == bucket).ToList();

                // Net of sales tax: tax collected belongs to the tax authority, so
                // counting it as sales would overstate both turnover and profit.
                var sales = rows.Sum(r => r.Total - r.TaxTotal);
                var cogs = rows.Sum(r => r.Cogs);
                var expense = expenses.Where(e => Key(e.ExpenseDate) == bucket).Sum(e => e.Amount);

                return new SummaryRowDto(
                    monthly ? bucket.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
                            : bucket.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                    bucket,
                    rows.Count,
                    sales,
                    rows.Sum(r => r.Discount),
                    cogs,
                    sales - cogs,
                    expense,
                    sales - cogs - expense,
                    receipts.Where(p => Key(p.ReceivedAt) == bucket).Sum(p => p.Amount),
                    purchases.Where(b => Key(b.BillDate) == bucket).Sum(b => b.Total));
            })
            .OrderByDescending(r => r.PeriodStart)
            .ToList();
    }

    public async Task<ProfitAndLossDto> ProfitAndLossAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var shopIds = await shopScope.ResolveAsync(filter.ShopId, ct).ConfigureAwait(false);
        var (from, to) = Range(filter, 30);

        var sales = await db.SalesInvoices
            .AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Void && shopIds.Contains(i.ShopId)
                && i.InvoiceDate >= from && i.InvoiceDate < to)
            .Select(i => new { i.Total, i.Discount, i.TaxTotal, Cogs = i.Items.Sum(x => x.Qty * x.UnitCost) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var expenseLines = await db.Expenses
            .AsNoTracking()
            .Where(e => !e.IsDeleted && shopIds.Contains(e.ShopId) && e.ExpenseDate >= from && e.ExpenseDate < to)
            .GroupBy(e => e.ExpenseCategory.Name)
            .Select(g => new NamedAmountDto(g.Key, g.Sum(e => e.Amount)))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Sales tax is collected on behalf of the tax authority, so it is never income.
        // Revenue is the invoice total less the tax charged on it.
        var revenue = sales.Sum(s => s.Total - s.TaxTotal);
        var cogs = sales.Sum(s => s.Cogs);
        var expenses = expenseLines.Sum(e => e.Amount);

        return new ProfitAndLossDto(
            from,
            to.AddTicks(-1),
            revenue,
            sales.Sum(s => s.Discount),
            cogs,
            revenue - cogs,
            expenseLines.OrderByDescending(e => e.Amount).ToList(),
            expenses,
            revenue - cogs - expenses);
    }

    public async Task<BalanceSheetDto> BalanceSheetAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var shopIds = await shopScope.ResolveAsync(filter.ShopId, ct).ConfigureAwait(false);
        var asAt = DateTime.SpecifyKind((filter.To ?? clock.UtcNow).Date.AddDays(1), DateTimeKind.Utc);

        var cash = await CashAtAsync(shopIds, asAt, ct).ConfigureAwait(false);

        var receivables = await db.SalesInvoices
            .AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Void && shopIds.Contains(i.ShopId)
                && i.InvoiceDate < asAt)
            .SumAsync(i => i.Total, ct)
            .ConfigureAwait(false)
            - await db.CustomerPayments
                .AsNoTracking()
                .Where(p => !p.IsDeleted && shopIds.Contains(p.ShopId) && p.ReceivedAt < asAt)
                .SumAsync(p => p.Amount, ct)
                .ConfigureAwait(false);

        var payables = await db.PurchaseBills
            .AsNoTracking()
            .Where(b => !b.IsDeleted && shopIds.Contains(b.ShopId) && b.BillDate < asAt)
            .SumAsync(b => b.Total, ct)
            .ConfigureAwait(false)
            - await db.SupplierPayments
                .AsNoTracking()
                .Where(p => !p.IsDeleted && shopIds.Contains(p.ShopId) && p.PaidAt < asAt)
                .SumAsync(p => p.Amount, ct)
                .ConfigureAwait(false);

        // Inventory is valued at the quantity on hand times the current product cost.
        var inventory = await db.StockLevels
            .AsNoTracking()
            .Where(s => shopIds.Contains(s.ShopId) && !s.Product.IsDeleted)
            .SumAsync(s => s.Qty * s.Product.CostPrice, ct)
            .ConfigureAwait(false);

        // Sales tax charged to customers is owed onward, so it sits as a liability
        // rather than quietly inflating equity.
        var taxPayable = await db.SalesInvoices
            .AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Void && shopIds.Contains(i.ShopId)
                && i.InvoiceDate < asAt)
            .SumAsync(i => i.TaxTotal, ct)
            .ConfigureAwait(false);

        var assets = cash + receivables + inventory;
        var liabilities = payables + taxPayable;

        return new BalanceSheetDto(
            asAt.AddTicks(-1),
            cash,
            receivables,
            inventory,
            assets,
            payables,
            taxPayable,
            liabilities,

            // Equity is the residual, which is what makes the sheet balance by construction.
            assets - liabilities);
    }

    public async Task<CashFlowDto> CashFlowAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var shopIds = await shopScope.ResolveAsync(filter.ShopId, ct).ConfigureAwait(false);
        var (from, to) = Range(filter, 30);

        var opening = await CashAtAsync(shopIds, from, ct).ConfigureAwait(false);

        var receipts = await db.CustomerPayments
            .AsNoTracking()
            .Where(p => !p.IsDeleted && shopIds.Contains(p.ShopId) && p.ReceivedAt >= from && p.ReceivedAt < to)
            .SumAsync(p => p.Amount, ct)
            .ConfigureAwait(false);

        var supplierPayments = await db.SupplierPayments
            .AsNoTracking()
            .Where(p => !p.IsDeleted && shopIds.Contains(p.ShopId) && p.PaidAt >= from && p.PaidAt < to)
            .SumAsync(p => p.Amount, ct)
            .ConfigureAwait(false);

        var expensePayments = await db.Expenses
            .AsNoTracking()
            .Where(e => !e.IsDeleted && shopIds.Contains(e.ShopId) && e.ExpenseDate >= from && e.ExpenseDate < to)
            .SumAsync(e => e.Amount, ct)
            .ConfigureAwait(false);

        var otherIn = await db.PettyCashEntries
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsSystemGenerated && shopIds.Contains(p.ShopId)
                && p.EntryDate >= from && p.EntryDate < to && p.Direction == PettyCashDirection.In)
            .SumAsync(p => p.Amount, ct)
            .ConfigureAwait(false);

        var otherOut = await db.PettyCashEntries
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsSystemGenerated && shopIds.Contains(p.ShopId)
                && p.EntryDate >= from && p.EntryDate < to && p.Direction == PettyCashDirection.Out)
            .SumAsync(p => p.Amount, ct)
            .ConfigureAwait(false);

        var inflow = receipts + otherIn;
        var outflow = supplierPayments + expensePayments + otherOut;

        return new CashFlowDto(
            from,
            to.AddTicks(-1),
            opening,
            receipts,
            otherIn,
            inflow,
            supplierPayments,
            expensePayments,
            otherOut,
            outflow,
            inflow - outflow,
            opening + inflow - outflow);
    }

    /// <summary>
    /// Cash and bank at a point in time. Receipts and payments are read from their own
    /// tables, and only the manual drawer entries are added, so nothing is counted twice.
    /// </summary>
    private async Task<decimal> CashAtAsync(IReadOnlyList<long> shopIds, DateTime before, CancellationToken ct)
    {
        var receipts = await db.CustomerPayments
            .AsNoTracking()
            .Where(p => !p.IsDeleted && shopIds.Contains(p.ShopId) && p.ReceivedAt < before)
            .SumAsync(p => p.Amount, ct)
            .ConfigureAwait(false);

        var supplierPayments = await db.SupplierPayments
            .AsNoTracking()
            .Where(p => !p.IsDeleted && shopIds.Contains(p.ShopId) && p.PaidAt < before)
            .SumAsync(p => p.Amount, ct)
            .ConfigureAwait(false);

        var expenses = await db.Expenses
            .AsNoTracking()
            .Where(e => !e.IsDeleted && shopIds.Contains(e.ShopId) && e.ExpenseDate < before)
            .SumAsync(e => e.Amount, ct)
            .ConfigureAwait(false);

        var manual = await db.PettyCashEntries
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsSystemGenerated && shopIds.Contains(p.ShopId) && p.EntryDate < before)
            .SumAsync(p => p.Direction == PettyCashDirection.In ? p.Amount : -p.Amount, ct)
            .ConfigureAwait(false);

        return receipts - supplierPayments - expenses + manual;
    }

    private (DateTime From, DateTime To) Range(ReportFilter filter, int defaultDays)
    {
        var to = DateTime.SpecifyKind((filter.To ?? clock.UtcNow).Date.AddDays(1), DateTimeKind.Utc);
        var from = DateTime.SpecifyKind(
            (filter.From ?? to.AddDays(-defaultDays)).Date,
            DateTimeKind.Utc);

        return from >= to ? (to.AddDays(-1), to) : (from, to);
    }
}
