using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Shops;

public sealed record ShopDto
{
    public long Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public bool IsActive { get; init; }
}

public sealed record SaveShopRequest(string Code, string Name, string? Address, string? Phone, bool IsActive);

public sealed class ShopService(
    IAppDbContext db,
    IAuditService audit,
    ICurrentUser currentUser,
    IPermissionService permissions,
    ICacheService cache,
    IExcelExportService excel,
    IDateTimeProvider clock)
{
    public static QuerySpec<ShopDto> Spec { get; } = new QuerySpec<ShopDto>()
        .Map("id", s => s.Id, "Id")
        .Text("code", s => s.Code, "Code")
        .Text("name", s => s.Name, "Shop name")
        .Text("address", s => s.Address, "Address")
        .Text("phone", s => s.Phone, "Phone")
        .Map("isActive", s => s.IsActive, "Active");

    public async Task<PagedResult<ShopDto>> QueryAsync(QueryRequest request, CancellationToken ct = default)
        => await QueryRunner.RunAsync(await ScopedAsync(ct).ConfigureAwait(false), request, Spec, ct).ConfigureAwait(false);

    public async Task<byte[]> ExportAsync(QueryRequest request, CancellationToken ct = default)
    {
        var rows = await QueryRunner
            .RunForExportAsync(await ScopedAsync(ct).ConfigureAwait(false), request, Spec, ct)
            .ConfigureAwait(false);
        await audit.LogAsync(Module.Shops, AuditAction.Export, nameof(Shop), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Shops", rows, Spec, request.Columns);
    }

    public async Task<IReadOnlyList<ShopDto>> LookupAsync(CancellationToken ct = default)
    {
        var query = await ScopedAsync(ct).ConfigureAwait(false);
        return await query.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<ShopDto> GetAsync(long id, CancellationToken ct = default)
    {
        var query = await ScopedAsync(ct).ConfigureAwait(false);
        return await query.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Shop), id);
    }

    public async Task<ShopDto> CreateAsync(SaveShopRequest request, CancellationToken ct = default)
    {
        await EnsureCodeFreeAsync(request.Code, null, ct).ConfigureAwait(false);

        var shop = new Shop
        {
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Address = request.Address,
            Phone = request.Phone,
            IsActive = request.IsActive,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        db.Shops.Add(shop);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Lookups);

        // The shop switcher reads each user's allowed shops, which is cached per user.
        // Without this the new shop stays invisible until that entry expires.
        cache.RemoveGroup(CacheKeys.Permissions);

        await audit.LogAsync(Module.Shops, AuditAction.Create, nameof(Shop), shop.Id, shop.Name, shop.Id, ct: ct)
            .ConfigureAwait(false);
        return Map(shop);
    }

    public async Task<ShopDto> UpdateAsync(long id, SaveShopRequest request, CancellationToken ct = default)
    {
        var shop = await db.Shops.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Shop), id);

        await EnsureCodeFreeAsync(request.Code, id, ct).ConfigureAwait(false);

        var before = $"{shop.Code}/{shop.Name}/active={shop.IsActive}";
        shop.Code = request.Code.Trim().ToUpperInvariant();
        shop.Name = request.Name.Trim();
        shop.Address = request.Address;
        shop.Phone = request.Phone;
        shop.IsActive = request.IsActive;
        shop.UpdatedAt = clock.UtcNow;
        shop.UpdatedBy = currentUser.Username;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Lookups);
        cache.RemoveGroup(CacheKeys.Permissions);

        await audit.LogAsync(
            Module.Shops,
            AuditAction.Update,
            nameof(Shop),
            shop.Id,
            $"{before} -> {shop.Code}/{shop.Name}/active={shop.IsActive}",
            shop.Id,
            ct: ct).ConfigureAwait(false);

        return Map(shop);
    }

    private async Task EnsureCodeFreeAsync(string code, long? excludeId, CancellationToken ct)
    {
        var normalised = code.Trim().ToUpperInvariant();
        var taken = await db.Shops
            .AnyAsync(s => s.Code == normalised && !s.IsDeleted && (excludeId == null || s.Id != excludeId), ct)
            .ConfigureAwait(false);

        if (taken)
        {
            throw ValidationFailedException.ForField(nameof(SaveShopRequest.Code), "That shop code is already in use.");
        }
    }

    private async Task<IQueryable<ShopDto>> ScopedAsync(CancellationToken ct)
    {
        var query = db.Shops.AsNoTracking().Where(s => !s.IsDeleted);

        if (!currentUser.IsAdmin && currentUser.UserId is { } userId)
        {
            var allowed = await permissions.AllowedShopIdsAsync(userId, ct).ConfigureAwait(false);
            query = query.Where(s => allowed.Contains(s.Id));
        }

        return query.Select(s => new ShopDto
    {
        Id = s.Id,
        Code = s.Code,
        Name = s.Name,
        Address = s.Address,
        Phone = s.Phone,
        IsActive = s.IsActive
    });
    }

    private static ShopDto Map(Shop s) => new()
    {
        Id = s.Id,
        Code = s.Code,
        Name = s.Name,
        Address = s.Address,
        Phone = s.Phone,
        IsActive = s.IsActive
    };
}
