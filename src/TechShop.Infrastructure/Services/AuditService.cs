using TechShop.Application.Common.Interfaces;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;
using TechShop.Infrastructure.Persistence;

namespace TechShop.Infrastructure.Services;

/// <summary>
/// Single writer for the audit trail. Rows are added to the same change tracker as the
/// action being audited, so a failed save leaves no orphan audit row. Login and export
/// calls save immediately because they have nothing else to commit with.
/// </summary>
public sealed class AuditService(AppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock) : IAuditService
{
    public async Task LogAsync(
        Module module,
        AuditAction action,
        string entity,
        long? entityId = null,
        string? summary = null,
        long? shopId = null,
        string? usernameOverride = null,
        CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            At = clock.UtcNow,
            UserId = currentUser.UserId,
            Username = usernameOverride ?? currentUser.Username,
            ShopId = shopId,
            Module = module,
            Action = action,
            Entity = entity,
            EntityId = entityId,
            Summary = Trim(summary),
            Machine = currentUser.Machine,
            CorrelationId = currentUser.CorrelationId
        });

        if (action is AuditAction.Login or AuditAction.LoginFailed or AuditAction.Export)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private static string? Trim(string? value)
        => value is null || value.Length <= 1000 ? value : value[..1000];
}

