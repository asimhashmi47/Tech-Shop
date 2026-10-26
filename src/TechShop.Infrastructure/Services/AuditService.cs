using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
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
        var row = new AuditLog
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
        };

        // Chained to the row before it, so editing or deleting any row downstream
        // breaks every hash after it and the tampering shows up in VerifyAsync.
        row.PreviousHash = await LastHashAsync(ct).ConfigureAwait(false);
        row.Hash = HashOf(row, row.PreviousHash);

        db.AuditLogs.Add(row);

        if (action is AuditAction.Login or AuditAction.LoginFailed or AuditAction.Export)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<AuditIntegrityResult> VerifyAsync(CancellationToken ct = default)
    {
        var rows = await db.AuditLogs
            .AsNoTracking()
            .OrderBy(a => a.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? previous = null;

        foreach (var row in rows)
        {
            // Rows written before chaining existed carry no hash. They are passed over
            // rather than reported, so an upgraded database is not permanently "broken".
            if (row.Hash is null)
            {
                continue;
            }

            if (row.PreviousHash != previous)
            {
                return new AuditIntegrityResult(
                    false,
                    rows.Count,
                    $"Entry {row.Id} follows a different entry than the one before it, so a row was removed or reordered.");
            }

            if (HashOf(row, row.PreviousHash) != row.Hash)
            {
                return new AuditIntegrityResult(
                    false,
                    rows.Count,
                    $"Entry {row.Id} does not match its own hash, so its contents were changed.");
            }

            previous = row.Hash;
        }

        return new AuditIntegrityResult(true, rows.Count, null);
    }

    private Task<string?> LastHashAsync(CancellationToken ct)
        => db.AuditLogs
            .OrderByDescending(a => a.Id)
            .Select(a => a.Hash)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Covers every field that carries meaning. Deliberately not the Id, which SQLite
    /// assigns on insert, after the hash has to be computed.
    /// </summary>
    private static string HashOf(AuditLog row, string? previousHash)
    {
        var payload = string.Join(
            '',
            previousHash ?? string.Empty,
            // A fixed pattern, not "O": SQLite hands the timestamp back with its Kind
            // lost, and "O" renders a Utc and an Unspecified DateTime differently, so
            // every row would fail its own hash the moment it was read back.
            row.At.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            row.UserId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.Username,
            row.ShopId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ((int)row.Module).ToString(CultureInfo.InvariantCulture),
            ((int)row.Action).ToString(CultureInfo.InvariantCulture),
            row.Entity,
            row.EntityId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.Summary ?? string.Empty,
            row.Machine ?? string.Empty,
            row.CorrelationId ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string? Trim(string? value)
        => value is null || value.Length <= 1000 ? value : value[..1000];
}

