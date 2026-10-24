using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.UnitTests;

/// <summary>
/// The arithmetic every screen and report depends on. These are the rules the business
/// actually has, written down as assertions so a refactor cannot quietly change what a
/// customer owes.
/// </summary>
public class DomainInvariantTests
{
    [Fact]
    public void Outstanding_is_total_less_what_has_been_paid()
    {
        var invoice = new SalesInvoice { Total = 1000m, PaidAmount = 400m, Status = InvoiceStatus.Partial };

        Assert.Equal(600m, invoice.Outstanding);
    }

    [Fact]
    public void A_void_invoice_owes_nothing_however_much_it_was_worth()
    {
        // Voiding is the one case where the arithmetic is deliberately overridden: the
        // document still exists for the numbering, but nobody owes anything on it.
        var invoice = new SalesInvoice { Total = 1000m, PaidAmount = 0m, Status = InvoiceStatus.Void };

        Assert.Equal(0m, invoice.Outstanding);
    }

    [Fact]
    public void A_line_total_excludes_tax_and_the_tax_inclusive_figure_adds_it()
    {
        var line = new SalesItem { Qty = 3, UnitPrice = 100m, TaxAmount = 51m };

        Assert.Equal(300m, line.LineTotal);
        Assert.Equal(351m, line.LineTotalWithTax);
    }

    [Fact]
    public void An_exempt_line_adds_nothing_for_tax()
    {
        var line = new SalesItem { Qty = 2, UnitPrice = 250m, TaxAmount = 0m };

        Assert.Equal(line.LineTotal, line.LineTotalWithTax);
    }

    [Fact]
    public void A_purchase_bill_owes_the_unpaid_remainder()
    {
        var bill = new PurchaseBill { Total = 5000m, PaidAmount = 1500m };

        Assert.Equal(3500m, bill.Outstanding);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 99.99, 99.99)]
    [InlineData(7, 0.01, 0.07)]
    public void Line_total_is_quantity_times_price(int qty, decimal price, decimal expected)
        => Assert.Equal(expected, new SalesItem { Qty = qty, UnitPrice = price }.LineTotal);
}
