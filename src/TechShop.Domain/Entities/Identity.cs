using TechShop.Domain.Common;
using TechShop.Domain.Enums;

namespace TechShop.Domain.Entities;

public class Shop : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<UserShop> UserShops { get; set; } = [];
}

public class AppUser : AuditableEntity
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Standard;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Consecutive failed sign-ins. Reset the moment the right password arrives, so a
    /// cashier who mistypes twice a day never accumulates their way to a lockout.
    /// </summary>
    public int FailedLoginCount { get; set; }

    /// <summary>Set when the count passes the limit; sign-in is refused until it passes.</summary>
    public DateTime? LockedOutUntil { get; set; }

    public ICollection<UserShop> UserShops { get; set; } = [];
    public ICollection<UserPermission> Permissions { get; set; } = [];
}

public class UserShop
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public long ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
}

public class UserPermission
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Module Module { get; set; }
    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }
}

/// <summary>
/// A single application wide setting, stored as a key and a value so the table does not
/// need a migration every time a new option appears. Only an admin can change these.
/// </summary>
public class AppSetting : AuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class AuditLog
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public long? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public long? ShopId { get; set; }
    public Module Module { get; set; }
    public AuditAction Action { get; set; }
    public string Entity { get; set; } = string.Empty;
    public long? EntityId { get; set; }
    public string? Summary { get; set; }
    public string? Machine { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Hash of this row's contents chained to the one before it. The database is a file
    /// on a shop PC, so anyone can open it; chaining means an edited or deleted row
    /// breaks the chain and the tampering can at least be detected.
    /// </summary>
    public string? Hash { get; set; }

    public string? PreviousHash { get; set; }
}
