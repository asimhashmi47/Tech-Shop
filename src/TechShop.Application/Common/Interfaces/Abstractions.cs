using TechShop.Domain.Enums;

namespace TechShop.Application.Common.Interfaces;

/// <summary>
/// The signed-in operator. Unlike the web original this is a single long-lived session
/// per process rather than a per-request principal, so it is mutable: signing in and out
/// swaps the identity that every service below reads.
/// </summary>
public interface ICurrentUser
{
    long? UserId { get; }
    string Username { get; }
    string FullName { get; }
    UserRole? Role { get; }
    bool IsAdmin { get; }
    bool IsAuthenticated { get; }

    /// <summary>Workstation name, written to the audit log in place of the web app's IP.</summary>
    string? Machine { get; }
    string? CorrelationId { get; }
}

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

public interface IPermissionService
{
    Task<bool> HasAccessAsync(long userId, Module module, Access access, CancellationToken ct = default);

    /// <summary>Shop ids the user may read. Admin returns every active shop.</summary>
    Task<IReadOnlyList<long>> AllowedShopIdsAsync(long userId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Module, (bool Read, bool Write)>> GetPermissionsAsync(long userId, CancellationToken ct = default);

    void Invalidate(long userId);
}

public interface IAuditService
{
    Task LogAsync(
        Module module,
        AuditAction action,
        string entity,
        long? entityId = null,
        string? summary = null,
        long? shopId = null,
        string? usernameOverride = null,
        CancellationToken ct = default);
}

public interface ICacheService
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null);
    void Remove(string key);

    /// <summary>Drops every cached entry under a logical group, for example dashboard aggregates.</summary>
    void RemoveGroup(string group);
}

public interface IShopScope
{
    /// <summary>
    /// Resolves the shop ids a call may touch. Throws when the caller asks for a shop
    /// outside its assignment, so scoping is enforced in the service layer and not only
    /// by what the sidebar chooses to show.
    /// </summary>
    Task<IReadOnlyList<long>> ResolveAsync(long? requestedShopId, CancellationToken ct = default);

    Task EnsureWritableAsync(long shopId, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

/// <summary>
/// Builds a workbook from the same field whitelist the grid uses, so an export mirrors
/// the columns and rows the operator is actually looking at.
/// </summary>
public interface IExcelExportService
{
    byte[] Build<T>(string sheetName, IReadOnlyList<T> rows, Query.QuerySpec<T> spec, IEnumerable<string> columns);
}
