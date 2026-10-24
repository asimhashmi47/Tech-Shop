using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Money;
using TechShop.Application.Features.Sales;
using TechShop.Application.Features.Stock;
using TechShop.Domain.Enums;

namespace TechShop.IntegrationTests;

/// <summary>
/// Selling, end to end, against a real database: what a sale does to stock, to the
/// customer account, to the drawer and to the books.
/// </summary>
[Collection("shop")]
public class SellingTests(ShopFixture shop)
{
    [Fact]
    public async Task A_cash_sale_moves_stock_books_a_receipt_and_fills_the_drawer()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId);
        var before = await shop.OnHand(shopId, line.ProductId);

        var drawerBefore = await shop.InScope(sp =>
            sp.GetRequiredService<PettyCashService>().BalanceAsync(shopId));

        var walkIn = await shop.WalkInCustomerId();

        var invoice = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                walkIn,
                0m,
                2 * line.CostPrice,
                PaymentMethod.Cash,
                "unit sale",
                [new SaleLineInput(line.ProductId, 2, line.CostPrice)])));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0m, invoice.Outstanding);

        Assert.Equal(before - 2, await shop.OnHand(shopId, line.ProductId));

        var movements = await shop.InScope(sp =>
            sp.GetRequiredService<StockService>().QueryMovementsAsync(
                new QueryRequest { PageSize = 10, Search = invoice.InvoiceNo }));

        Assert.Contains(movements.Items, m => m.Type == StockMovementType.Sale && m.Qty == -2);

        var receipts = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().QueryPaymentsAsync(
                new QueryRequest { PageSize = 10, Search = invoice.InvoiceNo }));

        Assert.NotEmpty(receipts.Items);

        var drawerAfter = await shop.InScope(sp =>
            sp.GetRequiredService<PettyCashService>().BalanceAsync(shopId));

        Assert.Equal(drawerBefore + invoice.Total, drawerAfter);
    }

    [Fact]
    public async Task Selling_more_than_is_on_the_shelf_is_refused()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId);
        var onHand = await shop.OnHand(shopId, line.ProductId);

        await Assert.ThrowsAsync<ValidationFailedException>(() => shop.InScope(async sp =>
            await sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                await shop.WalkInCustomerId(),
                0m,
                0m,
                PaymentMethod.Cash,
                "oversell",
                [new SaleLineInput(line.ProductId, onHand + 1, 10m)]))));

        Assert.Equal(onHand, await shop.OnHand(shopId, line.ProductId));
    }

    [Fact]
    public async Task A_refused_sale_leaves_no_invoice_behind()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId);
        var onHand = await shop.OnHand(shopId, line.ProductId);

        var countBefore = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().QueryAsync(new QueryRequest { PageSize = 1 }));

        // Two lines: the first would succeed, the second cannot. The whole sale has to
        // roll back, or the shop ends up with half an invoice and missing stock.
        await Assert.ThrowsAsync<ValidationFailedException>(() => shop.InScope(async sp =>
            await sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                await shop.WalkInCustomerId(),
                0m,
                0m,
                PaymentMethod.Cash,
                "partial failure",
                [
                    new SaleLineInput(line.ProductId, 1, 10m),
                    new SaleLineInput(line.ProductId, onHand + 50, 10m),
                ]))));

        var countAfter = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().QueryAsync(new QueryRequest { PageSize = 1 }));

        Assert.Equal(countBefore.TotalCount, countAfter.TotalCount);
        Assert.Equal(onHand, await shop.OnHand(shopId, line.ProductId));
    }

    [Fact]
    public async Task A_walk_in_sale_cannot_be_left_unpaid()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId);

        await Assert.ThrowsAsync<ValidationFailedException>(() => shop.InScope(async sp =>
            await sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                await shop.WalkInCustomerId(),
                0m,
                0m,
                PaymentMethod.Credit,
                "walk-in on credit",
                [new SaleLineInput(line.ProductId, 1, line.CostPrice)]))));
    }

    [Fact]
    public async Task A_named_customer_may_take_goods_on_credit_and_owes_the_balance()
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
                "on account",
                [new SaleLineInput(line.ProductId, 1, 500m)])));

        Assert.Equal(InvoiceStatus.Unpaid, invoice.Status);
        Assert.Equal(invoice.Total, invoice.Outstanding);
    }

    [Fact]
    public async Task Paying_more_than_is_owed_is_refused()
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
                "overpay target",
                [new SaleLineInput(line.ProductId, 1, 400m)])));

        await Assert.ThrowsAsync<ValidationFailedException>(() => shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().ReceiveAsync(new CreateCustomerPaymentRequest(
                shopId,
                invoice.CustomerId,
                invoice.Id,
                DateTime.UtcNow,
                invoice.Total + 1000m,
                PaymentMethod.Cash,
                "too much"))));
    }

    [Fact]
    public async Task Voiding_returns_the_stock_and_clears_what_is_owed()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId);
        var before = await shop.OnHand(shopId, line.ProductId);

        var account = await shop.AccountCustomerId();

        var invoice = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                account,
                0m,
                0m,
                PaymentMethod.Credit,
                "to be voided",
                [new SaleLineInput(line.ProductId, 3, 100m)])));

        Assert.Equal(before - 3, await shop.OnHand(shopId, line.ProductId));

        var voided = await shop.InScope(sp =>
            sp.GetRequiredService<SalesService>().VoidAsync(invoice.Id, new VoidInvoiceRequest("changed mind")));

        Assert.Equal(InvoiceStatus.Void, voided.Status);
        Assert.Equal(0m, voided.Outstanding);
        Assert.Equal(before, await shop.OnHand(shopId, line.ProductId));
    }

    [Fact]
    public async Task A_discount_cannot_exceed_the_goods()
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId);

        await Assert.ThrowsAsync<ValidationFailedException>(() => shop.InScope(async sp =>
            await sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                await shop.WalkInCustomerId(),
                99_999_999m,
                0m,
                PaymentMethod.Cash,
                "absurd discount",
                [new SaleLineInput(line.ProductId, 1, 100m)]))));
    }

    [Fact]
    public async Task An_empty_cart_is_refused()
        => await Assert.ThrowsAsync<ValidationFailedException>(() => shop.InScope(async sp =>
            await sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                await shop.TradingShopId(),
                await shop.WalkInCustomerId(),
                0m,
                0m,
                PaymentMethod.Cash,
                "empty",
                []))));

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task A_line_needs_a_positive_quantity(int qty)
    {
        var shopId = await shop.TradingShopId();
        var line = await shop.StockedLine(shopId);

        await Assert.ThrowsAsync<ValidationFailedException>(() => shop.InScope(async sp =>
            await sp.GetRequiredService<SalesService>().CreateSaleAsync(new CreateSaleRequest(
                shopId,
                await shop.WalkInCustomerId(),
                0m,
                0m,
                PaymentMethod.Cash,
                "bad qty",
                [new SaleLineInput(line.ProductId, qty, 100m)]))));
    }
}
