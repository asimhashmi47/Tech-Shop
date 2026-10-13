using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Users;

public sealed record UserListDto
{
    public long Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public UserRole Role { get; init; }
    public bool IsActive { get; init; }
    public string Shops { get; init; } = string.Empty;
    public DateTime? LastLoginAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record PermissionInput(Module Module, bool CanRead, bool CanWrite);

public sealed record UserDetailDto(
    long Id,
    string Username,
    string FullName,
    UserRole Role,
    bool IsActive,
    IReadOnlyList<long> ShopIds,
    IReadOnlyList<PermissionInput> Permissions);

public sealed record SaveUserRequest(
    string Username,
    string FullName,
    UserRole Role,
    bool IsActive,
    string? Password,
    IReadOnlyList<long> ShopIds,
    IReadOnlyList<PermissionInput> Permissions);

/// <summary>Admin only. Creates standard users, assigns shops and the per-module access matrix.</summary>
public sealed class UserService(
    IAppDbContext db,
    IPasswordHasher hasher,
    IAuditService audit,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IExcelExportService excel,
    IDateTimeProvider clock)
{
    public static QuerySpec<UserListDto> Spec { get; } = new QuerySpec<UserListDto>()
        .Map("id", u => u.Id, "Id")
        .Text("username", u => u.Username, "Username")
        .Text("fullName", u => u.FullName, "Full name")
        .Map("role", u => u.Role, "Role")
        .Map("isActive", u => u.IsActive, "Active")
        .Text("shops", u => u.Shops, "Shops")
        .Map("lastLoginAt", u => u.LastLoginAt, "Last login")
        .Map("createdAt", u => u.CreatedAt, "Created");

    public async Task<PagedResult<UserListDto>> QueryAsync(QueryRequest request, CancellationToken ct = default)
        => QueryRunner.RunInMemory(await ProjectedAsync(ct).ConfigureAwait(false), request, Spec);

    public async Task<byte[]> ExportAsync(QueryRequest request, CancellationToken ct = default)
    {
        var rows = QueryRunner.RunInMemoryForExport(await ProjectedAsync(ct).ConfigureAwait(false), request, Spec);
        await audit.LogAsync(Module.Users, AuditAction.Export, nameof(AppUser), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Users", rows, Spec, request.Columns);
    }

    public async Task<UserDetailDto> GetAsync(long id, CancellationToken ct = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.UserShops)
            .Include(u => u.Permissions)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct)
            .ConfigureAwait(false) ?? throw new NotFoundException(nameof(AppUser), id);

        return new UserDetailDto(
            user.Id,
            user.Username,
            user.FullName,
            user.Role,
            user.IsActive,
            user.UserShops.Select(us => us.ShopId).ToList(),
            user.Permissions.Select(p => new PermissionInput(p.Module, p.CanRead, p.CanWrite)).ToList());
    }

    public async Task<UserDetailDto> CreateAsync(SaveUserRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            throw ValidationFailedException.ForField(nameof(request.Password), "Use at least 8 characters.");
        }

        var username = request.Username.Trim();
        if (await db.Users.AnyAsync(u => u.Username == username && !u.IsDeleted, ct).ConfigureAwait(false))
        {
            throw ValidationFailedException.ForField(nameof(request.Username), "That username is already taken.");
        }

        var user = new AppUser
        {
            Username = username,
            FullName = request.FullName.Trim(),
            Role = request.Role,
            IsActive = request.IsActive,
            PasswordHash = hasher.Hash(request.Password),
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await ApplyAssignmentsAsync(user, request, ct).ConfigureAwait(false);

        await audit.LogAsync(Module.Users, AuditAction.Create, nameof(AppUser), user.Id, user.Username, ct: ct)
            .ConfigureAwait(false);

        return await GetAsync(user.Id, ct).ConfigureAwait(false);
    }

    public async Task<UserDetailDto> UpdateAsync(long id, SaveUserRequest request, CancellationToken ct = default)
    {
        var user = await db.Users
            .Include(u => u.UserShops)
            .Include(u => u.Permissions)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct)
            .ConfigureAwait(false) ?? throw new NotFoundException(nameof(AppUser), id);

