using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Common;
using TechShop.Application.Features.Stock;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Purchases;

public sealed record PurchaseBillListDto
{
    public long Id { get; init; }
    public string BillNo { get; init; } = string.Empty;
    public DateTime BillDate { get; init; }
    public long ShopId { get; init; }
    public string ShopName { get; init; } = string.Empty;
    public long SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal Outstanding { get; init; }
    public int ItemCount { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

public sealed record PurchaseItemDto
{
    public long ProductId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Qty { get; init; }
    public decimal UnitCost { get; init; }
    public decimal LineTotal { get; init; }
}

public sealed record PurchaseBillDetailDto(
    long Id,
    string BillNo,
    DateTime BillDate,
    long ShopId,
    string ShopName,
    long SupplierId,
    string SupplierName,
    decimal Total,
    decimal PaidAmount,
    decimal Outstanding,
    PaymentMethod PaymentMethod,
    string? Note,
    IReadOnlyList<PurchaseItemDto> Items);

public sealed record PurchaseLineInput(long ProductId, int Qty, decimal UnitCost);

public sealed record CreatePurchaseRequest(
    long ShopId,
    long SupplierId,
    DateTime BillDate,
    decimal PaidNow,
    PaymentMethod PaymentMethod,
    string? Note,
    IReadOnlyList<PurchaseLineInput> Items);

public sealed record SupplierPaymentDto
{
    public long Id { get; init; }
    public DateTime PaidAt { get; init; }
    public long ShopId { get; init; }
    public string ShopName { get; init; } = string.Empty;
    public long SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public string? BillNo { get; init; }
    public decimal Amount { get; init; }
    public PaymentMethod Method { get; init; }
    public string? Note { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

public sealed record CreateSupplierPaymentRequest(
    long ShopId,
    long SupplierId,
    long? PurchaseBillId,
    DateTime PaidAt,
    decimal Amount,
    PaymentMethod Method,
    string? Note);

public sealed class PurchaseService(
    IAppDbContext db,
    StockService stock,
    DocumentNumberService numbers,
    IAuditService audit,
    ICurrentUser currentUser,
    IShopScope shopScope,
    ICacheService cache,
    IExcelExportService excel,
    IDateTimeProvider clock)
{
    public static QuerySpec<PurchaseBillListDto> Spec { get; } = new QuerySpec<PurchaseBillListDto>()
        .Map("id", b => b.Id, "Id")
        .Text("billNo", b => b.BillNo, "Bill no")
        .Map("billDate", b => b.BillDate, "Date")
        .Text("shopName", b => b.ShopName, "Shop")
        .Text("supplierName", b => b.SupplierName, "Supplier")
        .Map("total", b => b.Total, "Total")
        .Map("paidAmount", b => b.PaidAmount, "Paid")
        .Map("outstanding", b => b.Outstanding, "Outstanding")
        .Map("itemCount", b => b.ItemCount, "Lines")
        .Text("createdBy", b => b.CreatedBy, "By");

    public static QuerySpec<SupplierPaymentDto> PaymentSpec { get; } = new QuerySpec<SupplierPaymentDto>()
        .Map("id", p => p.Id, "Id")
        .Map("paidAt", p => p.PaidAt, "Date")
        .Text("shopName", p => p.ShopName, "Shop")
        .Text("supplierName", p => p.SupplierName, "Supplier")
        .Text("billNo", p => p.BillNo, "Against bill")
        .Map("amount", p => p.Amount, "Amount")
        .Map("method", p => p.Method, "Method")
        .Text("note", p => p.Note, "Note")
        .Text("createdBy", p => p.CreatedBy, "By");

    public async Task<PagedResult<PurchaseBillListDto>> QueryAsync(QueryRequest request, CancellationToken ct = default)
        => await QueryRunner
            .RunAsync(await BillsAsync(request.ShopId, ct).ConfigureAwait(false), request, Spec, ct)
            .ConfigureAwait(false);

    public async Task<byte[]> ExportAsync(QueryRequest request, CancellationToken ct = default)
    {
        var source = await BillsAsync(request.ShopId, ct).ConfigureAwait(false);
        var rows = await QueryRunner.RunForExportAsync(source, request, Spec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.Purchases, AuditAction.Export, nameof(PurchaseBill), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Purchase bills", rows, Spec, request.Columns);
    }

    public async Task<PagedResult<SupplierPaymentDto>> QueryPaymentsAsync(QueryRequest request, CancellationToken ct = default)
        => await QueryRunner
            .RunAsync(await PaymentsAsync(request.ShopId, ct).ConfigureAwait(false), request, PaymentSpec, ct)
            .ConfigureAwait(false);

    public async Task<byte[]> ExportPaymentsAsync(QueryRequest request, CancellationToken ct = default)
    {
        var source = await PaymentsAsync(request.ShopId, ct).ConfigureAwait(false);
        var rows = await QueryRunner.RunForExportAsync(source, request, PaymentSpec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.Purchases, AuditAction.Export, nameof(SupplierPayment), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Supplier payments", rows, PaymentSpec, request.Columns);
    }

    public async Task<PurchaseBillDetailDto> GetAsync(long id, CancellationToken ct = default)
    {
        var shopIds = await shopScope.ResolveAsync(null, ct).ConfigureAwait(false);

        var bill = await db.PurchaseBills
            .AsNoTracking()
            .Include(b => b.Items).ThenInclude(i => i.Product)
            .Include(b => b.Shop)
            .Include(b => b.Supplier)
            .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted && shopIds.Contains(b.ShopId), ct)
            .ConfigureAwait(false) ?? throw new NotFoundException(nameof(PurchaseBill), id);

        var paid = await db.SupplierPayments
            .Where(p => p.PurchaseBillId == id && !p.IsDeleted)
            .SumAsync(p => p.Amount, ct)
            .ConfigureAwait(false);

        return new PurchaseBillDetailDto(
            bill.Id,
            bill.BillNo,
            bill.BillDate,
            bill.ShopId,
            bill.Shop.Name,
            bill.SupplierId,
            bill.Supplier.Name,
            bill.Total,
            paid,
            bill.Total - paid,
            bill.PaymentMethod,
            bill.Note,
            bill.Items
                .Select(i => new PurchaseItemDto
    {
        ProductId = i.ProductId,
        Sku = i.Product.Sku,
        ProductName = i.Product.Name,
        Qty = i.Qty,
        UnitCost = i.UnitCost,
        LineTotal = i.Qty * i.UnitCost
    })
                .ToList());
    }

    public async Task<PurchaseBillDetailDto> CreateAsync(CreatePurchaseRequest request, CancellationToken ct = default)
    {
        await shopScope.EnsureWritableAsync(request.ShopId, ct).ConfigureAwait(false);

        if (request.Items.Count == 0)
        {
            throw ValidationFailedException.ForField(nameof(request.Items), "Add at least one line.");
        }

        if (request.Items.Any(i => i.Qty <= 0))
        {
            throw ValidationFailedException.ForField(nameof(request.Items), "Every line needs a quantity above zero.");
        }

        if (request.Items.Any(i => i.UnitCost < 0))
        {
            throw ValidationFailedException.ForField(nameof(request.Items), "Unit cost cannot be negative.");
        }

        if (!await db.Suppliers.AnyAsync(s => s.Id == request.SupplierId && !s.IsDeleted, ct).ConfigureAwait(false))
        {
            throw ValidationFailedException.ForField(nameof(request.SupplierId), "Pick a valid supplier.");
        }

        var total = request.Items.Sum(i => i.Qty * i.UnitCost);

        if (request.PaidNow < 0 || request.PaidNow > total)
        {
            throw ValidationFailedException.ForField(
                nameof(request.PaidNow),
                "Paid now must be between zero and the bill total.");
        }

        await using var tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);

        var bill = new PurchaseBill
        {
            BillNo = await numbers.NextBillNoAsync(request.ShopId, ct).ConfigureAwait(false),
            ShopId = request.ShopId,
            SupplierId = request.SupplierId,
            BillDate = DateTime.SpecifyKind(request.BillDate, DateTimeKind.Utc),
            Total = total,
            PaidAmount = request.PaidNow,
            PaymentMethod = request.PaymentMethod,
            Note = request.Note,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        foreach (var line in request.Items)
        {
            bill.Items.Add(new PurchaseItem
            {
                ProductId = line.ProductId,
                Qty = line.Qty,
                UnitCost = line.UnitCost
            });
        }

        db.PurchaseBills.Add(bill);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (var line in request.Items)
        {
            await stock.ApplyAsync(
                request.ShopId,
                line.ProductId,
                line.Qty,
                StockMovementType.Purchase,
                line.UnitCost,
                bill.BillNo,
                reason: null,
                ct).ConfigureAwait(false);

            // The latest buying price becomes the product cost used for COGS on future sales.
            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == line.ProductId, ct).ConfigureAwait(false);
            if (product is not null && line.UnitCost > 0)
            {
                product.CostPrice = line.UnitCost;
            }
        }

        // Cash paid at the counter is recorded as a supplier payment so the payable and the
        // cash position both come from one place.
        if (request.PaidNow > 0)
        {
            db.SupplierPayments.Add(new SupplierPayment
            {
                ShopId = request.ShopId,
                SupplierId = request.SupplierId,
                PurchaseBillId = bill.Id,
                PaidAt = bill.BillDate,
                Amount = request.PaidNow,
                Method = request.PaymentMethod,
                Note = $"Paid with bill {bill.BillNo}",
                CreatedAt = clock.UtcNow,
                CreatedBy = currentUser.Username
            });

            if (request.PaymentMethod == PaymentMethod.Cash)
            {
                db.PettyCashEntries.Add(new PettyCashEntry
                {
                    ShopId = request.ShopId,
                    EntryDate = bill.BillDate,
                    Direction = PettyCashDirection.Out,
                    Amount = request.PaidNow,
                    Note = $"Supplier payment with bill {bill.BillNo}",
                    IsSystemGenerated = true,
                    CreatedAt = clock.UtcNow,
                    CreatedBy = currentUser.Username
                });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.Purchases,
            AuditAction.Create,
            nameof(PurchaseBill),
            bill.Id,
            $"{bill.BillNo} total {total:0.00} paid {request.PaidNow:0.00}",
            request.ShopId,
            ct: ct).ConfigureAwait(false);

        return await GetAsync(bill.Id, ct).ConfigureAwait(false);
    }

    public async Task<SupplierPaymentDto> PayAsync(CreateSupplierPaymentRequest request, CancellationToken ct = default)
    {
        await shopScope.EnsureWritableAsync(request.ShopId, ct).ConfigureAwait(false);

        if (request.Amount <= 0)
        {
            throw ValidationFailedException.ForField(nameof(request.Amount), "Enter an amount above zero.");
        }

        var payable = await SupplierBalanceAsync(request.SupplierId, request.ShopId, ct).ConfigureAwait(false);
        if (request.Amount > payable)
        {
            throw ValidationFailedException.ForField(
                nameof(request.Amount),
                $"That supplier is owed {payable:0.00}, so the payment is too large.");
        }

        await using var tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);

        var payment = new SupplierPayment
        {
            ShopId = request.ShopId,
            SupplierId = request.SupplierId,
            PurchaseBillId = request.PurchaseBillId,
            PaidAt = DateTime.SpecifyKind(request.PaidAt, DateTimeKind.Utc),
            Amount = request.Amount,
            Method = request.Method,
            Note = request.Note,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        db.SupplierPayments.Add(payment);

        if (request.PurchaseBillId is { } billId)
        {
            var bill = await db.PurchaseBills.FirstOrDefaultAsync(b => b.Id == billId && !b.IsDeleted, ct)
                .ConfigureAwait(false) ?? throw new NotFoundException(nameof(PurchaseBill), billId);
            bill.PaidAmount += request.Amount;
        }
        else
        {
            await AllocateOldestFirstAsync(request.SupplierId, request.ShopId, request.Amount, ct).ConfigureAwait(false);
        }

        if (request.Method == PaymentMethod.Cash)
        {
            db.PettyCashEntries.Add(new PettyCashEntry
            {
                ShopId = request.ShopId,
                EntryDate = payment.PaidAt,
                Direction = PettyCashDirection.Out,
                Amount = request.Amount,
                Note = $"Supplier payment {request.Amount:0.00}",
                IsSystemGenerated = true,
                CreatedAt = clock.UtcNow,
                CreatedBy = currentUser.Username
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.Purchases,
            AuditAction.Create,
            nameof(SupplierPayment),
            payment.Id,
            $"{request.Amount:0.00} via {request.Method}",
            request.ShopId,
            ct: ct).ConfigureAwait(false);

        var source = await PaymentsAsync(null, ct).ConfigureAwait(false);
        return await source.FirstAsync(p => p.Id == payment.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Unallocated payments clear the oldest open bills first, matching the ageing view.</summary>
    private async Task AllocateOldestFirstAsync(long supplierId, long shopId, decimal amount, CancellationToken ct)
    {
        var open = await db.PurchaseBills
            .Where(b => b.SupplierId == supplierId && b.ShopId == shopId && !b.IsDeleted && b.PaidAmount < b.Total)
            .OrderBy(b => b.BillDate)
            .ThenBy(b => b.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var left = amount;
        foreach (var bill in open)
        {
            if (left <= 0)
            {
                break;
            }

            var due = bill.Total - bill.PaidAmount;
            var take = Math.Min(due, left);
            bill.PaidAmount += take;
            left -= take;
        }
    }

    private Task<decimal> SupplierBalanceAsync(long supplierId, long shopId, CancellationToken ct)
        => db.PurchaseBills
            .Where(b => b.SupplierId == supplierId && b.ShopId == shopId && !b.IsDeleted)
            .SumAsync(b => b.Total - b.PaidAmount, ct);

    private async Task<IQueryable<PurchaseBillListDto>> BillsAsync(long? shopId, CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        return db.PurchaseBills
            .AsNoTracking()
            .Where(b => !b.IsDeleted && shopIds.Contains(b.ShopId))
            .Select(b => new PurchaseBillListDto
    {
        Id = b.Id,
        BillNo = b.BillNo,
        BillDate = b.BillDate,
        ShopId = b.ShopId,
        ShopName = b.Shop.Name,
        SupplierId = b.SupplierId,
        SupplierName = b.Supplier.Name,
        Total = b.Total,
        PaidAmount = b.PaidAmount,
        Outstanding = b.Total - b.PaidAmount,
        ItemCount = b.Items.Count,
        CreatedBy = b.CreatedBy
    });
    }

    private async Task<IQueryable<SupplierPaymentDto>> PaymentsAsync(long? shopId, CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        return db.SupplierPayments
            .AsNoTracking()
            .Where(p => !p.IsDeleted && shopIds.Contains(p.ShopId))
            .Select(p => new SupplierPaymentDto
    {
        Id = p.Id,
        PaidAt = p.PaidAt,
        ShopId = p.ShopId,
        ShopName = p.Shop.Name,
        SupplierId = p.SupplierId,
        SupplierName = p.Supplier.Name,
        BillNo = p.PurchaseBill != null ? p.PurchaseBill.BillNo : null,
        Amount = p.Amount,
        Method = p.Method,
        Note = p.Note,
        CreatedBy = p.CreatedBy
    });
    }
}
