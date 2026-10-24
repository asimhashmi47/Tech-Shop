using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Features.Auth;
using TechShop.Domain.Enums;
using TechShop.Infrastructure;
using TechShop.Infrastructure.Persistence;
using TechShop.Infrastructure.Services;

namespace TechShop.IntegrationTests;

/// <summary>
/// A real shop on a real SQLite file, seeded and signed in.
///
/// Deliberately not an in-memory provider. The money paths here lean on transactions,
/// WAL and the REAL column mapping money uses, and an in-memory double would let a
/// transaction bug pass a test and then lose a sale on a counter.
///
/// Each test class gets its own database so one test cannot see another's stock.
/// </summary>
public sealed class ShopFixture : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    public string DatabasePath { get; } = Path.Combine(
        Path.GetTempPath(),
        "techshop-tests",
        $"it-{Guid.NewGuid():N}.db");

    public IServiceProvider Provider => _provider;

    public UserSession Session => _provider.GetRequiredService<UserSession>();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddTechShop(DatabasePath);
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            await new DatabaseSeeder(db, hasher).SeedAsync();
        }

        await SignInAsync(DatabaseSeeder.AdminUsername, DatabaseSeeder.AdminPassword);
    }

    /// <summary>Signs in and makes that identity the one every service below sees.</summary>
    public async Task<UserProfile> SignInAsync(string username, string password)
    {
        using var scope = _provider.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var profile = await auth.LoginAsync(new LoginRequest(username, password));

        Session.SignIn(
            profile.Id,
            profile.Username,
            profile.FullName,
            profile.Role,
            profile.Shops.Select(s => s.Id).ToList());

        return profile;
    }

    /// <summary>Runs work in its own scope, the way one screen's unit of work does.</summary>
    public async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _provider.CreateScope();
        return await work(scope.ServiceProvider);
    }

    public async Task InScope(Func<IServiceProvider, Task> work)
    {
        using var scope = _provider.CreateScope();
        await work(scope.ServiceProvider);
    }

    public T Resolve<T>(IServiceProvider scope) where T : notnull => scope.GetRequiredService<T>();

    /// <summary>A shop that actually holds stock, which is not necessarily the first one.</summary>
    public async Task<long> TradingShopId()
        => await InScope(async sp =>
        {
            var levels = await sp.GetRequiredService<Application.Features.Stock.StockService>()
                .QueryLevelsAsync(new Application.Common.Query.QueryRequest { PageSize = 500 });

            return levels.Items
                .GroupBy(l => l.ShopId)
                .OrderByDescending(g => g.Sum(l => l.Qty))
                .Select(g => g.Key)
                .First();
        });

    public async Task<Application.Features.Stock.StockLevelDto> StockedLine(long shopId, int atLeast = 5, bool? taxable = null)
        => await InScope(async sp =>
        {
            var levels = await sp.GetRequiredService<Application.Features.Stock.StockService>()
                .QueryLevelsAsync(new Application.Common.Query.QueryRequest
                {
                    ShopId = shopId,
                    PageSize = 500,
                    Sort = [new Application.Common.Query.SortRule { Field = "qty", Desc = true }],
                });

            if (taxable is not { } wantTaxable)
            {
                return levels.Items.First(l => l.Qty >= atLeast);
            }

            var products = await sp.GetRequiredService<Application.Features.Catalog.ProductService>()
                .QueryAsync(new Application.Common.Query.QueryRequest { PageSize = 500 });

            var byId = products.Items.ToDictionary(p => p.Id);

            return levels.Items.First(l =>
                l.Qty >= atLeast && byId.TryGetValue(l.ProductId, out var p) && p.IsTaxable == wantTaxable);
        });

    public async Task<long> WalkInCustomerId()
        => await InScope(async sp =>
        {
            var customers = await sp.GetRequiredService<Application.Features.Parties.CustomerService>().LookupAsync();
            return customers.First(c => c.IsWalkIn).Id;
        });

    public async Task<long> AccountCustomerId()
        => await InScope(async sp =>
        {
            var customers = await sp.GetRequiredService<Application.Features.Parties.CustomerService>().LookupAsync();
            return customers.First(c => !c.IsWalkIn).Id;
        });

    public Task<int> OnHand(long shopId, long productId)
        => InScope(sp => sp.GetRequiredService<Application.Features.Stock.StockService>()
            .CurrentQtyAsync(shopId, productId));

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();

        // SQLite keeps the file open until the pool is cleared.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            var directory = Path.GetDirectoryName(DatabasePath);

            foreach (var file in Directory.EnumerateFiles(directory!, Path.GetFileNameWithoutExtension(DatabasePath) + "*"))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A locked temp file is not worth failing a test run over.
        }
    }
}

[CollectionDefinition("shop")]
public sealed class ShopCollection : ICollectionFixture<ShopFixture>;
