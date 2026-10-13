using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechShop.Domain.Enums;
using TechShop.Infrastructure.Persistence;

namespace TechShop.QaHarness;

/// <summary>
/// Checks the figures in the database agree with each other.
///
/// These are the identities the app claims to maintain: stock equals the sum of its
/// movements, an invoice equals its lines, tax on an invoice equals the tax on its
/// lines, and a product that is exempt is never taxed. They are asserted against the
/// freshly seeded database so a change to the seeder or to a service that quietly breaks
/// one of them fails the run rather than surfacing as a wrong number on a report months
/// later.
/// </summary>
internal static class DataIntegrityTests
{
    public static async Task RunAsync(Harness harness, IServiceProvider provider)
    {
        harness.Section("data integrity");

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // ---------------------------------------------------------- volume
        var products = await db.Products.CountAsync().ConfigureAwait(true);
        var invoices = await db.SalesInvoices.CountAsync().ConfigureAwait(true);

        harness.Expect("the catalogue has at least 50 products", products >= 50, $"only {products}");
        harness.Expect("there is six months of trading", invoices >= 500, $"only {invoices} invoices");

        harness.Expect(
            "the catalogue is a mix of taxable and exempt",
            await db.Products.AnyAsync(p => p.IsTaxable).ConfigureAwait(true)
            && await db.Products.AnyAsync(p => !p.IsTaxable).ConfigureAwait(true),
            "every product is taxed the same way, so the per-line case is never exercised");

        // ---------------------------------------------------------- stock
        var negativeStock = await db.StockLevels.CountAsync(s => s.Qty < 0).ConfigureAwait(true);
        harness.Expect("no stock level is negative", negativeStock == 0, $"{negativeStock} rows below zero");

        var levels = await db.StockLevels
            .Select(l => new { l.ShopId, l.ProductId, l.Qty })
            .ToListAsync().ConfigureAwait(true);

        var movements = await db.StockMovements
            .GroupBy(m => new { m.ShopId, m.ProductId })
            .Select(g => new { g.Key.ShopId, g.Key.ProductId, Qty = g.Sum(m => m.Qty) })
            .ToListAsync().ConfigureAwait(true);

        var moveBy = movements.ToDictionary(m => (m.ShopId, m.ProductId), m => m.Qty);

        var drift = levels.Count(l =>
            moveBy.GetValueOrDefault((l.ShopId, l.ProductId)) != l.Qty);

        harness.Expect("stock equals the sum of its movements", drift == 0, $"{drift} lines disagree");

        // ---------------------------------------------------------- invoices
        var lineSums = await db.SalesInvoices
            .Select(i => new
            {
                i.Id,
                i.SubTotal,
                i.Discount,
                i.TaxTotal,
                i.Total,
                i.PaidAmount,
                i.Status,
                Goods = i.Items.Sum(x => x.Qty * x.UnitPrice),
                LineTax = i.Items.Sum(x => x.TaxAmount),
            })
            .ToListAsync().ConfigureAwait(true);

        harness.Expect(
            "every invoice subtotal equals its lines",
            lineSums.TrueForAll(i => Math.Abs(i.SubTotal - i.Goods) < 0.01m),
            $"{lineSums.Count(i => Math.Abs(i.SubTotal - i.Goods) >= 0.01m)} invoices disagree");

        harness.Expect(
            "every invoice tax equals the tax on its lines",
            lineSums.TrueForAll(i => Math.Abs(i.TaxTotal - i.LineTax) < 0.01m),
            $"{lineSums.Count(i => Math.Abs(i.TaxTotal - i.LineTax) >= 0.01m)} invoices disagree");

        harness.Expect(
            "every invoice total is goods less discount plus tax",
            lineSums.TrueForAll(i => Math.Abs(i.Total - (i.SubTotal - i.Discount + i.TaxTotal)) < 0.01m),
            $"{lineSums.Count(i => Math.Abs(i.Total - (i.SubTotal - i.Discount + i.TaxTotal)) >= 0.01m)} invoices disagree");

        harness.Expect(
            "no invoice is overpaid",
            lineSums.TrueForAll(i => i.PaidAmount <= i.Total + 0.01m),
            "an invoice has more money against it than it is worth");

        harness.Expect(
            "a void invoice holds no money",
            lineSums.Where(i => i.Status == InvoiceStatus.Void).All(i => i.PaidAmount == 0m),
            "a voided invoice still has a payment against it");

        // ---------------------------------------------------------- tax
        var taxedExempt = await db.SalesItems
            .CountAsync(s => !s.Product.IsTaxable && s.TaxAmount != 0m).ConfigureAwait(true);

        harness.Expect("an exempt product is never taxed", taxedExempt == 0,
            $"{taxedExempt} lines tax a product marked exempt");

        var negativeTax = await db.SalesItems.CountAsync(s => s.TaxAmount < 0m).ConfigureAwait(true);
        harness.Expect("no line carries negative tax", negativeTax == 0, $"{negativeTax} lines");

        var mixed = lineSums.Count(i => i.LineTax > 0m)
            + await db.SalesInvoices
                .CountAsync(i => i.Items.Any(x => x.TaxAmount > 0m) && i.Items.Any(x => x.TaxAmount == 0m))
                .ConfigureAwait(true);

        harness.Expect("some invoices mix taxed and untaxed lines", mixed > 0,
            "no invoice has both a taxed and an untaxed line, so the feature is untested by the data");

        // ---------------------------------------------------------- money trails
        var customerDrift = await db.Customers
            .Select(c => new
            {
                Invoiced = db.SalesInvoices
                    .Where(i => i.CustomerId == c.Id && i.Status != InvoiceStatus.Void)
                    .Sum(i => i.PaidAmount),
                Received = db.CustomerPayments.Where(p => p.CustomerId == c.Id).Sum(p => p.Amount),
            })
            .ToListAsync().ConfigureAwait(true);

        harness.Expect(
            "receipts match what invoices say was paid",
            customerDrift.TrueForAll(c => Math.Abs(c.Invoiced - c.Received) < 0.01m),
            $"{customerDrift.Count(c => Math.Abs(c.Invoiced - c.Received) >= 0.01m)} customers disagree");

        var drawers = await db.PettyCashEntries
            .GroupBy(p => p.ShopId)
            .Select(g => g.Sum(p => p.Direction == PettyCashDirection.In ? p.Amount : -p.Amount))
            .ToListAsync().ConfigureAwait(true);

        harness.Expect("no cash drawer is negative", drawers.TrueForAll(b => b >= 0m),
            "a shop's drawer holds less than nothing");
    }
}
