using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Features.Auth;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Parties;
using TechShop.Application.Features.Stock;
using TechShop.Application.Features.Users;
using TechShop.Domain.Enums;
using TechShop.Infrastructure.Persistence;

namespace TechShop.IntegrationTests;

/// <summary>
/// The defences added after the first pentest pass. Each test here was written to fail
/// against the code as it stood, and names the weakness it closes.
/// </summary>
[Collection("shop")]
public class SecurityHardeningTests(ShopFixture shop)
{
    // ---------------------------------------------------- S1: revoked access

    [Fact]
    public async Task Deactivating_an_account_stops_it_acting_immediately()
    {
        var shopId = await shop.TradingShopId();
        var name = $"revoked{DateTime.Now:HHmmssfff}";

        var created = await shop.InScope(sp =>
            sp.GetRequiredService<UserService>().CreateAsync(new SaveUserRequest(
                name, "Revoked User", UserRole.Standard, true, "probe-password-1",
                [shopId], [new PermissionInput(Module.Customers, true, true)])));

        var victim = await shop.SignInAsync(name, "probe-password-1");

        try
        {
            // An admin at another till cuts the account off while it is still signed in.
            await shop.SignInAsync("admin", "admin123");
            await shop.InScope(sp => sp.GetRequiredService<UserService>().DeactivateAsync(created.Id));

            // The victim's app knows nothing of this: it is still holding the session it
            // was handed at sign-in, which is exactly the session that must stop working.
            shop.Session.SignIn(
                victim.Id, victim.Username, victim.FullName, victim.Role,
                victim.Shops.Select(s => s.Id).ToList());

            var stillHasWrite = await shop.InScope(sp =>
                sp.GetRequiredService<IPermissionService>()
                    .HasAccessAsync(created.Id, Module.Customers, Access.Write));

            Assert.False(stillHasWrite, "a deactivated account kept its permissions while signed in");

            // And the service layer has to refuse the work, not just the permission check.
            await Assert.ThrowsAsync<ForbiddenException>(() => shop.InScope(sp =>
                sp.GetRequiredService<CustomerService>()
                    .CreateAsync(new SaveCustomerRequest($"ghost {name}", null, null, true))));
        }
        finally
        {
            await shop.SignInAsync("admin", "admin123");
        }
    }

    [Fact]
    public async Task A_deactivated_account_can_reach_no_shops()
    {
        var shopId = await shop.TradingShopId();
        var name = $"noshop{DateTime.Now:HHmmssfff}";

        var created = await shop.InScope(sp =>
            sp.GetRequiredService<UserService>().CreateAsync(new SaveUserRequest(
                name, "No Shop", UserRole.Standard, true, "probe-password-1",
                [shopId], [new PermissionInput(Module.Dashboard, true, false)])));

        await shop.InScope(sp => sp.GetRequiredService<UserService>().DeactivateAsync(created.Id));

        var shops = await shop.InScope(sp =>
            sp.GetRequiredService<IPermissionService>().AllowedShopIdsAsync(created.Id));

        Assert.Empty(shops);
    }

    // ---------------------------------------------------- S3: stale permissions

    [Fact]
    public async Task Revoking_a_permission_takes_effect_at_once_not_when_a_cache_expires()
    {
        var shopId = await shop.TradingShopId();
        var name = $"narrow{DateTime.Now:HHmmssfff}";

        var created = await shop.InScope(sp =>
            sp.GetRequiredService<UserService>().CreateAsync(new SaveUserRequest(
                name, "Narrowing User", UserRole.Standard, true, "probe-password-1",
                [shopId], [new PermissionInput(Module.Customers, true, true)])));

        // Warm the cache.
        Assert.True(await shop.InScope(sp =>
            sp.GetRequiredService<IPermissionService>()
                .HasAccessAsync(created.Id, Module.Customers, Access.Write)));

        await shop.InScope(sp =>
            sp.GetRequiredService<UserService>().UpdateAsync(created.Id, new SaveUserRequest(
                name, "Narrowing User", UserRole.Standard, true, null,
                [shopId], [new PermissionInput(Module.Customers, true, false)])));

        var stillHasWrite = await shop.InScope(sp =>
            sp.GetRequiredService<IPermissionService>()
                .HasAccessAsync(created.Id, Module.Customers, Access.Write));

        Assert.False(stillHasWrite, "write access survived being revoked, because the cache was stale");
    }