        var username = request.Username.Trim();
        if (await db.Users.AnyAsync(u => u.Username == username && u.Id != id && !u.IsDeleted, ct).ConfigureAwait(false))
        {
            throw ValidationFailedException.ForField(nameof(request.Username), "That username is already taken.");
        }

        user.Username = username;
        user.FullName = request.FullName.Trim();
        user.Role = request.Role;
        user.IsActive = request.IsActive;
        user.UpdatedAt = clock.UtcNow;
        user.UpdatedBy = currentUser.Username;

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            if (request.Password.Length < 8)
            {
                throw ValidationFailedException.ForField(nameof(request.Password), "Use at least 8 characters.");
            }

            user.PasswordHash = hasher.Hash(request.Password);
        }

        await ApplyAssignmentsAsync(user, request, ct).ConfigureAwait(false);

        await audit.LogAsync(
            Module.Users,
            AuditAction.PermissionChange,
            nameof(AppUser),
            user.Id,
            $"shops={request.ShopIds.Count}, modules={request.Permissions.Count(p => p.CanRead || p.CanWrite)}",
            ct: ct).ConfigureAwait(false);

        return await GetAsync(user.Id, ct).ConfigureAwait(false);
    }

    public async Task DeactivateAsync(long id, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(AppUser), id);

        if (user.Role == UserRole.Admin)
        {
            throw new ConflictException("The admin account cannot be deactivated.");
        }

        user.IsActive = false;
        user.UpdatedAt = clock.UtcNow;
        user.UpdatedBy = currentUser.Username;

        // The web original also revoked the user's refresh tokens here. A desktop
        // session holds no token: the next permission check reads IsActive, and
        // invalidating the cache below is what makes that take effect immediately.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        permissions.Invalidate(id);

        await audit.LogAsync(Module.Users, AuditAction.Delete, nameof(AppUser), id, user.Username, ct: ct)
            .ConfigureAwait(false);
    }

    private async Task ApplyAssignmentsAsync(AppUser user, SaveUserRequest request, CancellationToken ct)
    {
        var existingShops = await db.UserShops.Where(us => us.UserId == user.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in existingShops)
        {
            db.UserShops.Remove(row);
        }

        foreach (var shopId in request.ShopIds.Distinct())
        {
            db.UserShops.Add(new UserShop { UserId = user.Id, ShopId = shopId });
        }

        var existingPerms = await db.UserPermissions.Where(p => p.UserId == user.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in existingPerms)
        {
            db.UserPermissions.Remove(row);
        }

        foreach (var perm in request.Permissions)
        {
            if (!perm.CanRead && !perm.CanWrite)
            {
                continue;
            }

            db.UserPermissions.Add(new UserPermission
            {
                UserId = user.Id,
                Module = perm.Module,

                // Write implies Read, so a write-only checkbox still grants read.
                CanRead = perm.CanRead || perm.CanWrite,
                CanWrite = perm.CanWrite
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        permissions.Invalidate(user.Id);
    }

    /// <summary>
    /// The user list is tiny (one admin plus a handful of staff), so it is materialised and
    /// the Shops column joined in memory. SQLite has no row concatenation to translate to.
    /// </summary>
    private async Task<List<UserListDto>> ProjectedAsync(CancellationToken ct)
    {
        var rows = await db.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.FullName,
                u.Role,
                u.IsActive,
                ShopNames = u.UserShops.Select(us => us.Shop.Name).ToList(),
                u.LastLoginAt,
                u.CreatedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Select(u => new UserListDto
    {
        Id = u.Id,
        Username = u.Username,
        FullName = u.FullName,
        Role = u.Role,
        IsActive = u.IsActive,
        Shops = u.Role == UserRole.Admin ? "All shops" : string.Join(", ", u.ShopNames.Order()),
        LastLoginAt = u.LastLoginAt,
        CreatedAt = u.CreatedAt
    })
            .ToList();
    }
}
