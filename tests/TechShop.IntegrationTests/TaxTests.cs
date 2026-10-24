using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Features.Reports;
using TechShop.Application.Features.Sales;
using TechShop.Domain.Enums;

namespace TechShop.IntegrationTests;

/// <summary>
/// Sales tax, charged per line. The case the whole feature exists for is a basket
/// holding a taxable item and an exempt one, so most of these use exactly that.
/// </summary>
[Collection("shop")]
public class TaxTests(ShopFixture shop)
{
    [Fact]
    public async Task Tax_on_an_invoice_is_the_sum_of_the_tax_on_its_lines()
    {
        var shopId = await shop.TradingShopId();
        var taxable = await shop.StockedLine(shopId, taxable: true);
        var exempt = await shop.StockedLine(shopId, taxable: false);
        var walkIn = await shop.WalkInCustomerId();

        var goods = (2 * 1000m) + 500m;
        var tax = 340m;

        var invoice = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                walkIn,
                0m,
                goods + tax,
                PaymentMethod.Cash,
                "mixed basket",
                [
                    new SaleLineInput(taxable.ProductId, 2, 1000m, tax),
                    new SaleLineInput(exempt.ProductId, 1, 500m, 0m),
                ])));

        Assert.Equal(tax, invoice.TaxTotal);
        Assert.Equal(goods, invoice.SubTotal);
        Assert.Equal(goods + tax, invoice.Total);

        Assert.Equal(0m, invoice.Items.Single(i => i.ProductId == exempt.ProductId).TaxAmount);
        Assert.Equal(tax, invoice.Items.Single(i => i.ProductId == taxable.ProductId).TaxAmount);
    }

    [Fact]
    public async Task Tax_is_added_after_the_discount_and_is_never_discounted()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId, taxable: true);
        var walkIn = await shop.WalkInCustomerId();

        var invoice = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                walkIn,
                100m,
                1000m - 100m + 170m,
                PaymentMethod.Cash,
                "discounted and taxed",
                [new SaleLineInput(line.ProductId, 1, 1000m, 170m)])));

        // Goods 1000, discount 100, tax 170 -> 1070. The discount reduces the goods,
        // never the tax, because the shop still owes the full tax onward.
        Assert.Equal(1000m, invoice.SubTotal);
        Assert.Equal(100m, invoice.Discount);
        Assert.Equal(170m, invoice.TaxTotal);
        Assert.Equal(1070m, invoice.Total);
    }

    [Fact]
    public async Task A_line_cannot_carry_negative_tax()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId);
        var walkIn = await shop.WalkInCustomerId();

        await Assert.ThrowsAsync<ValidationFailedException>(() => shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                walkIn,
                0m,
                0m,
                PaymentMethod.Cash,
                "negative tax",
                [new SaleLineInput(line.ProductId, 1, 100m, -5m)]))));
    }

    [Fact]
    public async Task Tax_collected_is_a_liability_and_not_revenue()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId, taxable: true);
        var walkIn = await shop.WalkInCustomerId();
        var filter = new ReportFilter(shopId, null, null);

        var before = await shop.InScope(sp => sp.GetRequiredService<ReportService>().BalanceSheetAsync(filter));

        await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                walkIn,
                0m,
                1170m,
                PaymentMethod.Cash,
                "taxed sale",
                [new SaleLineInput(line.ProductId, 1, 1000m, 170m)])));

        var after = await shop.InScope(sp => sp.GetRequiredService<ReportService>().BalanceSheetAsync(filter));

        Assert.Equal(before.TaxPayable + 170m, after.TaxPayable);

        // And the sheet still balances, which is what makes the liability honest.
        Assert.True(
            Math.Abs(after.TotalAssets - (after.TotalLiabilities + after.OwnerEquity)) < 0.01m,
            $"assets {after.TotalAssets} vs liabilities+equity {after.TotalLiabilities + after.OwnerEquity}");
    }

    [Fact]
    public async Task Voiding_a_taxed_invoice_removes_the_tax_liability_it_created()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId, taxable: true);
        var account = await shop.AccountCustomerId();
        var filter = new ReportFilter(shopId, null, null);

        var invoice = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                account,
                0m,
                0m,
                PaymentMethod.Credit,
                "taxed, to be voided",
                [new SaleLineInput(line.ProductId, 1, 1000m, 170m)])));

        var before = await shop.InScope(sp => sp.GetRequiredService<ReportService>().BalanceSheetAsync(filter));

        await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().VoidAsync(invoice.Id, new VoidInvoiceRequest("cancelled")));

        var after = await shop.InScope(sp => sp.GetRequiredService<ReportService>().BalanceSheetAsync(filter));

        Assert.Equal(before.TaxPayable - 170m, after.TaxPayable);
    }

    [Fact]
    public async Task Revenue_in_the_profit_and_loss_excludes_the_tax_charged()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId, taxable: true);
        var walkIn = await shop.WalkInCustomerId();
        var filter = new ReportFilter(shopId, DateTime.UtcNow.Date, DateTime.UtcNow.Date);

        var before = await shop.InScope(sp => sp.GetRequiredService<ReportService>().ProfitAndLossAsync(filter));

        await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                walkIn,
                0m,
                1170m,
                PaymentMethod.Cash,
                "revenue check",
                [new SaleLineInput(line.ProductId, 1, 1000m, 170m)])));

        var after = await shop.InScope(sp => sp.GetRequiredService<ReportService>().ProfitAndLossAsync(filter));

        // Revenue moves by the goods only. If it moved by 1170 the shop would be
        // reporting the tax authority's money as its own turnover.
        Assert.Equal(before.Revenue + 1000m, after.Revenue);
    }
}
