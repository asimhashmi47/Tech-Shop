using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Domain.Enums;

namespace TechShop.Infrastructure.Services;

/// <summary>
/// The one place a write is allowed or refused. Backed by the same cached permission
/// map the shell uses to build the sidebar, so what a user sees and what a user may do
/// cannot drift apart.
/// </summary>
public sealed class AccessGuard(ICurrentUser currentUser, IPermissionService permissions) : IAccessGuard
{
    public async Task EnsureAsync(Module module, Access access, CancellationToken ct = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Not signed in.");

        // Checked before the permission, so a session that has aged out is refused even
        // where the account would otherwise be entitled to the work.
        if (currentUser.IsIdle)
        {
            throw new ForbiddenException("Your session timed out. Sign in again to carry on.");
        }

        if (!await permissions.HasAccessAsync(userId, module, access, ct).ConfigureAwait(false))
        {
            var verb = access == Access.Write ? "change" : "see";

            throw new ForbiddenException($"Your account is not allowed to {verb} {Describe(module)}.");
        }
    }

    /// <summary>Module names are PascalCase enum members; the message is read by a cashier.</summary>
    private static string Describe(Module module) => module switch
    {
        Module.Pos => "the till",
        Module.AuditLog => "the audit log",
        Module.PettyCash => "petty cash",
        _ => System.Text.RegularExpressions.Regex
            .Replace(module.ToString(), "(?<=[a-z])(?=[A-Z])", " ")
            .ToLowerInvariant(),
    };
}
