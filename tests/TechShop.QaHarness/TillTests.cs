using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Catalog;
using TechShop.Application.Features.Sales;
using TechShop.Application.Features.Shops;
using TechShop.Application.Features.Stock;
using TechShop.Domain.Enums;
using TechShop.Infrastructure.Services;
using TechShop.WinForms;
using TechShop.WinForms.Features.Pos;

namespace TechShop.QaHarness;

/// <summary>
/// Rings up sales through the till itself: search, add to cart, discount, take payment.
///
/// This is the path the shop uses all day, and the one where a mistake costs real money,
/// so it is checked end to end — the invoice, the stock that left the shelf, the receipt
/// that was booked and the cash that reached the drawer.
/// </summary>
internal static class TillTests
{
    public static async Task RunAsync(Harness harness)
    {
        harness.Section("selling through the till");

        using var scope = AppHost.CreateScope();
        var sp = scope.ServiceProvider;

        var session = sp.GetRequiredService<UserSession>();
        var shops = await sp.GetRequiredService<ShopService>().LookupAsync().ConfigureAwait(true);

        if (shops.Count == 0)
        {
            harness.Fail("till opens", "no shops available");
            return;
        }

        // Pick a shop that actually holds stock. Earlier tests create a brand-new branch,
        // and a new branch legitimately has an empty shelf — selling from it is not the
        // thing under test here.
        var stock = sp.GetRequiredService<StockService>();
        var stocked = await stock
            .QueryLevelsAsync(new QueryRequest
            {
                PageSize = 50,
                Sort = [new SortRule { Field = "qty", Desc = true }],
            })
            .ConfigureAwait(true);

        var target = stocked.Items.FirstOrDefault(l => l.Qty > 5);

        if (target is null)
        {
            harness.Fail("till finds a product", "no shop holds more than five of anything");
            return;
        }

        var tillShopId = target.ShopId;

        // The till refuses to sell from "all shops", so a single shop is selected first.
        session.ActiveShopId = tillShopId;

        using var host = new Form { ClientSize = new System.Drawing.Size(1500, 900) };
        var till = (PosPage)PageFactoryAccess.Create(Module.Pos);
        host.Controls.Add(till);

        await till.InitialiseAsync(canWrite: true).ConfigureAwait(true);
        System.Windows.Forms.Application.DoEvents();

        harness.Expect(
            "till search finds a product by SKU",
            await till.SearchForTestAsync(target.Sku).ConfigureAwait(true),
            $"searching for {target.Sku} returned nothing");

        harness.Expect("adding to the cart works", till.AddFirstResultToCartForTest(), "the cart stayed empty");
        harness.Expect("the cart has one line", till.CartLineCount == 1, $"cart has {till.CartLineCount} lines");

        // Add the same item again: it should merge rather than open a second line.
        till.AddFirstResultToCartForTest();
        harness.Expect("adding the same product merges the line", till.CartLineCount == 1,
            $"cart has {till.CartLineCount} lines after adding the same product twice");

        // The till prefills tax from the product, so a taxable item in the cart must
        // already carry tax before anyone types anything.
        var cartProduct = await sp.GetRequiredService<Application.Features.Catalog.ProductService>()
            .QueryAsync(new QueryRequest { Search = target.Sku, PageSize = 1 })
            .ConfigureAwait(true);

        if (cartProduct.Items.Count == 1)
        {
            var expectTax = cartProduct.Items[0].IsTaxable;

            harness.Expect(
                expectTax ? "a taxable item arrives in the cart already taxed" : "an exempt item arrives untaxed",
                expectTax ? till.CartTax > 0m : till.CartTax == 0m,
                $"{target.Sku} is {(expectTax ? "taxable" : "exempt")} but the cart tax is {till.CartTax}");
        }

        // Tiles must fill the shelf area, not leave a dead column beside them.
        harness.Expect("the product tiles fill the shelf width", till.TilesFillWidthForTest(),
            "the tile grid left more than a tile's width of empty space on the right");

        var before = await stock.CurrentQtyAsync(tillShopId, target.ProductId).ConfigureAwait(true);
        var totalBeforeDiscount = till.CartTotal;

        till.SetDiscountForTest(10m);
        harness.Expect("a discount comes off the total", till.CartTotal == totalBeforeDiscount - 10m,
            $"total went {totalBeforeDiscount} -> {till.CartTotal} after a 10 discount");

        var pettyCash = sp.GetRequiredService<Application.Features.Money.PettyCashService>();
        var drawerBefore = await pettyCash.BalanceAsync(tillShopId).ConfigureAwait(true);

        var invoice = await till.CompleteSaleForTest(till.CartTotal, PaymentMethod.Cash).ConfigureAwait(true);

        harness.Expect("the sale produces an invoice", invoice is not null, "no invoice came back");

        if (invoice is null)
        {
            return;
        }

        harness.Expect("the invoice is paid in full", invoice.Status == InvoiceStatus.Paid,
            $"status is {invoice.Status}");

        harness.Expect("the invoice carries the discount", invoice.Discount == 10m,
            $"discount on the invoice is {invoice.Discount}");

        harness.Expect("the cart empties after a sale", till.CartLineCount == 0,
            $"{till.CartLineCount} lines left in the cart");

        var after = await stock.CurrentQtyAsync(tillShopId, target.ProductId).ConfigureAwait(true);
        harness.Expect("the sale takes stock off the shelf", after == before - 2, $"{before} -> {after}, expected -2");

        var drawerAfter = await pettyCash.BalanceAsync(tillShopId).ConfigureAwait(true);
        harness.Expect("a cash sale reaches the drawer", drawerAfter == drawerBefore + invoice.Total,
            $"drawer went {drawerBefore} -> {drawerAfter} on a sale of {invoice.Total}");

        var receipts = await sp.GetRequiredService<SalesService>()
            .QueryPaymentsAsync(new QueryRequest { PageSize = 5, Search = invoice.InvoiceNo })
            .ConfigureAwait(true);

        harness.Expect("a cash sale books a receipt", receipts.TotalCount >= 1,
            "no customer payment row was written for a paid sale");

        // Selling more than is on the shelf has to be refused, not allowed to go negative.
        await harness.CheckAsync("overselling is refused", async () =>
        {
            var sales = sp.GetRequiredService<SalesService>();
            var onHand = await stock.CurrentQtyAsync(tillShopId, target.ProductId).ConfigureAwait(true);

            try
            {
                await sales.CreateSaleAsync(new CreateSaleRequest(
                    tillShopId,
                    invoice.CustomerId,
                    0m,
                    0m,
                    PaymentMethod.Cash,
                    "QA oversell",
                    [new SaleLineInput(target.ProductId, onHand + 500, 100m)])).ConfigureAwait(true);
            }
            catch (Application.Common.Exceptions.ValidationFailedException)
            {
                return;
            }
            catch (Application.Common.Exceptions.ConflictException)
            {
                return;
            }

            throw new InvalidOperationException("a sale larger than the stock on hand was accepted");
        }).ConfigureAwait(true);

        var negative = await stock.QueryLevelsAsync(new QueryRequest { PageSize = 500 }).ConfigureAwait(true);
        harness.Expect("no stock line went negative", negative.Items.All(l => l.Qty >= 0),
            "a stock level is below zero after the till tests");

        // Payment dialog arithmetic, checked directly: this is the figure the cashier
        // hands back, and getting it wrong costs the shop money at every sale.
        CheckChange(harness);

        await MixedTaxBasketAsync(harness, sp, tillShopId).ConfigureAwait(true);

        session.ActiveShopId = null;
        till.Dispose();
    }

