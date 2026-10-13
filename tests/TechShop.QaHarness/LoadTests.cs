using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Catalog;
using TechShop.Application.Features.Sales;
using TechShop.Application.Features.Shops;
using TechShop.Application.Features.Stock;
using TechShop.Domain.Enums;
using TechShop.WinForms;

namespace TechShop.QaHarness;

/// <summary>
/// Pushes real volume through the app and times it.
///
/// The budgets below are deliberately generous — this runs on whatever machine happens to
/// be building — but they are tight enough to catch the failure that actually matters:
/// a screen that loads every row and filters in memory. That looks fine on the demo
/// database and falls over in a shop with two years of trading.
/// </summary>
internal static class LoadTests
{
    private const int BulkProducts = 300;
    private const int BulkSales = 250;

    public static async Task RunAsync(Harness harness)
    {
        harness.Section("load");

        using var scope = AppHost.CreateScope();
        var sp = scope.ServiceProvider;

        var shops = await sp.GetRequiredService<ShopService>().LookupAsync().ConfigureAwait(true);
        var categories = await sp.GetRequiredService<CategoryService>().LookupAsync().ConfigureAwait(true);

        if (shops.Count == 0 || categories.Count == 0)
        {
            harness.Fail("load test can start", "no shops or categories");
            return;
        }

        // A shop that actually holds stock: earlier tests create an empty branch, and
        // selling from an empty shelf measures nothing.
        var stocked = await sp.GetRequiredService<StockService>()
            .QueryLevelsAsync(new QueryRequest
            {
                PageSize = 20,
                Sort = [new SortRule { Field = "qty", Desc = true }],
            })
            .ConfigureAwait(true);

        var busyShopId = stocked.Items.Count > 0 ? stocked.Items[0].ShopId : shops[0].Id;

        await BulkInsertProductsAsync(harness, sp, categories[0].Id).ConfigureAwait(true);
        await BulkSellAsync(harness, sp, busyShopId).ConfigureAwait(true);
        await GridPagingAsync(harness, sp).ConfigureAwait(true);
        await DeepPageAsync(harness, sp).ConfigureAwait(true);
        await SearchAsync(harness, sp).ConfigureAwait(true);
    }

    private static async Task BulkInsertProductsAsync(Harness harness, IServiceProvider sp, long categoryId)
    {
        var products = sp.GetRequiredService<ProductService>();
        var stamp = DateTime.Now.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var clock = Stopwatch.StartNew();

        for (var i = 0; i < BulkProducts; i++)
        {
            await products.CreateAsync(new SaveProductRequest(
                $"LOAD-{stamp}-{i:D4}",
                $"99{stamp}{i:D4}",
                $"Load test product {i}",
                categoryId,
                "Load",
                100m + i,
                150m + i,
                5,
                i % 2 == 0,
                17m,
                true)).ConfigureAwait(true);
        }

        clock.Stop();
        var each = clock.Elapsed.TotalMilliseconds / BulkProducts;

        harness.Expect(
            $"{BulkProducts} products insert in under 30s",
            clock.Elapsed.TotalSeconds < 30,
            $"took {clock.Elapsed.TotalSeconds:F1}s ({each:F1}ms each)");

        harness.Pass($"product insert rate", $"{each:F1}ms each");
    }

