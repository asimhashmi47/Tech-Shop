using Microsoft.Extensions.DependencyInjection;

namespace TechShop.WinForms;

/// <summary>
/// The container, reachable from a form without threading a provider through every
/// constructor.
///
/// Scopes matter here. EF Core's change tracker is not something a long-lived desktop
/// process should share across screens: two forms editing the same entity through one
/// context will fight. So each screen opens its own scope and disposes it on close, and
/// a one-off action such as sign-in takes a scope for the length of the call.
/// </summary>
public static class AppHost
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("The host has not been started yet.");

    public static void Use(IServiceProvider services) => _services = services;

    /// <summary>A scope for one screen, disposed when that screen closes.</summary>
    public static IServiceScope CreateScope() => Services.CreateScope();

    /// <summary>Resolves a process-wide service, such as the signed-in session.</summary>
    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>
    /// Runs one piece of work in its own scope. Used for actions that do not belong to a
    /// screen's unit of work, such as signing in or loading the shell's shop list.
    /// </summary>
    public static async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> work)
        where TService : notnull
    {
        using var scope = CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        return await work(service).ConfigureAwait(true);
    }

    public static async Task InScopeAsync<TService>(Func<TService, Task> work)
        where TService : notnull
    {
        using var scope = CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        await work(service).ConfigureAwait(true);
    }
}
