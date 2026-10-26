using TechShop.Application.Common.Interfaces;
using TechShop.Domain.Enums;

namespace TechShop.Infrastructure.Services;

/// <summary>
/// The signed-in operator for this process. The web original rebuilt a principal from the
/// bearer token on every request; a desktop app signs in once, so the identity lives here
/// as a singleton and the services below read it through <see cref="ICurrentUser"/>.
/// Sign-out clears it, which is what makes the shell able to return to the login screen
/// without tearing down the host.
/// </summary>
public sealed class UserSession : ICurrentUser
{
    public long? UserId { get; private set; }

    public string Username { get; private set; } = "system";

    public string FullName { get; private set; } = string.Empty;

    public UserRole? Role { get; private set; }

    public bool IsAdmin => Role == UserRole.Admin;

    public bool IsAuthenticated => UserId is not null;

    /// <summary>
    /// How long the session may sit untouched. Fifteen minutes is long enough to deal
    /// with a delivery or a phone call and short enough that a till left over a lunch
    /// break is not still signed in when someone else wanders past.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;

    public bool IsIdle => IsAuthenticated && DateTime.UtcNow - LastActivityUtc >= IdleTimeout;

    /// <summary>
    /// Called from the shell's message filter on every keypress and click. Real work at
    /// the till is what keeps the session alive; a timer firing on its own is not.
    /// </summary>
    public void Touch() => LastActivityUtc = DateTime.UtcNow;

    public string Machine { get; } = SafeMachineName();

    /// <summary>
    /// Stamped onto every audit row written in one sitting, so a session's actions can be
    /// pulled back out of the log together.
    /// </summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Shop ids this user may touch, cached on the session after sign-in.</summary>
    public IReadOnlyList<long> AllowedShopIds { get; private set; } = [];

    /// <summary>The shop the shell's picker currently has selected. Null means all shops.</summary>
    public long? ActiveShopId { get; set; }

    public event EventHandler? Changed;

    public void SignIn(long userId, string username, string fullName, UserRole role, IReadOnlyList<long> allowedShopIds)
    {
        UserId = userId;
        Username = username;
        FullName = fullName;
        Role = role;
        AllowedShopIds = allowedShopIds;
        ActiveShopId = allowedShopIds.Count == 1 ? allowedShopIds[0] : null;
        CorrelationId = Guid.NewGuid().ToString("n")[..12];
        LastActivityUtc = DateTime.UtcNow;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        UserId = null;
        Username = "system";
        FullName = string.Empty;
        Role = null;
        AllowedShopIds = [];
        ActiveShopId = null;
        CorrelationId = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string SafeMachineName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            return "unknown";
        }
    }
}
