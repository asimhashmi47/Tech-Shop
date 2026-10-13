using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Auth;

/// <summary>
/// Sign-in for a desktop till.
///
/// The web original issued a JWT access token and a server-side refresh token, because a
/// browser holds nothing the API can trust. Here the process itself is the session: a
/// successful sign-in hands the profile back to the shell, which puts it on the shared
/// <see cref="ICurrentUser"/>. There is no token to steal, refresh or revoke, so those
/// paths are gone — but the failed-login audit row, the active-account check and the
/// password rules are the same, because those protect the shop, not the transport.
/// </summary>
public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher hasher,
    IPermissionService permissions,
    IAuditService audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
{
    public async Task<UserProfile> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var username = (request.Username ?? string.Empty).Trim();

        var user = await db.Users
            .Include(u => u.UserShops).ThenInclude(us => us.Shop)
            .Include(u => u.Permissions)
            .FirstOrDefaultAsync(u => u.Username == username && !u.IsDeleted, ct)
            .ConfigureAwait(false);

        if (user is null || !user.IsActive || !hasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
        {
            await audit.LogAsync(
                Module.Users,
                AuditAction.LoginFailed,
                nameof(AppUser),
                user?.Id,
                $"Failed login for '{username}'",
                usernameOverride: username,
                ct: ct).ConfigureAwait(false);

            // One message for both cases, so the form cannot be used to discover which
            // usernames exist.
            throw new ForbiddenException("Invalid username or password.");
        }

        user.LastLoginAt = clock.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var profile = await BuildProfileAsync(user, ct).ConfigureAwait(false);

        await audit.LogAsync(
            Module.Users,
            AuditAction.Login,
            nameof(AppUser),
            user.Id,
            $"Login for '{user.Username}'",
            usernameOverride: user.Username,
            ct: ct).ConfigureAwait(false);

        return profile;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is null)
        {
            return;
        }

        await audit.LogAsync(Module.Users, AuditAction.Logout, nameof(AppUser), currentUser.UserId, ct: ct)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Not signed in.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        if (!hasher.Verify(request.CurrentPassword ?? string.Empty, user.PasswordHash))
        {
            throw ValidationFailedException.ForField(
                nameof(request.CurrentPassword),
                "The current password is not correct.");
        }

        var replacement = request.NewPassword ?? string.Empty;

        if (replacement.Length < 8)
        {
            throw ValidationFailedException.ForField(nameof(request.NewPassword), "Use at least 8 characters.");
        }

        if (hasher.Verify(replacement, user.PasswordHash))
        {
            throw ValidationFailedException.ForField(
                nameof(request.NewPassword),
                "The new password has to be different from the current one.");
        }

        user.PasswordHash = hasher.Hash(replacement);
        user.UpdatedAt = clock.UtcNow;
        user.UpdatedBy = currentUser.Username;

        await audit.LogAsync(Module.Users, AuditAction.PasswordChange, nameof(AppUser), userId, ct: ct)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<UserProfile> MeAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Not signed in.");
        var user = await db.Users
            .Include(u => u.UserShops).ThenInclude(us => us.Shop)
            .Include(u => u.Permissions)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            .ConfigureAwait(false) ?? throw new NotFoundException(nameof(AppUser), userId);

        return await BuildProfileAsync(user, ct).ConfigureAwait(false);
    }

    private async Task<UserProfile> BuildProfileAsync(AppUser user, CancellationToken ct)
    {
        var shopIds = await permissions.AllowedShopIdsAsync(user.Id, ct).ConfigureAwait(false);
        var shops = await db.Shops
            .Where(s => shopIds.Contains(s.Id) && s.IsActive && !s.IsDeleted)
            .OrderBy(s => s.Name)
            .Select(s => new ShopRef(s.Id, s.Code, s.Name))
            .ToListAsync(ct).ConfigureAwait(false);

        var map = await permissions.GetPermissionsAsync(user.Id, ct).ConfigureAwait(false);
        var perms = map
            .Select(kv => new ModulePermission(kv.Key, kv.Value.Read, kv.Value.Write))
            .OrderBy(p => p.Module)
            .ToList();

        return new UserProfile(user.Id, user.Username, user.FullName, user.Role, shops, perms);
    }
}
