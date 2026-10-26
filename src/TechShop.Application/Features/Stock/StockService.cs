using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Stock;

public sealed record StockLevelDto
{
    public long ProductId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public long ShopId { get; init; }
    public string ShopName { get; init; } = string.Empty;
    public int Qty { get; init; }
    public int ReorderLevel { get; init; }
    public decimal CostPrice { get; init; }
    public decimal StockValue { get; init; }
    public bool IsLow { get; init; }
}

public sealed record StockMovementDto
{
    public long Id { get; init; }
    public DateTime MovedAt { get; init; }
    public long ShopId { get; init; }
    public string ShopName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public StockMovementType Type { get; init; }
    public int Qty { get; init; }
    public decimal UnitCost { get; init; }
    public string? Reference { get; init; }
    public string? Reason { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

public sealed record AdjustStockRequest(long ShopId, long ProductId, int Qty, string Reason);

public sealed record TransferStockRequest(long FromShopId, long ToShopId, long ProductId, int Qty, string? Note);

public sealed record OpeningStockRequest(long ShopId, long ProductId, int Qty);

/// <summary>
/// Single place where stock changes. Every change writes a StockMovement row and keeps the
/// StockLevel cache row in step, so quantity always equals the sum of its movements.
/// </summary>
public sealed class StockService(
    IAppDbContext db,
    IAuditService audit,
    ICurrentUser currentUser,
    IShopScope shopScope,
    ICacheService cache,
    IExcelExportService excel,
    IAccessGuard access,
    IDateTimeProvider clock)
{
    public static QuerySpec<StockLevelDto> LevelSpec { get; } = new QuerySpec<StockLevelDto>()
        .Text("sku", s => s.Sku, "SKU")
        .Text("productName", s => s.ProductName, "Product")
        .Text("categoryName", s => s.CategoryName, "Category")
        .Text("shopName", s => s.ShopName, "Shop")
        .Map("qty", s => s.Qty, "Qty")
        .Map("reorderLevel", s => s.ReorderLevel, "Reorder level")
        .Map("costPrice", s => s.CostPrice, "Cost")
        .Map("stockValue", s => s.StockValue, "Stock value")
        .Map("isLow", s => s.IsLow, "Low stock");

    public static QuerySpec<StockMovementDto> MovementSpec { get; } = new QuerySpec<StockMovementDto>()
        .Map("id", m => m.Id, "Id")
        .Map("movedAt", m => m.MovedAt, "Date")
        .Text("shopName", m => m.ShopName, "Shop")
        .Text("sku", m => m.Sku, "SKU")
        .Text("productName", m => m.ProductName, "Product")
        .Map("type", m => m.Type, "Type")
        .Map("qty", m => m.Qty, "Qty")
        .Map("unitCost", m => m.UnitCost, "Unit cost")
        .Text("reference", m => m.Reference, "Reference")
        .Text("reason", m => m.Reason, "Reason")
        .Text("createdBy", m => m.CreatedBy, "By");

    public async Task<PagedResult<StockLevelDto>> QueryLevelsAsync(QueryRequest request, CancellationToken ct = default)
        => await QueryRunner
            .RunAsync(await LevelsAsync(request.ShopId, ct).ConfigureAwait(false), request, LevelSpec, ct)
            .ConfigureAwait(false);

    public async Task<byte[]> ExportLevelsAsync(QueryRequest request, CancellationToken ct = default)
    {
        var source = await LevelsAsync(request.ShopId, ct).ConfigureAwait(false);
        var rows = await QueryRunner.RunForExportAsync(source, request, LevelSpec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.Stock, AuditAction.Export, nameof(StockLevel), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Stock", rows, LevelSpec, request.Columns);
    }

    public async Task<PagedResult<StockMovementDto>> QueryMovementsAsync(QueryRequest request, CancellationToken ct = default)
        => await QueryRunner
            .RunAsync(await MovementsAsync(request.ShopId, ct).ConfigureAwait(false), request, MovementSpec, ct)
            .ConfigureAwait(false);

    public async Task<byte[]> ExportMovementsAsync(QueryRequest request, CancellationToken ct = default)
    {
        var source = await MovementsAsync(request.ShopId, ct).ConfigureAwait(false);
        var rows = await QueryRunner.RunForExportAsync(source, request, MovementSpec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.Stock, AuditAction.Export, nameof(StockMovement), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Stock movements", rows, MovementSpec, request.Columns);
    }

    public async Task<List<StockLevelDto>> LowStockAsync(long? shopId, int take = 10, CancellationToken ct = default)
    {
        var source = await LevelsAsync(shopId, ct).ConfigureAwait(false);
        return await source
            .Where(s => s.IsLow)
            .OrderBy(s => s.Qty)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task SetOpeningAsync(OpeningStockRequest request, CancellationToken ct = default)
    {
        await access.EnsureAsync(Module.Stock, Access.Write, ct).ConfigureAwait(false);

        await shopScope.EnsureWritableAsync(request.ShopId, ct).ConfigureAwait(false);

        if (request.Qty < 0)
        {
            throw ValidationFailedException.ForField(nameof(request.Qty), "Opening quantity cannot be negative.");
        }

        var product = await LoadProductAsync(request.ProductId, ct).ConfigureAwait(false);
        var current = await CurrentQtyAsync(request.ShopId, request.ProductId, ct).ConfigureAwait(false);
        var delta = request.Qty - current;

        if (delta == 0)
        {
            return;
        }

        await ApplyAsync(
            request.ShopId,
            request.ProductId,
            delta,
            StockMovementType.Opening,
            product.CostPrice,
            reference: "Opening stock",
            reason: null,
            ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.Stock,
            AuditAction.Update,
            nameof(StockLevel),
            request.ProductId,
            $"Opening stock set to {request.Qty} for {product.Sku}",
            request.ShopId,
            ct: ct).ConfigureAwait(false);
    }

    public async Task AdjustAsync(AdjustStockRequest request, CancellationToken ct = default)
    {
        await access.EnsureAsync(Module.Stock, Access.Write, ct).ConfigureAwait(false);

        await shopScope.EnsureWritableAsync(request.ShopId, ct).ConfigureAwait(false);

        if (request.Qty == 0)
        {
            throw ValidationFailedException.ForField(nameof(request.Qty), "Enter a non zero quantity.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw ValidationFailedException.ForField(nameof(request.Reason), "A reason is required for every adjustment.");
        }

        var product = await LoadProductAsync(request.ProductId, ct).ConfigureAwait(false);
        var current = await CurrentQtyAsync(request.ShopId, request.ProductId, ct).ConfigureAwait(false);

        if (current + request.Qty < 0)
        {
            throw ValidationFailedException.ForField(
                nameof(request.Qty),
                $"Only {current} in stock, so that adjustment would go negative.");
        }

        await ApplyAsync(
            request.ShopId,
            request.ProductId,
            request.Qty,
            request.Qty > 0 ? StockMovementType.AdjustmentIn : StockMovementType.AdjustmentOut,
            product.CostPrice,
            reference: "Manual adjustment",
            reason: request.Reason.Trim(),
            ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.Stock,
            AuditAction.Update,
            nameof(StockMovement),
            request.ProductId,
            $"{product.Sku} adjusted by {request.Qty}: {request.Reason}",
            request.ShopId,
            ct: ct).ConfigureAwait(false);
    }

    public async Task TransferAsync(TransferStockRequest request, CancellationToken ct = default)
    {
        await access.EnsureAsync(Module.Stock, Access.Write, ct).ConfigureAwait(false);

        if (request.FromShopId == request.ToShopId)
        {
            throw ValidationFailedException.ForField(nameof(request.ToShopId), "Pick a different destination shop.");
        }

        if (request.Qty <= 0)
        {
            throw ValidationFailedException.ForField(nameof(request.Qty), "Enter a quantity greater than zero.");
        }

        await shopScope.EnsureWritableAsync(request.FromShopId, ct).ConfigureAwait(false);
        await shopScope.EnsureWritableAsync(request.ToShopId, ct).ConfigureAwait(false);

        var product = await LoadProductAsync(request.ProductId, ct).ConfigureAwait(false);
        var available = await CurrentQtyAsync(request.FromShopId, request.ProductId, ct).ConfigureAwait(false);

        if (available < request.Qty)
        {
            throw ValidationFailedException.ForField(
                nameof(request.Qty),
                $"Only {available} in stock at the source shop.");
        }

        await using var tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);

        var reference = $"Transfer {product.Sku} x{request.Qty}";
        await ApplyAsync(request.FromShopId, request.ProductId, -request.Qty, StockMovementType.TransferOut,
            product.CostPrice, reference, request.Note, ct).ConfigureAwait(false);
        await ApplyAsync(request.ToShopId, request.ProductId, request.Qty, StockMovementType.TransferIn,
            product.CostPrice, reference, request.Note, ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.Stock,
            AuditAction.Update,
            nameof(StockMovement),
            request.ProductId,
            $"{reference} from shop {request.FromShopId} to shop {request.ToShopId}",
            request.FromShopId,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes one movement and updates the matching level row. Callers own the SaveChanges
    /// so a POS sale or a purchase can bundle several products into one transaction.
    /// </summary>
    public async Task ApplyAsync(
        long shopId,
        long productId,
        int signedQty,
        StockMovementType type,
        decimal unitCost,
        string? reference,
        string? reason,
        CancellationToken ct = default)
    {
        // Every change to the shelf goes through the atomic pair below rather than a
        // read-modify-write on a tracked entity. The old way checked the quantity in
        // one statement and wrote it in another, so concurrent tills each read the
        // same last unit and each sold it, taking stock negative.
        if (signedQty < 0)
        {
            var taken = await db.TryTakeStockAsync(shopId, productId, -signedQty, ct).ConfigureAwait(false);

            if (!taken)
            {
                var onHand = await CurrentQtyAsync(shopId, productId, ct).ConfigureAwait(false);

                throw ValidationFailedException.ForField(
                    "Qty",
                    $"Only {onHand} left in stock, so {-signedQty} cannot be taken.");
            }
        }
        else
        {
            await db.AddStockAsync(shopId, productId, signedQty, ct).ConfigureAwait(false);
        }

        db.StockMovements.Add(new StockMovement
        {
            ShopId = shopId,
            ProductId = productId,
            Type = type,
            Qty = signedQty,
            UnitCost = unitCost,
            MovedAt = clock.UtcNow,
            Reference = reference,
            Reason = reason,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        });
    }

    public Task<int> CurrentQtyAsync(long shopId, long productId, CancellationToken ct = default)
        => db.StockLevels
            .Where(s => s.ShopId == shopId && s.ProductId == productId)
            .Select(s => s.Qty)
            .FirstOrDefaultAsync(ct);

    private async Task<Product> LoadProductAsync(long productId, CancellationToken ct)
        => await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted, ct)
            .ConfigureAwait(false) ?? throw new NotFoundException(nameof(Product), productId);

    private async Task<IQueryable<StockLevelDto>> LevelsAsync(long? shopId, CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        return db.StockLevels
            .AsNoTracking()
            .Where(s => shopIds.Contains(s.ShopId) && !s.Product.IsDeleted)
            .Select(s => new StockLevelDto
    {
        ProductId = s.ProductId,
        Sku = s.Product.Sku,
        ProductName = s.Product.Name,
        CategoryName = s.Product.Category.Name,
        ShopId = s.ShopId,
        ShopName = s.Shop.Name,
        Qty = s.Qty,
        ReorderLevel = s.Product.ReorderLevel,
        CostPrice = s.Product.CostPrice,
        StockValue = s.Qty * s.Product.CostPrice,
        IsLow = s.Qty <= s.Product.ReorderLevel
    });
    }

    private async Task<IQueryable<StockMovementDto>> MovementsAsync(long? shopId, CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        return db.StockMovements
            .AsNoTracking()
            .Where(m => shopIds.Contains(m.ShopId))
            .Select(m => new StockMovementDto
    {
        Id = m.Id,
        MovedAt = m.MovedAt,
        ShopId = m.ShopId,
        ShopName = m.Shop.Name,
        Sku = m.Product.Sku,
        ProductName = m.Product.Name,
        Type = m.Type,
        Qty = m.Qty,
        UnitCost = m.UnitCost,
        Reference = m.Reference,
        Reason = m.Reason,
        CreatedBy = m.CreatedBy
    });
    }
}