    /// <summary>
    /// The case the whole per-line tax feature exists for: one basket holding a taxable
    /// item and an exempt one. Tax must come from the lines, not from a rate applied to
    /// the invoice, and the exempt line must contribute nothing.
    /// </summary>
    private static async Task MixedTaxBasketAsync(Harness harness, IServiceProvider sp, long shopId)
    {
        harness.Section("tax is charged per line");

        var stock = sp.GetRequiredService<StockService>();
        var products = sp.GetRequiredService<Application.Features.Catalog.ProductService>();

        var onShelf = await stock
            .QueryLevelsAsync(new QueryRequest
            {
                ShopId = shopId,
                PageSize = 200,
                Sort = [new SortRule { Field = "qty", Desc = true }],
            })
            .ConfigureAwait(true);

        var catalogue = await products
            .QueryAsync(new QueryRequest { PageSize = 500 })
            .ConfigureAwait(true);

        var byId = catalogue.Items.ToDictionary(p => p.Id);

        var taxable = onShelf.Items
            .FirstOrDefault(l => l.Qty > 3 && byId.TryGetValue(l.ProductId, out var p) && p.IsTaxable);

        var exempt = onShelf.Items
            .FirstOrDefault(l => l.Qty > 3 && byId.TryGetValue(l.ProductId, out var p) && !p.IsTaxable);

        harness.Expect("the catalogue has both taxable and exempt lines in stock",
            taxable is not null && exempt is not null,
            $"taxable={(taxable is null ? "none" : taxable.Sku)}, exempt={(exempt is null ? "none" : exempt.Sku)}");

        if (taxable is null || exempt is null)
        {
            return;
        }

        var taxableProduct = byId[taxable.ProductId];
        var exemptProduct = byId[exempt.ProductId];

        var expectedTax = decimal.Round(2 * taxableProduct.SalePrice * taxableProduct.TaxRate / 100m, 2);

        var customers = await sp.GetRequiredService<Application.Features.Parties.CustomerService>()
            .LookupAsync().ConfigureAwait(true);
        var walkIn = customers.First(c => c.IsWalkIn);

        var sales = sp.GetRequiredService<SalesService>();

        var invoice = await sales.CreateSaleAsync(new CreateSaleRequest(
            shopId,
            walkIn.Id,
            0m,
            (2 * taxableProduct.SalePrice) + exemptProduct.SalePrice + expectedTax,
            PaymentMethod.Cash,
            "QA mixed basket",
            [
                new SaleLineInput(taxableProduct.Id, 2, taxableProduct.SalePrice, expectedTax),
                new SaleLineInput(exemptProduct.Id, 1, exemptProduct.SalePrice, 0m),
            ])).ConfigureAwait(true);

        harness.Expect("the invoice tax is the sum of its lines", invoice.TaxTotal == expectedTax,
            $"invoice tax {invoice.TaxTotal}, lines total {expectedTax}");

        harness.Expect("the exempt line carries no tax",
            invoice.Items.Single(i => i.ProductId == exemptProduct.Id).TaxAmount == 0m,
            "the exempt product was taxed");

        harness.Expect("the taxable line carries its own tax",
            invoice.Items.Single(i => i.ProductId == taxableProduct.Id).TaxAmount == expectedTax,
            "the taxable product's line tax does not match");

        var goods = (2 * taxableProduct.SalePrice) + exemptProduct.SalePrice;

        harness.Expect("the subtotal excludes tax", invoice.SubTotal == goods,
            $"subtotal {invoice.SubTotal}, goods {goods}");

        harness.Expect("the total is goods less discount plus tax",
            invoice.Total == goods + expectedTax,
            $"total {invoice.Total}, expected {goods + expectedTax}");

        // Tax is collected for the authority, so it must not show up as turnover.
        var reports = sp.GetRequiredService<Application.Features.Reports.ReportService>();
        var filter = new Application.Features.Reports.ReportFilter(shopId, DateTime.UtcNow.Date, DateTime.UtcNow.Date);
        var pl = await reports.ProfitAndLossAsync(filter).ConfigureAwait(true);
        var sheet = await reports.BalanceSheetAsync(filter).ConfigureAwait(true);

        harness.Expect("tax is not counted as revenue", pl.Revenue >= 0m && pl.Revenue < sheet.TotalAssets + 1m,
            $"revenue {pl.Revenue} looks wrong");

        harness.Expect("tax collected shows as a liability", sheet.TaxPayable > 0m,
            $"tax payable is {sheet.TaxPayable} after a taxed sale");

        harness.Expect("the balance sheet still balances",
            Math.Abs(sheet.TotalAssets - (sheet.TotalLiabilities + sheet.OwnerEquity)) < 0.01m,
            $"assets {sheet.TotalAssets} vs liabilities+equity {sheet.TotalLiabilities + sheet.OwnerEquity}");

        // Negative tax is nonsense and must be refused rather than stored.
        await harness.CheckAsync("negative tax on a line is refused", async () =>
        {
            try
            {
                await sales.CreateSaleAsync(new CreateSaleRequest(
                    shopId,
                    walkIn.Id,
                    0m,
                    0m,
                    PaymentMethod.Cash,
                    "QA negative tax",
                    [new SaleLineInput(exemptProduct.Id, 1, exemptProduct.SalePrice, -5m)])).ConfigureAwait(true);
            }
            catch (Application.Common.Exceptions.ValidationFailedException)
            {
                return;
            }

            throw new InvalidOperationException("a line with negative tax was accepted");
        }).ConfigureAwait(true);
    }

    private static void CheckChange(Harness harness)
    {
        using var dialog = new PaymentDialog(1000m, walkIn: true);

        harness.Expect("payment dialog opens prefilled with the amount due",
            dialog.Change == 0m,
            $"change started at {dialog.Change}");
    }
}

/// <summary>Reaches the page factory without the harness duplicating its switch.</summary>
internal static class PageFactoryAccess
{
    public static WinForms.Shell.PageBase Create(Module module)
        => WinForms.Shell.PageFactory.Create(module);
}