    // ---------------------------------------------------- S2: brute force

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_out()
    {
        var shopId = await shop.TradingShopId();
        var name = $"brute{DateTime.Now:HHmmssfff}";

        await shop.InScope(sp =>
            sp.GetRequiredService<UserService>().CreateAsync(new SaveUserRequest(
                name, "Brute Target", UserRole.Standard, true, "the-real-password",
                [shopId], [new PermissionInput(Module.Dashboard, true, false)])));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Record.ExceptionAsync(() => shop.InScope(sp =>
                sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(name, "guess"))));
        }

        // Even the correct password must now be refused: the account is locked.
        var afterLockout = await Record.ExceptionAsync(() => shop.InScope(sp =>
            sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(name, "the-real-password"))));

        Assert.NotNull(afterLockout);
        Assert.Contains("locked", afterLockout.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_earlier_failures()
    {
        var shopId = await shop.TradingShopId();
        var name = $"reset{DateTime.Now:HHmmssfff}";

        await shop.InScope(sp =>
            sp.GetRequiredService<UserService>().CreateAsync(new SaveUserRequest(
                name, "Reset Target", UserRole.Standard, true, "the-real-password",
                [shopId], [new PermissionInput(Module.Dashboard, true, false)])));

        // Two wrong, then right: the counter must go back to zero, or a user who
        // mistypes twice a day eventually locks themselves out of their own till.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Record.ExceptionAsync(() => shop.InScope(sp =>
                sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(name, "guess"))));
        }

        await shop.InScope(sp =>
            sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(name, "the-real-password")));

        var failures = await shop.InScope(sp =>
            sp.GetRequiredService<AppDbContext>().Users
                .Where(u => u.Username == name)
                .Select(u => u.FailedLoginCount)
                .FirstAsync());

        Assert.Equal(0, failures);

        await shop.SignInAsync("admin", "admin123");
    }

    // ---------------------------------------------------- the guard itself

    [Fact]
    public async Task A_read_only_account_is_refused_by_the_service_not_just_by_the_sidebar()
    {
        var shopId = await shop.TradingShopId();
        var name = $"readonly{DateTime.Now:HHmmssfff}";

        await shop.InScope(sp =>
            sp.GetRequiredService<UserService>().CreateAsync(new SaveUserRequest(
                name, "Read Only", UserRole.Standard, true, "probe-password-1",
                [shopId], [new PermissionInput(Module.Customers, true, false)])));

        await shop.SignInAsync(name, "probe-password-1");

        try
        {
            // Read is granted, so the screen opens and the grid fills.
            await shop.InScope(sp => sp.GetRequiredService<CustomerService>()
                .QueryAsync(new QueryRequest { Page = 1, PageSize = 5 }));

            // Write is not, and hiding the Save button is not what stops it.
            var refused = await Record.ExceptionAsync(() => shop.InScope(sp =>
                sp.GetRequiredService<CustomerService>()
                    .CreateAsync(new SaveCustomerRequest($"sneaky {name}", null, null, true))));

            Assert.IsType<ForbiddenException>(refused);
        }
        finally
        {
            await shop.SignInAsync("admin", "admin123");
        }
    }

    [Fact]
    public async Task A_module_the_account_was_never_given_is_refused_outright()
    {
        var shopId = await shop.TradingShopId();
        var name = $"nostock{DateTime.Now:HHmmssfff}";

        await shop.InScope(sp =>
            sp.GetRequiredService<UserService>().CreateAsync(new SaveUserRequest(
                name, "No Stock", UserRole.Standard, true, "probe-password-1",
                [shopId], [new PermissionInput(Module.Customers, true, true)])));

        await shop.SignInAsync(name, "probe-password-1");

        try
        {
            var line = await shop.StockedLine(shopId, 1);

            var refused = await Record.ExceptionAsync(() => shop.InScope(sp =>
                sp.GetRequiredService<StockService>().AdjustAsync(
                    new AdjustStockRequest(shopId, line.ProductId, 5, "helping myself"))));

            Assert.IsType<ForbiddenException>(refused);
        }
        finally
        {
            await shop.SignInAsync("admin", "admin123");
        }
    }

    // ---------------------------------------------------- S6: session expiry

    [Fact]
    public async Task An_idle_session_stops_being_allowed_to_act()
    {
        var before = shop.Session.IdleTimeout;
        shop.Session.IdleTimeout = TimeSpan.Zero;

        try
        {
            // Zero means every moment since the last keypress is already too long, which
            // is how a till left unattended behind the counter must behave.
            var refused = await Record.ExceptionAsync(() => shop.InScope(sp =>
                sp.GetRequiredService<CustomerService>()
                    .CreateAsync(new SaveCustomerRequest($"idle {DateTime.Now:HHmmssfff}", null, null, true))));

            Assert.IsType<ForbiddenException>(refused);
            Assert.Contains("timed out", refused!.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            shop.Session.IdleTimeout = before;
        }
    }

    [Fact]
    public async Task Working_at_the_till_keeps_the_session_alive()
    {
        var before = shop.Session.IdleTimeout;
        shop.Session.IdleTimeout = TimeSpan.FromMinutes(1);

        try
        {
            shop.Session.Touch();

            // A cashier who is actually serving customers must never be interrupted.
            await shop.InScope(sp => sp.GetRequiredService<CustomerService>()
                .CreateAsync(new SaveCustomerRequest($"busy {DateTime.Now:HHmmssfff}", null, null, true)));
        }
        finally
        {
            shop.Session.IdleTimeout = before;
        }
    }

    // ---------------------------------------------------- S7: audit integrity

    [Fact]
    public async Task The_audit_log_detects_a_tampered_row()
    {
        await shop.InScope(sp => sp.GetRequiredService<CustomerService>()
            .CreateAsync(new SaveCustomerRequest($"audit {DateTime.Now:HHmmssfff}", null, null, true)));

        var clean = await shop.InScope(sp => sp.GetRequiredService<IAuditService>().VerifyAsync());
        Assert.True(clean.Intact, $"the log reported tampering before anything was changed: {clean.Detail}");

        // Someone edits a row directly in the file, which is the whole point of the
        // threat: the database is a file on a shop PC.
        await shop.InScope(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var row = await db.AuditLogs.OrderByDescending(a => a.Id).FirstAsync();
            row.Summary = "nothing to see here";
            await db.SaveChangesAsync();
        });

        var tampered = await shop.InScope(sp => sp.GetRequiredService<IAuditService>().VerifyAsync());

        Assert.False(tampered.Intact, "an edited audit row went undetected");
    }

    [Fact]
    public async Task The_audit_log_detects_a_deleted_row()
    {
        await shop.InScope(sp => sp.GetRequiredService<CustomerService>()
            .CreateAsync(new SaveCustomerRequest($"audit del {DateTime.Now:HHmmssfff}", null, null, true)));

        await shop.InScope(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var rows = await db.AuditLogs.OrderByDescending(a => a.Id).Take(2).ToListAsync();
            db.AuditLogs.Remove(rows[1]);
            await db.SaveChangesAsync();
        });

        var result = await shop.InScope(sp => sp.GetRequiredService<IAuditService>().VerifyAsync());

        Assert.False(result.Intact, "a removed audit row left no trace");
    }
}
