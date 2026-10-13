using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Ledgers;

public sealed record LedgerRowDto(
    DateTime Date,
    string Reference,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal Balance);

public sealed record LedgerDto(
    long PartyId,
    string PartyName,
    decimal Opening,
    decimal Closing,
    IReadOnlyList<LedgerRowDto> Rows);

/// <summary>Ageing buckets are measured from the document date. The MVP has no due dates.</summary>
public sealed record AgeingRowDto(
    long PartyId,
    string PartyName,
    string? DocumentNo,
    DateTime? DocumentDate,
    int? AgeDays,
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Over90,
    decimal Total);

public sealed record AgeingSummaryDto(
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Over90,
    decimal Total);

public sealed record AgeingResultDto(AgeingSummaryDto Summary, PagedResult<AgeingRowDto> Rows);

public sealed class LedgerService(
    IAppDbContext db,
    IShopScope shopScope,
    IAuditService audit,
    IExcelExportService excel,
    IDateTimeProvider clock)
{
    public static QuerySpec<AgeingRowDto> AgeingSpec { get; } = new QuerySpec<AgeingRowDto>()
        .Text("partyName", a => a.PartyName, "Name")
        .Text("documentNo", a => a.DocumentNo, "Document")
        .Map("documentDate", a => a.DocumentDate, "Date")
        .Map("ageDays", a => a.AgeDays, "Age (days)")
        .Map("current", a => a.Current, "Current")
        .Map("days1To30", a => a.Days1To30, "1-30")
        .Map("days31To60", a => a.Days31To60, "31-60")
        .Map("days61To90", a => a.Days61To90, "61-90")
        .Map("over90", a => a.Over90, "90+")
        .Map("total", a => a.Total, "Total");

    public static QuerySpec<LedgerRowDto> LedgerSpec { get; } = new QuerySpec<LedgerRowDto>()
        .Map("date", r => r.Date, "Date")
        .Text("reference", r => r.Reference, "Reference")
        .Text("description", r => r.Description, "Description")
        .Map("debit", r => r.Debit, "Debit")
        .Map("credit", r => r.Credit, "Credit")
        .Map("balance", r => r.Balance, "Balance");

    public async Task<LedgerDto> CustomerLedgerAsync(
        long customerId,
        long? shopId,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        var name = await db.Customers
            .Where(c => c.Id == customerId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? "Customer";

        var invoices = await db.SalesInvoices
            .AsNoTracking()
            .Where(i => i.CustomerId == customerId && !i.IsDeleted
                && i.Status != InvoiceStatus.Void && shopIds.Contains(i.ShopId))
            .Select(i => new { i.InvoiceDate, i.InvoiceNo, i.Total })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var receipts = await db.CustomerPayments
            .AsNoTracking()
            .Where(p => p.CustomerId == customerId && !p.IsDeleted && shopIds.Contains(p.ShopId))
            .Select(p => new { p.ReceivedAt, p.Amount, p.Method })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var entries = invoices
            .Select(i => (i.InvoiceDate, Ref: i.InvoiceNo, Desc: "Sales invoice", Debit: i.Total, Credit: 0m))
            .Concat(receipts.Select(p =>
                (p.ReceivedAt, Ref: "Receipt", Desc: $"Payment received ({p.Method})", Debit: 0m, Credit: p.Amount)))
            .OrderBy(e => e.Item1)
            .ToList();

        return Build(customerId, name, entries, from, to);
    }

    public async Task<LedgerDto> SupplierLedgerAsync(
        long supplierId,
        long? shopId,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        var name = await db.Suppliers
            .Where(s => s.Id == supplierId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? "Supplier";

        var bills = await db.PurchaseBills
            .AsNoTracking()
            .Where(b => b.SupplierId == supplierId && !b.IsDeleted && shopIds.Contains(b.ShopId))
            .Select(b => new { b.BillDate, b.BillNo, b.Total })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var payments = await db.SupplierPayments
            .AsNoTracking()
            .Where(p => p.SupplierId == supplierId && !p.IsDeleted && shopIds.Contains(p.ShopId))
            .Select(p => new { p.PaidAt, p.Amount, p.Method })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // A supplier balance grows with bills and shrinks with payments, so bills sit in the
        // debit column here to keep the running balance reading as "amount we owe".
        var entries = bills
            .Select(b => (b.BillDate, Ref: b.BillNo, Desc: "Purchase bill", Debit: b.Total, Credit: 0m))
            .Concat(payments.Select(p =>
                (p.PaidAt, Ref: "Payment", Desc: $"Payment made ({p.Method})", Debit: 0m, Credit: p.Amount)))
            .OrderBy(e => e.Item1)
            .ToList();

        return Build(supplierId, name, entries, from, to);
    }

    public Task<AgeingResultDto> CustomerAgeingAsync(
        QueryRequest request,
        bool invoiceWise,
        CancellationToken ct = default)
        => AgeingAsync(request, invoiceWise, receivable: true, ct);

    public Task<AgeingResultDto> SupplierAgeingAsync(
        QueryRequest request,
        bool invoiceWise,
        CancellationToken ct = default)
        => AgeingAsync(request, invoiceWise, receivable: false, ct);

    public async Task<byte[]> ExportAgeingAsync(
        QueryRequest request,
        bool invoiceWise,
        bool receivable,
        CancellationToken ct = default)
    {
        var all = request with { Page = 1, PageSize = 500 };
        var rows = new List<AgeingRowDto>();
        PagedResult<AgeingRowDto> page;

        do
        {
            page = (await AgeingAsync(all, invoiceWise, receivable, ct).ConfigureAwait(false)).Rows;
            rows.AddRange(page.Items);
            all = all with { Page = all.Page + 1 };
        }
        while (rows.Count < page.TotalCount && page.Items.Count > 0);

        await audit.LogAsync(
            receivable ? Module.Customers : Module.Suppliers,
            AuditAction.Export,
            receivable ? "ReceivableAgeing" : "PayableAgeing",
            summary: $"{rows.Count} rows",
            ct: ct).ConfigureAwait(false);

        var sheet = (receivable ? "Receivable ageing " : "Payable ageing ") + (invoiceWise ? "by document" : "by party");
        return excel.Build(sheet, rows, AgeingSpec, request.Columns);
    }

    private async Task<AgeingResultDto> AgeingAsync(
        QueryRequest request,
        bool invoiceWise,
        bool receivable,
        CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(request.ShopId, ct).ConfigureAwait(false);
        var asAt = (request.ToDate ?? clock.UtcNow).Date;

        var open = receivable
            ? await db.SalesInvoices
                .AsNoTracking()
                .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Void
                    && shopIds.Contains(i.ShopId) && i.Total - i.PaidAmount > 0m)
                .Select(i => new OpenDoc(i.CustomerId, i.Customer.Name, i.InvoiceNo, i.InvoiceDate, i.Total - i.PaidAmount))
                .ToListAsync(ct)
                .ConfigureAwait(false)
            : await db.PurchaseBills
                .AsNoTracking()
                .Where(b => !b.IsDeleted && shopIds.Contains(b.ShopId) && b.Total - b.PaidAmount > 0m)
                .Select(b => new OpenDoc(b.SupplierId, b.Supplier.Name, b.BillNo, b.BillDate, b.Total - b.PaidAmount))
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var documentRows = open
            .Select(d => Bucket(d, (int)(asAt - d.Date.Date).TotalDays))
            .ToList();

        var rows = invoiceWise
            ? documentRows.OrderBy(r => r.PartyName).ThenBy(r => r.DocumentDate).ToList()
            : documentRows
                .GroupBy(r => (r.PartyId, r.PartyName))
                .Select(g => new AgeingRowDto(
                    g.Key.PartyId,
                    g.Key.PartyName,
                    null,
                    null,
                    null,
                    g.Sum(x => x.Current),
                    g.Sum(x => x.Days1To30),
                    g.Sum(x => x.Days31To60),
                    g.Sum(x => x.Days61To90),
                    g.Sum(x => x.Over90),
                    g.Sum(x => x.Total)))
                .OrderByDescending(r => r.Total)
                .ToList();

        var summary = new AgeingSummaryDto(
            rows.Sum(r => r.Current),
            rows.Sum(r => r.Days1To30),
            rows.Sum(r => r.Days31To60),
            rows.Sum(r => r.Days61To90),
            rows.Sum(r => r.Over90),
            rows.Sum(r => r.Total));

        return new AgeingResultDto(summary, QueryRunner.RunInMemory(rows, request, AgeingSpec));
    }

    private static AgeingRowDto Bucket(OpenDoc doc, int age)
    {
        var current = age <= 0 ? doc.Outstanding : 0m;
        var b1 = age is >= 1 and <= 30 ? doc.Outstanding : 0m;
        var b2 = age is >= 31 and <= 60 ? doc.Outstanding : 0m;
        var b3 = age is >= 61 and <= 90 ? doc.Outstanding : 0m;
        var b4 = age > 90 ? doc.Outstanding : 0m;

        return new AgeingRowDto(
            doc.PartyId, doc.PartyName, doc.DocumentNo, doc.Date, age < 0 ? 0 : age,
            current, b1, b2, b3, b4, doc.Outstanding);
    }

    private static LedgerDto Build(
        long partyId,
        string partyName,
        List<(DateTime Date, string Ref, string Desc, decimal Debit, decimal Credit)> entries,
        DateTime? from,
        DateTime? to)
    {
        var fromUtc = from is null ? (DateTime?)null : DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
        var toUtc = to is null ? (DateTime?)null : DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc);

        var opening = fromUtc is null
            ? 0m
            : entries.Where(e => e.Date < fromUtc).Sum(e => e.Debit - e.Credit);

        var running = opening;
        var rows = new List<LedgerRowDto>();

        foreach (var entry in entries.Where(e =>
            (fromUtc is null || e.Date >= fromUtc) && (toUtc is null || e.Date < toUtc)))
        {
            running += entry.Debit - entry.Credit;
            rows.Add(new LedgerRowDto(entry.Date, entry.Ref, entry.Desc, entry.Debit, entry.Credit, running));
        }

        return new LedgerDto(partyId, partyName, opening, running, rows);
    }

    private sealed record OpenDoc(long PartyId, string PartyName, string DocumentNo, DateTime Date, decimal Outstanding);
}
