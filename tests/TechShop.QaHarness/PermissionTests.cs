using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Auth;
using TechShop.Application.Features.Catalog;
using TechShop.Application.Features.Parties;
using TechShop.Domain.Enums;
using TechShop.Infrastructure.Persistence;
using TechShop.Infrastructure.Services;

namespace TechShop.QaHarness;

/// <summary>
/// Checks that permissions are enforced where it matters.
///
/// Hiding a button is a courtesy; the test that counts is whether the service refuses a
/// call from an account that should not be able to make it. A UI-only check would pass
/// even if every screen were one URL away from being wide open.
/// </summary>
internal static class PermissionTests
{
    public static async Task RunAsync(Harness harness, IServiceProvider provider)
    {
        harness.Section("permissions are enforced in the service layer");

        var session = provider.GetRequiredService<UserSession>();

        // zoya is seeded read-only, assigned to the first shop.
        await AsUserAsync(provider, session, "zoya", async sp =>
        {
            var permissions = sp.GetRequiredService<IPermissionService>();
            var userId = session.UserId!.Value;

            harness.Expect(
                "read-only account may read products",
                await permissions.HasAccessAsync(userId, Module.Products, Access.Read).ConfigureAwait(true),
                "zoya cannot read products, but is seeded with read access");

            harness.Expect(
                "read-only account may not write products",
                !await permissions.HasAccessAsync(userId, Module.Products, Access.Write).ConfigureAwait(true),
                "zoya has write access to products, which she should not");

            harness.Expect(
                "a standard account is not an admin",
                !session.IsAdmin,
                "a standard account reported itself as an administrator");

            harness.Expect(
                "users and the audit log stay admin only",
                !await permissions.HasAccessAsync(userId, Module.Users, Access.Read).ConfigureAwait(true)
                && !await permissions.HasAccessAsync(userId, Module.AuditLog, Access.Read).ConfigureAwait(true),
                "a standard account can reach Users or the Audit log");

            // She is assigned one shop, so asking for another has to be refused rather
            // than quietly widened.
            var shops = await permissions.AllowedShopIdsAsync(userId).ConfigureAwait(true);
            harness.Expect("read-only account sees only its own shop", shops.Count == 1,
                $"zoya can see {shops.Count} shops");

            var scope = sp.GetRequiredService<IShopScope>();

            try
            {
                await scope.ResolveAsync(long.MaxValue).ConfigureAwait(true);
                harness.Fail("asking for an unassigned shop is refused", "the request was allowed");
            }
            catch (ForbiddenException)
            {
                harness.Pass("asking for an unassigned shop is refused");
            }
        }).ConfigureAwait(true);

        // sara may write customers but not products.
        await AsUserAsync(provider, session, "sara", async sp =>
        {
            var products = sp.GetRequiredService<ProductService>();
            var categories = await products.QueryAsync(new QueryRequest { PageSize = 1 }).ConfigureAwait(true);

            harness.Expect("a standard account can still read the catalogue", categories.TotalCount > 0,
                "sara cannot read products, but is seeded with read access");

            var customers = sp.GetRequiredService<CustomerService>();

            await harness.CheckAsync("an account with write access can add a customer", async () =>
            {
                await customers
                    .CreateAsync(new SaveCustomerRequest($"QA Sara {DateTime.Now:HHmmssfff}", null, null, true))
                    .ConfigureAwait(true);
            }).ConfigureAwait(true);
        }).ConfigureAwait(true);

        // Put the admin back for anything that runs after this.
        await SignInAsync(provider, session, DatabaseSeeder.AdminUsername, DatabaseSeeder.AdminPassword)
            .ConfigureAwait(true);
    }

    private static async Task AsUserAsync(
        IServiceProvider provider,
        UserSession session,
        string username,
        Func<IServiceProvider, Task> body)
    {
        await SignInAsync(provider, session, username, username).ConfigureAwait(true);

        using var scope = provider.CreateScope();
        await body(scope.ServiceProvider).ConfigureAwait(true);
    }

    private static async Task SignInAsync(
        IServiceProvider provider,
        UserSession session,
        string username,
        string password)
    {
        using var scope = provider.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var profile = await auth.LoginAsync(new LoginRequest(username, password)).ConfigureAwait(true);

        session.SignIn(
            profile.Id,
            profile.Username,
            profile.FullName,
            profile.Role,
            profile.Shops.Select(s => s.Id).ToList());
    }
}
