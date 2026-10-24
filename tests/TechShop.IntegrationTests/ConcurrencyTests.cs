using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Sales;
using TechShop.Application.Features.Stock;
using TechShop.Domain.Enums;
using TechShop.Infrastructure.Persistence;

namespace TechShop.IntegrationTests;

/// <summary>
/// What happens when a hundred tills ring up at once.
///
/// A shop with four counters and a stockroom tablet is already concurrent, and the
/// failure that matters is not slowness: it is two counters selling the same last unit,
/// or two invoices taking the same number. Both are silent, and both are discovered
/// weeks later by someone reconciling stock.
///
/// Each task takes its own DI scope, which is its own DbContext, because that is what
/// the app does per screen.
/// </summary>
[Collection("shop")]
public class ConcurrencyTests(ShopFixture shop)
{
    private const int Users = 100;

    private sealed record Outcome(bool Sold, string? Invoice, string? Failure);

    [Fact]
    public async Task A_hundred_tills_selling_the_last_units_never_oversell()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId, atLeast: 20);
        var walkIn = await shop.WalkInCustomerId();

        // Trim the shelf to exactly half the number of buyers, so the correct outcome
        // is unambiguous: fifty sales, fifty refusals, nothing left.
        var available = Users / 2;
        await SetStockTo(shopId, line.ProductId, available);

        var outcomes = await RunConcurrently(Users, _ => SellOne(shopId, walkIn, line.ProductId));

        var sold = outcomes.Count(o => o.Sold);
        var onHand = await shop.OnHand(shopId, line.ProductId);

        Assert.True(onHand >= 0, $"stock went negative: {onHand}");

        Assert.True(
            sold <= available,
            $"{sold} sales went through for {available} units in stock — the shelf was oversold by {sold - available}");

        Assert.Equal(available - sold, onHand);
    }

    [Fact]
    public async Task A_hundred_concurrent_sales_never_reuse_an_invoice_number()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId, atLeast: 20);
        var walkIn = await shop.WalkInCustomerId();

        await SetStockTo(shopId, line.ProductId, Users * 2);

        var outcomes = await RunConcurrently(Users, _ => SellOne(shopId, walkIn, line.ProductId));

        var numbers = outcomes.Where(o => o.Sold).Select(o => o.Invoice!).ToList();
        var duplicates = numbers.GroupBy(n => n).Where(g => g.Count() > 1).ToList();

        Assert.True(
            duplicates.Count == 0,
            $"{duplicates.Count} invoice numbers were issued more than once: "
            + string.Join(", ", duplicates.Take(5).Select(g => $"{g.Key} x{g.Count()}")));
    }

    [Fact]
    public async Task Stock_still_reconciles_with_its_movements_after_a_hundred_concurrent_sales()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId, atLeast: 20);
        var walkIn = await shop.WalkInCustomerId();

        await SetStockTo(shopId, line.ProductId, Users * 2);

        await RunConcurrently(Users, _ => SellOne(shopId, walkIn, line.ProductId));

        var reconciled = await shop.InScope(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();

            var level = await db.StockLevels
                .Where(l => l.ShopId == shopId && l.ProductId == line.ProductId)
                .Select(l => l.Qty)
                .FirstAsync();

            var moved = await db.StockMovements
                .Where(m => m.ShopId == shopId && m.ProductId == line.ProductId)
                .SumAsync(m => m.Qty);

            return (level, moved);
        });

        Assert.Equal(reconciled.moved, reconciled.level);
    }

    [Fact]
    public async Task A_hundred_readers_alongside_writers_do_not_fall_over()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId, atLeast: 20);
        var walkIn = await shop.WalkInCustomerId();

        await SetStockTo(shopId, line.ProductId, Users * 2);

        var failures = new ConcurrentBag<string>();
        var clock = Stopwatch.StartNew();

        // Half the users are selling, half are looking at reports and lists — which is
        // what a shop with an office actually looks like at midday.
        var work = Enumerable.Range(0, Users).Select(async i =>
        {
            try
            {
                if (i % 2 == 0)
                {
                    await SellOne(shopId, walkIn, line.ProductId);
                }
                else
                {
                    await shop.InScope(async sp =>
                    {
                        await sp.GetRequiredService<SalesService>()
                            .QueryAsync(new QueryRequest { PageSize = 25, ShopId = shopId });

                        await sp.GetRequiredService<StockService>()
                            .QueryLevelsAsync(new QueryRequest { PageSize = 25, ShopId = shopId });
                    });
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{ex.GetType().Name}: {ex.Message}");
            }
        });

        await Task.WhenAll(work);
        clock.Stop();

        // Business refusals are fine and expected. Infrastructure failures are not:
        // a locked database or a tracking collision is the app losing a sale.
        var infrastructure = failures
            .Where(f => f.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
                || f.Contains("locked", StringComparison.OrdinalIgnoreCase)
                || f.Contains("InvalidOperation", StringComparison.OrdinalIgnoreCase)
                || f.Contains("DbUpdate", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            infrastructure.Count == 0,
            $"{infrastructure.Count} of {Users} operations failed on infrastructure in {clock.ElapsedMilliseconds}ms: "
            + string.Join(" | ", infrastructure.Distinct().Take(3)));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Outcome> SellOne(long shopId, long customerId, long productId)
    {
        try
        {
            var invoice = await shop.InScope(sp =>
                sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                    shopId,
                    customerId,
                    0m,
                    10m,
                    PaymentMethod.Cash,
                    "concurrent",
                    [new SaleLineInput(productId, 1, 10m)])));

            return new Outcome(true, invoice.InvoiceNo, null);
        }
        catch (Exception ex)
        {
            return new Outcome(false, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<List<Outcome>> RunConcurrently(int count, Func<int, Task<Outcome>> work)
    {
        // Released together, so the operations genuinely overlap rather than queueing
        // behind each other's start-up.
        using var gate = new SemaphoreSlim(0, count);

        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await gate.WaitAsync();
            return await work(i);
        }).ToList();

        gate.Release(count);

        return [.. await Task.WhenAll(tasks)];
    }

    private Task SetStockTo(long shopId, long productId, int qty)
        => shop.InScope(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();

            var level = await db.StockLevels
                .FirstAsync(l => l.ShopId == shopId && l.ProductId == productId);

            var delta = qty - level.Qty;
            level.Qty = qty;

            // Keep the movement history consistent with the level, so the
            // reconciliation test is measuring the sales and not this adjustment.
            db.StockMovements.Add(new Domain.Entities.StockMovement
            {
                ShopId = shopId,
                ProductId = productId,
                Type = delta >= 0 ? StockMovementType.AdjustmentIn : StockMovementType.AdjustmentOut,
                Qty = delta,
                UnitCost = 10m,
                MovedAt = DateTime.UtcNow,
                Reference = "test setup",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "tests",
            });

            await db.SaveChangesAsync();
        });
}
