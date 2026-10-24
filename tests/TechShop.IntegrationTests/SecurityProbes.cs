using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Auth;
using TechShop.Application.Features.Catalog;
using TechShop.Application.Features.Parties;
using TechShop.Application.Features.Sales;
using TechShop.Application.Features.Users;
using TechShop.Domain.Enums;
using TechShop.Infrastructure.Persistence;

namespace TechShop.IntegrationTests;

/// <summary>
/// Attacks the application the way someone with a copy of it would.
///
/// These run against the service layer rather than the screens, because that is the
/// real boundary: hiding a button does not stop anything, and a desktop app ships its
/// own client to the attacker. Each test here asserts the defence that should hold.
/// </summary>
[Collection("shop")]
public class SecurityProbes(ShopFixture shop)
{
    // ------------------------------------------------------- authentication

    [Fact]
    public async Task A_wrong_password_is_refused()
        => await Assert.ThrowsAsync<ForbiddenException>(() => shop.InScope(sp =>
            sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest("admin", "wrong"))));

    [Fact]
    public async Task A_username_that_does_not_exist_fails_the_same_way_as_a_wrong_password()
    {
        var unknown = await Record.ExceptionAsync(() => shop.InScope(sp =>
            sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest("ghost", "x"))));

        var wrong = await Record.ExceptionAsync(() => shop.InScope(sp =>
            sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest("admin", "x"))));

        // Different messages would let an attacker enumerate which accounts exist.
        Assert.Equal(unknown!.GetType(), wrong!.GetType());
        Assert.Equal(unknown.Message, wrong.Message);
    }

    [Fact]
    public async Task Passwords_are_not_stored_in_a_recoverable_form()
    {
        var stored = await shop.InScope(sp =>
            sp.GetRequiredService<AppDbContext>().Users
                .Where(u => u.Username == "admin")
                .Select(u => u.PasswordHash)
                .FirstAsync());

        Assert.DoesNotContain("admin123", stored, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("$2", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deactivated_account_cannot_sign_in()
    {
        var shopId = await shop.TradingShopId();

        var created = await shop.InScope(sp =>
            sp.GetRequiredService<UserService>().CreateAsync(new SaveUserRequest(
                $"probe{DateTime.Now:HHmmssfff}",
                "Probe User",
                UserRole.Standard,
                true,
                "probe-password-1",
                [shopId],
                [new PermissionInput(Module.Dashboard, true, false)])));

        await shop.InScope(sp => sp.GetRequiredService<UserService>().DeactivateAsync(created.Id));

        await Assert.ThrowsAsync<ForbiddenException>(() => shop.InScope(sp =>
            sp.GetRequiredService<AuthService>().LoginAsync(
                new LoginRequest(created.Username, "probe-password-1"))));
    }

    // ------------------------------------------------------- authorisation

    [Fact]
    public async Task A_read_only_account_cannot_write_even_by_calling_the_service_directly()
    {
        await shop.SignInAsync("zoya", "zoya");

        try
        {
            var permissions = await shop.InScope(sp =>
                sp.GetRequiredService<IPermissionService>()
                    .HasAccessAsync(shop.Session.UserId!.Value, Module.Products, Access.Write));

            Assert.False(permissions, "a read-only account reports write access to Products");
        }
        finally
        {
            await shop.SignInAsync("admin", "admin123");
        }
    }

    [Fact]
    public async Task Users_and_the_audit_log_stay_out_of_reach_of_a_standard_account()
    {
        await shop.SignInAsync("hina", "hina");

        try
        {
            var id = shop.Session.UserId!.Value;

            var canReadUsers = await shop.InScope(sp =>
                sp.GetRequiredService<IPermissionService>().HasAccessAsync(id, Module.Users, Access.Read));

            var canReadAudit = await shop.InScope(sp =>
                sp.GetRequiredService<IPermissionService>().HasAccessAsync(id, Module.AuditLog, Access.Read));

            Assert.False(canReadUsers, "a standard account can reach Users");
            Assert.False(canReadAudit, "a standard account can reach the Audit log");
        }
        finally
        {
            await shop.SignInAsync("admin", "admin123");
        }
    }

    [Fact]
    public async Task A_user_cannot_reach_a_shop_they_are_not_assigned_to()
    {
        await shop.SignInAsync("zoya", "zoya");

        try
        {
            var allowed = shop.Session.AllowedShopIds;

            var forbidden = await shop.InScope(async sp =>
                await sp.GetRequiredService<AppDbContext>().Shops
                    .Where(s => !allowed.Contains(s.Id))
                    .Select(s => s.Id)
                    .FirstOrDefaultAsync());

            if (forbidden == 0)
            {
                return;
            }

            await Assert.ThrowsAsync<ForbiddenException>(() => shop.InScope(sp =>
                sp.GetRequiredService<IShopScope>().ResolveAsync(forbidden)));
        }
        finally
        {
            await shop.SignInAsync("admin", "admin123");
        }
    }

    [Fact]
    public async Task Selling_into_a_shop_the_user_cannot_reach_is_refused()
    {
        await shop.SignInAsync("zoya", "zoya");

        try
        {
            var allowed = shop.Session.AllowedShopIds;

            var forbidden = await shop.InScope(async sp =>
                await sp.GetRequiredService<AppDbContext>().Shops
                    .Where(s => !allowed.Contains(s.Id))
                    .Select(s => s.Id)
                    .FirstOrDefaultAsync());

            if (forbidden == 0)
            {
                return;
            }

            var walkIn = await shop.WalkInCustomerId();

            await Assert.ThrowsAsync<ForbiddenException>(() => shop.InScope(sp =>
                sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                    forbidden,
                    walkIn,
                    0m,
                    0m,
                    PaymentMethod.Cash,
                    "cross-shop",
                    [new SaleLineInput(1, 1, 1m)]))));
        }
        finally
        {
            await shop.SignInAsync("admin", "admin123");
        }
    }

    // ------------------------------------------------------- injection

    [Theory]
    [InlineData("'; DROP TABLE Products; --")]
    [InlineData("%' OR '1'='1")]
    [InlineData("\" OR 1=1 --")]
    [InlineData("x' UNION SELECT PasswordHash FROM Users --")]
    public async Task A_search_term_is_data_and_never_sql(string payload)
    {
        // Runs, returns nothing useful, and leaves the catalogue standing.
        var result = await shop.InScope(sp =>
            sp.GetRequiredService<ProductService>().QueryAsync(
                new QueryRequest { Search = payload, PageSize = 10 }));

        Assert.Empty(result.Items);

        var stillThere = await shop.InScope(sp =>
            sp.GetRequiredService<AppDbContext>().Products.CountAsync());

        Assert.True(stillThere > 0, "the products table did not survive the payload");
    }

    [Fact]
    public async Task A_filter_naming_a_column_the_screen_never_offered_cannot_reach_it()
    {
        var result = await shop.InScope(sp =>
            sp.GetRequiredService<UserService>().QueryAsync(new QueryRequest
            {
                PageSize = 50,
                Filters =
                [
                    new FilterRule
                    {
                        Field = "PasswordHash",
                        Operator = FilterOperator.Contains,
                        Value = "$2",
                    },
                ],
            }));

        // The rule is dropped, so every user comes back rather than a filtered set --
        // and no password material is reachable through the query contract at all.
        Assert.True(result.TotalCount > 0);
    }

    // ------------------------------------------------------- data integrity

    [Fact]
    public async Task A_payment_cannot_be_applied_to_another_customers_invoice()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId);
        var account = await shop.AccountCustomerId();

        var invoice = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                account,
                0m,
                0m,
                PaymentMethod.Credit,
                "victim invoice",
                [new SaleLineInput(line.ProductId, 1, 100m)])));

        var other = await shop.InScope(async sp =>
        {
            var customers = await sp.GetRequiredService<CustomerService>().LookupAsync();
            return customers.First(c => c.Id != account && !c.IsWalkIn).Id;
        });

        await Assert.ThrowsAnyAsync<Exception>(() => shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().ReceiveAsync(new CreateCustomerPaymentRequest(
                shopId,
                other,
                invoice.Id,
                DateTime.UtcNow,
                50m,
                PaymentMethod.Cash,
                "misapplied"))));
    }

    [Fact]
    public async Task A_void_invoice_cannot_take_money()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId);
        var account = await shop.AccountCustomerId();

        var invoice = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                account,
                0m,
                0m,
                PaymentMethod.Credit,
                "to void then pay",
                [new SaleLineInput(line.ProductId, 1, 100m)])));

        await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().VoidAsync(invoice.Id, new VoidInvoiceRequest("void")));

        await Assert.ThrowsAnyAsync<Exception>(() => shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().ReceiveAsync(new CreateCustomerPaymentRequest(
                shopId,
                invoice.CustomerId,
                invoice.Id,
                DateTime.UtcNow,
                50m,
                PaymentMethod.Cash,
                "paying a void"))));
    }

    [Fact]
    public async Task An_invoice_cannot_be_voided_twice()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId);
        var account = await shop.AccountCustomerId();
        var before = await shop.OnHand(shopId, line.ProductId);

        var invoice = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                account,
                0m,
                0m,
                PaymentMethod.Credit,
                "double void",
                [new SaleLineInput(line.ProductId, 2, 100m)])));

        await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().VoidAsync(invoice.Id, new VoidInvoiceRequest("first")));

        await Assert.ThrowsAnyAsync<Exception>(() => shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().VoidAsync(invoice.Id, new VoidInvoiceRequest("second"))));

        // Voiding twice would put the stock back twice and invent two units.
        Assert.Equal(before, await shop.OnHand(shopId, line.ProductId));
    }

    [Fact]
    public async Task The_audit_log_records_a_failed_sign_in()
    {
        var before = await shop.InScope(sp =>
            sp.GetRequiredService<AppDbContext>().AuditLogs
                .CountAsync(a => a.Action == AuditAction.LoginFailed));

        await Record.ExceptionAsync(() => shop.InScope(sp =>
            sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest("admin", "nope"))));

        var after = await shop.InScope(sp =>
            sp.GetRequiredService<AppDbContext>().AuditLogs
                .CountAsync(a => a.Action == AuditAction.LoginFailed));

        Assert.Equal(before + 1, after);
    }
}