    private static async Task BulkSellAsync(Harness harness, IServiceProvider sp, long shopId)
    {
        var sales = sp.GetRequiredService<SalesService>();
        var stock = sp.GetRequiredService<StockService>();
        var customers = await sp.GetRequiredService<Application.Features.Parties.CustomerService>()
            .LookupAsync().ConfigureAwait(true);

        var walkIn = customers.FirstOrDefault(c => c.IsWalkIn) ?? customers.FirstOrDefault();
        if (walkIn is null)
        {
            harness.Fail("bulk sales run", "no customer to sell to");
            return;
        }

        var levels = await stock
            .QueryLevelsAsync(new QueryRequest
            {
                ShopId = shopId,
                PageSize = 60,
                Sort = [new SortRule { Field = "qty", Desc = true }],
            })
            .ConfigureAwait(true);

        var sellable = levels.Items.Where(l => l.Qty > BulkSales / 40).ToList();
        if (sellable.Count == 0)
        {
            harness.Fail("bulk sales run", "nothing with enough stock to sell repeatedly");
            return;
        }

        var clock = Stopwatch.StartNew();
        var sold = 0;

        for (var i = 0; i < BulkSales; i++)
        {
            var line = sellable[i % sellable.Count];

            try
            {
                await sales.CreateSaleAsync(new CreateSaleRequest(
                    shopId,
                    walkIn.Id,
                    0m,
                    line.CostPrice,
                    PaymentMethod.Cash,
                    "load",
                    [new SaleLineInput(line.ProductId, 1, line.CostPrice)])).ConfigureAwait(true);

                sold++;
            }
            catch (Application.Common.Exceptions.ValidationFailedException)
            {
                // Ran out of that line; the rest of the run still stands.
            }
        }

        clock.Stop();

        harness.Expect($"{sold} sales ring up in under 40s", clock.Elapsed.TotalSeconds < 40,
            $"took {clock.Elapsed.TotalSeconds:F1}s");

        harness.Pass("sale rate", $"{clock.Elapsed.TotalMilliseconds / Math.Max(sold, 1):F1}ms each");

        var negatives = await stock.QueryLevelsAsync(new QueryRequest { PageSize = 1000 }).ConfigureAwait(true);
        harness.Expect("no stock went negative under load",
            negatives.Items.All(l => l.Qty >= 0),
            "a stock level is below zero after the bulk sales");
    }

    private static async Task GridPagingAsync(Harness harness, IServiceProvider sp)
    {
        var sales = sp.GetRequiredService<SalesService>();

        var total = await sales.QueryAsync(new QueryRequest { PageSize = 1 }).ConfigureAwait(true);

        var clock = Stopwatch.StartNew();
        var page = await sales.QueryAsync(new QueryRequest
        {
            Page = 1,
            PageSize = 50,
            Sort = [new SortRule { Field = "invoiceDate", Desc = true }],
        }).ConfigureAwait(true);
        clock.Stop();

        harness.Expect($"first page of {total.TotalCount:N0} invoices loads in under 1s",
            clock.ElapsedMilliseconds < 1000,
            $"took {clock.ElapsedMilliseconds}ms");

        harness.Expect("a page returns only one page of rows", page.Items.Count <= 50,
            $"asked for 50 rows and got {page.Items.Count}");
    }

    private static async Task DeepPageAsync(Harness harness, IServiceProvider sp)
    {
        var sales = sp.GetRequiredService<SalesService>();
        var total = await sales.QueryAsync(new QueryRequest { PageSize = 1 }).ConfigureAwait(true);

        var lastPage = Math.Max(1, (int)Math.Ceiling(total.TotalCount / 50d));

        var clock = Stopwatch.StartNew();
        var page = await sales.QueryAsync(new QueryRequest
        {
            Page = lastPage,
            PageSize = 50,
            Sort = [new SortRule { Field = "invoiceDate", Desc = true }],
        }).ConfigureAwait(true);
        clock.Stop();

        // The last page has to cost about what the first page costs. If it does not, the
        // query is materialising everything before it pages.
        harness.Expect($"the last page of {total.TotalCount:N0} invoices loads in under 1s",
            clock.ElapsedMilliseconds < 1000,
            $"took {clock.ElapsedMilliseconds}ms");

        harness.Expect("the last page has rows on it", page.Items.Count > 0,
            $"page {lastPage} of {lastPage} came back empty");
    }

    private static async Task SearchAsync(Harness harness, IServiceProvider sp)
    {
        var products = sp.GetRequiredService<ProductService>();

        var clock = Stopwatch.StartNew();
        var found = await products
            .QueryAsync(new QueryRequest { Search = "Load test", PageSize = 25 })
            .ConfigureAwait(true);
        clock.Stop();

        harness.Expect("searching the catalogue takes under 1s", clock.ElapsedMilliseconds < 1000,
            $"took {clock.ElapsedMilliseconds}ms");

        harness.Expect("the search finds the bulk-inserted products", found.TotalCount >= BulkProducts,
            $"found {found.TotalCount} of {BulkProducts}");

        var movements = sp.GetRequiredService<StockService>();
        clock.Restart();
        var moves = await movements
            .QueryMovementsAsync(new QueryRequest { PageSize = 50, Sort = [new SortRule { Field = "movedAt", Desc = true }] })
            .ConfigureAwait(true);
        clock.Stop();

        harness.Expect($"the movement history ({moves.TotalCount:N0} rows) pages in under 1s",
            clock.ElapsedMilliseconds < 1000,
            $"took {clock.ElapsedMilliseconds}ms");
    }
}
