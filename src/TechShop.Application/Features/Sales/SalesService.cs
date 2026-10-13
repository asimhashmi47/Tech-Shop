using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Common;
using TechShop.Application.Features.Stock;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Sales;

public sealed record InvoiceListDto
{
    public long Id { get; init; }
    public string InvoiceNo { get; init; } = string.Empty;
    public DateTime InvoiceDate { get; init; }
    public long ShopId { get; init; }
    public string ShopName { get; init; } = string.Empty;
    public long CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public decimal SubTotal { get; init; }
    public decimal Discount { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal Total { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal Outstanding { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public InvoiceStatus Status { get; init; }
    public int ItemCount { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

public sealed record InvoiceItemDto
{
    public long ProductId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Qty { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal LineTotal { get; init; }
}

public sealed record InvoiceDetailDto(
    long Id,
    string InvoiceNo,
    DateTime InvoiceDate,
    long ShopId,
    string ShopName,
    string? ShopAddress,
    string? ShopPhone,
    long CustomerId,
    string CustomerName,
    string? CustomerPhone,
    decimal SubTotal,
    decimal Discount,
    decimal TaxTotal,
    decimal Total,
    decimal PaidAmount,
    decimal Outstanding,
    PaymentMethod PaymentMethod,
    InvoiceStatus Status,
    string? Note,
    string? VoidReason,
    string CreatedBy,
    IReadOnlyList<InvoiceItemDto> Items);

/// <summary>
/// One line on the way in. Tax arrives as money rather than a rate because the till
/// prefills it from the product but lets the operator change it per line — a mixed
/// basket where one item is taxable and another is not is the normal case, not an edge.
/// </summary>
public sealed record SaleLineInput(long ProductId, int Qty, decimal UnitPrice, decimal TaxAmount = 0m);

public sealed record CreateSaleRequest(
    long ShopId,
    long CustomerId,
    decimal Discount,
    decimal PaidNow,
    PaymentMethod PaymentMethod,
    string? Note,
    IReadOnlyList<SaleLineInput> Items);

public sealed record VoidInvoiceRequest(string Reason);

public sealed record CustomerPaymentDto
{
    public long Id { get; init; }
    public DateTime ReceivedAt { get; init; }
    public long ShopId { get; init; }
    public string ShopName { get; init; } = string.Empty;
    public long CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? InvoiceNo { get; init; }
    public decimal Amount { get; init; }
    public PaymentMethod Method { get; init; }
    public string? Note { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

public sealed record CreateCustomerPaymentRequest(
    long ShopId,
    long CustomerId,
    long? SalesInvoiceId,
    DateTime ReceivedAt,
    decimal Amount,
    PaymentMethod Method,
    string? Note);

public sealed class SalesService(
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
    public static QuerySpec<InvoiceListDto> Spec { get; } = new QuerySpec<InvoiceListDto>()
        .Map("id", i => i.Id, "Id")
        .Text("invoiceNo", i => i.InvoiceNo, "Invoice no")
        .Map("invoiceDate", i => i.InvoiceDate, "Date")
        .Text("shopName", i => i.ShopName, "Shop")
        .Text("customerName", i => i.CustomerName, "Customer")
        .Map("subTotal", i => i.SubTotal, "Sub total")
        .Map("discount", i => i.Discount, "Discount")
        .Map("taxTotal", i => i.TaxTotal, "Tax")
        .Map("total", i => i.Total, "Total")
        .Map("paidAmount", i => i.PaidAmount, "Paid")
        .Map("outstanding", i => i.Outstanding, "Outstanding")
        .Map("paymentMethod", i => i.PaymentMethod, "Method")
        .Map("status", i => i.Status, "Status")
        .Map("itemCount", i => i.ItemCount, "Lines")
        .Text("createdBy", i => i.CreatedBy, "By");

    public static QuerySpec<CustomerPaymentDto> PaymentSpec { get; } = new QuerySpec<CustomerPaymentDto>()
        .Map("id", p => p.Id, "Id")
        .Map("receivedAt", p => p.ReceivedAt, "Date")
        .Text("shopName", p => p.ShopName, "Shop")
        .Text("customerName", p => p.CustomerName, "Customer")
        .Text("invoiceNo", p => p.InvoiceNo, "Against invoice")
        .Map("amount", p => p.Amount, "Amount")
        .Map("method", p => p.Method, "Method")
        .Text("note", p => p.Note, "Note")
        .Text("createdBy", p => p.CreatedBy, "By");

    public async Task<PagedResult<InvoiceListDto>> QueryAsync(QueryRequest request, CancellationToken ct = default)
        => await QueryRunner
            .RunAsync(await InvoicesAsync(request.ShopId, ct).ConfigureAwait(false), request, Spec, ct)
            .ConfigureAwait(false);

    public async Task<byte[]> ExportAsync(QueryRequest request, CancellationToken ct = default)
    {
        var source = await InvoicesAsync(request.ShopId, ct).ConfigureAwait(false);
        var rows = await QueryRunner.RunForExportAsync(source, request, Spec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.Invoices, AuditAction.Export, nameof(SalesInvoice), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Invoices", rows, Spec, request.Columns);
    }

    public async Task<PagedResult<CustomerPaymentDto>> QueryPaymentsAsync(QueryRequest request, CancellationToken ct = default)
        => await QueryRunner
            .RunAsync(await PaymentsAsync(request.ShopId, ct).ConfigureAwait(false), request, PaymentSpec, ct)
            .ConfigureAwait(false);

    public async Task<byte[]> ExportPaymentsAsync(QueryRequest request, CancellationToken ct = default)
    {
        var source = await PaymentsAsync(request.ShopId, ct).ConfigureAwait(false);
        var rows = await QueryRunner.RunForExportAsync(source, request, PaymentSpec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.Invoices, AuditAction.Export, nameof(CustomerPayment), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Customer payments", rows, PaymentSpec, request.Columns);
    }

    public async Task<InvoiceDetailDto> GetAsync(long id, CancellationToken ct = default)
    {
        var shopIds = await shopScope.ResolveAsync(null, ct).ConfigureAwait(false);

        var invoice = await db.SalesInvoices
            .AsNoTracking()
            .Include(i => i.Items).ThenInclude(x => x.Product)
            .Include(i => i.Shop)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted && shopIds.Contains(i.ShopId), ct)
            .ConfigureAwait(false) ?? throw new NotFoundException(nameof(SalesInvoice), id);

        return new InvoiceDetailDto(
            invoice.Id,
            invoice.InvoiceNo,
            invoice.InvoiceDate,
            invoice.ShopId,
            invoice.Shop.Name,
            invoice.Shop.Address,
            invoice.Shop.Phone,
            invoice.CustomerId,
            invoice.Customer.Name,
            invoice.Customer.Phone,
            invoice.SubTotal,
            invoice.Discount,
            invoice.TaxTotal,
            invoice.Total,
            invoice.PaidAmount,
            invoice.Status == InvoiceStatus.Void ? 0m : invoice.Total - invoice.PaidAmount,
            invoice.PaymentMethod,
            invoice.Status,
            invoice.Note,
            invoice.VoidReason,
            invoice.CreatedBy,
            invoice.Items
                .Select(x => new InvoiceItemDto
    {
        ProductId = x.ProductId,
        Sku = x.Product.Sku,
        ProductName = x.Product.Name,
        Qty = x.Qty,
        UnitPrice = x.UnitPrice,
        TaxAmount = x.TaxAmount,
        LineTotal = x.Qty * x.UnitPrice
    })
                .ToList());
    }

    public async Task<InvoiceDetailDto> CreateSaleAsync(CreateSaleRequest request, CancellationToken ct = default)
    {
        await shopScope.EnsureWritableAsync(request.ShopId, ct).ConfigureAwait(false);

        if (request.Items.Count == 0)
        {
            throw ValidationFailedException.ForField(nameof(request.Items), "The cart is empty.");
        }

        if (request.Items.Any(i => i.Qty <= 0))
        {
            throw ValidationFailedException.ForField(nameof(request.Items), "Every line needs a quantity above zero.");
        }

        if (request.Items.Any(i => i.UnitPrice < 0))
        {
            throw ValidationFailedException.ForField(nameof(request.Items), "Unit price cannot be negative.");
        }

        if (request.Items.Any(i => i.TaxAmount < 0))
        {
            throw ValidationFailedException.ForField(nameof(request.Items), "Tax cannot be negative.");
        }

        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId && !c.IsDeleted, ct)
            .ConfigureAwait(false)
            ?? throw ValidationFailedException.ForField(nameof(request.CustomerId), "Pick a valid customer.");

        var subTotal = request.Items.Sum(i => i.Qty * i.UnitPrice);

        if (request.Discount < 0)
        {
            throw ValidationFailedException.ForField(nameof(request.Discount), "Discount cannot be negative.");
        }

        if (request.Discount > subTotal)
        {
            throw ValidationFailedException.ForField(
                nameof(request.Discount),
                "Discount cannot be larger than the cart total.");
        }

        // Tax is added after the discount, on top of the net goods value. The discount
        // is an invoice-level reduction and is not apportioned back across the lines,
        // so line tax stays exactly the figure the operator saw on the till.
        var taxTotal = request.Items.Sum(i => i.TaxAmount);
        var total = subTotal - request.Discount + taxTotal;

        if (request.PaidNow < 0 || request.PaidNow > total)
        {
            throw ValidationFailedException.ForField(
                nameof(request.PaidNow),
                "Amount paid must be between zero and the invoice total.");
        }

        // A walk-in customer settles at the counter, so credit is not offered to them.
        if (customer.IsWalkIn && request.PaidNow < total)
        {
            throw ValidationFailedException.ForField(
                nameof(request.PaidNow),
                "A walk-in sale must be paid in full. Pick a named customer for credit.");
        }

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id, ct)
            .ConfigureAwait(false);

        foreach (var line in request.Items)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                throw ValidationFailedException.ForField(nameof(request.Items), $"Product {line.ProductId} is not available.");
            }

            var onHand = await stock.CurrentQtyAsync(request.ShopId, line.ProductId, ct).ConfigureAwait(false);
            var wanted = request.Items.Where(i => i.ProductId == line.ProductId).Sum(i => i.Qty);

            if (onHand < wanted)
            {
                throw ValidationFailedException.ForField(
                    nameof(request.Items),
                    $"{product.Name} has {onHand} in stock but the cart asks for {wanted}.");
            }
        }

        await using var tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);

        var invoice = new SalesInvoice
        {
            InvoiceNo = await numbers.NextInvoiceNoAsync(request.ShopId, ct).ConfigureAwait(false),
            ShopId = request.ShopId,
            CustomerId = request.CustomerId,
            InvoiceDate = clock.UtcNow,
            SubTotal = subTotal,
            Discount = request.Discount,
            TaxTotal = taxTotal,
            Total = total,
            PaidAmount = request.PaidNow,
            PaymentMethod = request.PaymentMethod,
            Status = StatusFor(total, request.PaidNow),
            Note = request.Note,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        foreach (var line in request.Items)
        {
            invoice.Items.Add(new SalesItem
            {
                ProductId = line.ProductId,
                Qty = line.Qty,
                UnitPrice = line.UnitPrice,
                TaxAmount = line.TaxAmount,

                // Cost is frozen on the line so later price changes never move historical COGS.
                UnitCost = products[line.ProductId].CostPrice
            });
        }

        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (var line in request.Items)
        {
            await stock.ApplyAsync(
                request.ShopId,
                line.ProductId,
                -line.Qty,
                StockMovementType.Sale,
                products[line.ProductId].CostPrice,
                invoice.InvoiceNo,
                reason: null,
                ct).ConfigureAwait(false);
        }

        if (request.PaidNow > 0)
        {
            db.CustomerPayments.Add(new CustomerPayment
            {
                ShopId = request.ShopId,
                CustomerId = request.CustomerId,
                SalesInvoiceId = invoice.Id,
                ReceivedAt = invoice.InvoiceDate,
                Amount = request.PaidNow,
                Method = request.PaymentMethod,
                Note = $"Received with invoice {invoice.InvoiceNo}",
                CreatedAt = clock.UtcNow,
                CreatedBy = currentUser.Username
            });

            if (request.PaymentMethod == PaymentMethod.Cash)
            {
                db.PettyCashEntries.Add(new PettyCashEntry
                {
                    ShopId = request.ShopId,
                    EntryDate = invoice.InvoiceDate,
                    Direction = PettyCashDirection.In,
                    Amount = request.PaidNow,
                    Note = $"Cash sale {invoice.InvoiceNo}",
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
            Module.Pos,
            AuditAction.Create,
            nameof(SalesInvoice),
            invoice.Id,
            $"{invoice.InvoiceNo} total {total:0.00} paid {request.PaidNow:0.00}",
            request.ShopId,
            ct: ct).ConfigureAwait(false);

        return await GetAsync(invoice.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Admin only. Reverses the stock movements and the money recorded against the invoice.</summary>
    public async Task<InvoiceDetailDto> VoidAsync(long id, VoidInvoiceRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only an admin can void an invoice.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw ValidationFailedException.ForField(nameof(request.Reason), "A reason is required to void an invoice.");
        }

        var invoice = await db.SalesInvoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, ct)
            .ConfigureAwait(false) ?? throw new NotFoundException(nameof(SalesInvoice), id);

        if (invoice.Status == InvoiceStatus.Void)
        {
            throw new ConflictException("That invoice is already void.");
        }

        await using var tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var item in invoice.Items)
        {
            await stock.ApplyAsync(
                invoice.ShopId,
                item.ProductId,
                item.Qty,
                StockMovementType.SaleVoid,
                item.UnitCost,
                invoice.InvoiceNo,
                $"Void: {request.Reason}",
                ct).ConfigureAwait(false);
        }

        // Receipts against a void invoice are reversed as well, including the cash drawer.
        var receipts = await db.CustomerPayments
            .Where(p => p.SalesInvoiceId == id && !p.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var receipt in receipts)
        {
            receipt.IsDeleted = true;
            receipt.UpdatedAt = clock.UtcNow;
            receipt.UpdatedBy = currentUser.Username;

            if (receipt.Method == PaymentMethod.Cash)
            {
                db.PettyCashEntries.Add(new PettyCashEntry
                {
                    ShopId = invoice.ShopId,
                    EntryDate = clock.UtcNow,
                    Direction = PettyCashDirection.Out,
                    Amount = receipt.Amount,
                    Note = $"Reversal of void invoice {invoice.InvoiceNo}",
                    IsSystemGenerated = true,
                    CreatedAt = clock.UtcNow,
                    CreatedBy = currentUser.Username
                });
            }
        }

        invoice.Status = InvoiceStatus.Void;
        invoice.PaidAmount = 0m;
        invoice.VoidedAt = clock.UtcNow;
        invoice.VoidedBy = currentUser.Username;
        invoice.VoidReason = request.Reason.Trim();
        invoice.UpdatedAt = clock.UtcNow;
        invoice.UpdatedBy = currentUser.Username;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.Invoices,
            AuditAction.Void,
            nameof(SalesInvoice),
            id,
            $"{invoice.InvoiceNo} voided: {invoice.VoidReason}",
            invoice.ShopId,
            ct: ct).ConfigureAwait(false);

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<CustomerPaymentDto> ReceiveAsync(CreateCustomerPaymentRequest request, CancellationToken ct = default)
    {
        await shopScope.EnsureWritableAsync(request.ShopId, ct).ConfigureAwait(false);

        if (request.Amount <= 0)
        {
            throw ValidationFailedException.ForField(nameof(request.Amount), "Enter an amount above zero.");
        }

        var owed = await db.SalesInvoices
            .Where(i => i.CustomerId == request.CustomerId && i.ShopId == request.ShopId
                && !i.IsDeleted && i.Status != InvoiceStatus.Void)
            .SumAsync(i => i.Total - i.PaidAmount, ct)
            .ConfigureAwait(false);

        if (request.Amount > owed)
        {
            throw ValidationFailedException.ForField(
                nameof(request.Amount),
                $"That customer owes {owed:0.00}, so the receipt is too large.");
        }

        await using var tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);

        var payment = new CustomerPayment
        {
            ShopId = request.ShopId,
            CustomerId = request.CustomerId,
            SalesInvoiceId = request.SalesInvoiceId,
            ReceivedAt = DateTime.SpecifyKind(request.ReceivedAt, DateTimeKind.Utc),
            Amount = request.Amount,
            Method = request.Method,
            Note = request.Note,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        db.CustomerPayments.Add(payment);

        if (request.SalesInvoiceId is { } invoiceId)
        {
            var invoice = await db.SalesInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId && !i.IsDeleted, ct)
                .ConfigureAwait(false) ?? throw new NotFoundException(nameof(SalesInvoice), invoiceId);

            if (invoice.Status == InvoiceStatus.Void)
            {
                throw new ConflictException("A void invoice cannot take a payment.");
            }

            invoice.PaidAmount += request.Amount;
            invoice.Status = StatusFor(invoice.Total, invoice.PaidAmount);
        }
        else
        {
            await AllocateOldestFirstAsync(request.CustomerId, request.ShopId, request.Amount, ct).ConfigureAwait(false);
        }

        if (request.Method == PaymentMethod.Cash)
        {
            db.PettyCashEntries.Add(new PettyCashEntry
            {
                ShopId = request.ShopId,
                EntryDate = payment.ReceivedAt,
                Direction = PettyCashDirection.In,
                Amount = request.Amount,
                Note = $"Customer receipt {request.Amount:0.00}",
                IsSystemGenerated = true,
                CreatedAt = clock.UtcNow,
                CreatedBy = currentUser.Username
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.Customers,
            AuditAction.Create,
            nameof(CustomerPayment),
            payment.Id,
            $"{request.Amount:0.00} via {request.Method}",
            request.ShopId,
            ct: ct).ConfigureAwait(false);

        var source = await PaymentsAsync(null, ct).ConfigureAwait(false);
        return await source.FirstAsync(p => p.Id == payment.Id, ct).ConfigureAwait(false);
    }

    private static InvoiceStatus StatusFor(decimal total, decimal paid)
        => paid <= 0m ? InvoiceStatus.Unpaid : paid >= total ? InvoiceStatus.Paid : InvoiceStatus.Partial;

    /// <summary>
    /// Payments without an invoice clear the oldest open invoices first. The customer-wise
    /// ageing view uses the same rule, which is what keeps the two ageing views equal.
    /// </summary>
    private async Task AllocateOldestFirstAsync(long customerId, long shopId, decimal amount, CancellationToken ct)
    {
        var open = await db.SalesInvoices
            .Where(i => i.CustomerId == customerId && i.ShopId == shopId && !i.IsDeleted
                && i.Status != InvoiceStatus.Void && i.PaidAmount < i.Total)
            .OrderBy(i => i.InvoiceDate)
            .ThenBy(i => i.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var left = amount;
        foreach (var invoice in open)
        {
            if (left <= 0)
            {
                break;
            }

            var due = invoice.Total - invoice.PaidAmount;
            var take = Math.Min(due, left);
            invoice.PaidAmount += take;
            invoice.Status = StatusFor(invoice.Total, invoice.PaidAmount);
            left -= take;
        }
    }

    private async Task<IQueryable<InvoiceListDto>> InvoicesAsync(long? shopId, CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        return db.SalesInvoices
            .AsNoTracking()
            .Where(i => !i.IsDeleted && shopIds.Contains(i.ShopId))
            .Select(i => new InvoiceListDto
    {
        Id = i.Id,
        InvoiceNo = i.InvoiceNo,
        InvoiceDate = i.InvoiceDate,
        ShopId = i.ShopId,
        ShopName = i.Shop.Name,
        CustomerId = i.CustomerId,
        CustomerName = i.Customer.Name,
        SubTotal = i.SubTotal,
        Discount = i.Discount,
        TaxTotal = i.TaxTotal,
        Total = i.Total,
        PaidAmount = i.PaidAmount,
        Outstanding = i.Status == InvoiceStatus.Void ? 0m : i.Total - i.PaidAmount,
        PaymentMethod = i.PaymentMethod,
        Status = i.Status,
        ItemCount = i.Items.Count,
        CreatedBy = i.CreatedBy
    });
    }

    private async Task<IQueryable<CustomerPaymentDto>> PaymentsAsync(long? shopId, CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        return db.CustomerPayments
            .AsNoTracking()
            .Where(p => !p.IsDeleted && shopIds.Contains(p.ShopId))
            .Select(p => new CustomerPaymentDto
    {
        Id = p.Id,
        ReceivedAt = p.ReceivedAt,
        ShopId = p.ShopId,
        ShopName = p.Shop.Name,
        CustomerId = p.CustomerId,
        CustomerName = p.Customer.Name,
        InvoiceNo = p.SalesInvoice != null ? p.SalesInvoice.InvoiceNo : null,
        Amount = p.Amount,
        Method = p.Method,
        Note = p.Note,
        CreatedBy = p.CreatedBy
    });
    }
}
