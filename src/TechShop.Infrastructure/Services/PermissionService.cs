using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Domain.Enums;
using TechShop.Infrastructure.Persistence;

namespace TechShop.Infrastructure.Services;

/// <summary>
/// Resolves and caches what a user may do. Admin is implicitly full access on every module
/// and every shop, so no permission rows are stored for the owner account.
/// </summary>
public sealed class PermissionService(AppDbContext db, ICacheService cache) : IPermissionService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    public async Task<bool> HasAccessAsync(long userId, Module module, Access access, CancellationToken ct = default)
    {
        var map = await GetPermissionsAsync(userId, ct).ConfigureAwait(false);

        if (!map.TryGetValue(module, out var entry))
        {
            return false;
        }

        return access == Access.Write ? entry.Write : entry.Read;
    }

    public async Task<IReadOnlyDictionary<Module, (bool Read, bool Write)>> GetPermissionsAsync(
        long userId,
        CancellationToken ct = default)
        => await cache.GetOrCreateAsync<IReadOnlyDictionary<Module, (bool Read, bool Write)>>(
            CacheKeys.Permission(userId),
            async () =>
            {
                var role = await db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => (UserRole?)u.Role)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                if (role == UserRole.Admin)
                {
                    return Enum.GetValues<Module>().ToDictionary(m => m, _ => (true, true));
                }

                var rows = await db.UserPermissions
                    .AsNoTracking()
                    .Where(p => p.UserId == userId)
                    .Select(p => new { p.Module, p.CanRead, p.CanWrite })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                // Users and Audit Log stay admin only, whatever the rows say.
                return rows
                    .Where(p => p.Module != Module.Users && p.Module != Module.AuditLog)
                    .ToDictionary(p => p.Module, p => (p.CanRead || p.CanWrite, p.CanWrite));
            },
            Ttl).ConfigureAwait(false);

    public Task<IReadOnlyList<long>> AllowedShopIdsAsync(long userId, CancellationToken ct = default)
        => cache.GetOrCreateAsync<IReadOnlyList<long>>(
            CacheKeys.AllowedShops(userId),
            async () =>
            {
                var isAdmin = await db.Users
                    .AsNoTracking()
                    .AnyAsync(u => u.Id == userId && u.Role == UserRole.Admin, ct)
                    .ConfigureAwait(false);

                if (isAdmin)
                {
                    return await db.Shops
                        .AsNoTracking()
                        .Where(s => !s.IsDeleted)
                        .Select(s => s.Id)
                        .ToListAsync(ct)
                        .ConfigureAwait(false);
                }

                return await db.UserShops
                    .AsNoTracking()
                    .Where(us => us.UserId == userId && !us.Shop.IsDeleted)
                    .Select(us => us.ShopId)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
            },
            Ttl);

    public void Invalidate(long userId)
    {
        cache.Remove(CacheKeys.Permission(userId));
        cache.Remove(CacheKeys.AllowedShops(userId));
    }
}

/// <summary>
/// Turns a requested shop into the set of shop ids a query may touch. Asking for a shop
/// outside the assignment is refused here, in the query layer, not just hidden in the UI.
/// </summary>
public sealed class ShopScope(ICurrentUser currentUser, IPermissionService permissions) : IShopScope
{
    public async Task<IReadOnlyList<long>> ResolveAsync(long? requestedShopId, CancellationToken ct = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Not signed in.");
        var allowed = await permissions.AllowedShopIdsAsync(userId, ct).ConfigureAwait(false);

        if (requestedShopId is not { } shopId)
        {
            return allowed;
        }

        if (!allowed.Contains(shopId))
        {
            throw new ForbiddenException("That shop is not assigned to your account.");
        }

        return [shopId];
    }

    public async Task EnsureWritableAsync(long shopId, CancellationToken ct = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Not signed in.");
        var allowed = await permissions.AllowedShopIdsAsync(userId, ct).ConfigureAwait(false);

        if (!allowed.Contains(shopId))
        {
            throw new ForbiddenException("That shop is not assigned to your account.");
        }
    }
}

