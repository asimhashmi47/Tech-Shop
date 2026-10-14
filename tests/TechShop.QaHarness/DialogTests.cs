using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Catalog;
using TechShop.Application.Features.Money;
using TechShop.Application.Features.Parties;
using TechShop.Application.Features.Purchases;
using TechShop.Application.Features.Sales;
using TechShop.Application.Features.Shops;
using TechShop.Application.Features.Stock;
using TechShop.Application.Features.Users;
using TechShop.WinForms;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Features.Admin;
using TechShop.WinForms.Features.Catalog;
using TechShop.WinForms.Features.Money;
using TechShop.WinForms.Features.Parties;
using TechShop.WinForms.Features.Pos;
using TechShop.WinForms.Features.Purchases;
using TechShop.WinForms.Features.Sales;
using TechShop.WinForms.Features.Stock;

namespace TechShop.QaHarness;

/// <summary>
/// Opens every dialog and checks it fits the screen.
///
/// A dialog sized on a big monitor loses its footer on a shop laptop, and the footer is
/// where Save, Print and Close live — so the operator cannot finish what they started.
/// A 1366x768 screen leaves about 720 usable pixels, which is less than several of these
/// dialogs originally asked for.
/// </summary>
internal static class DialogTests
{
    public static async Task RunAsync(Harness harness)
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 720);
        harness.Section($"dialogs fit the screen ({area.Width}x{area.Height} usable)");

        using var scope = AppHost.CreateScope();
        var sp = scope.ServiceProvider;

        var shops = await sp.GetRequiredService<ShopService>().LookupAsync().ConfigureAwait(true);
        var categories = await sp.GetRequiredService<CategoryService>().LookupAsync().ConfigureAwait(true);
        var suppliers = await sp.GetRequiredService<SupplierService>().LookupAsync().ConfigureAwait(true);
        var customers = await sp.GetRequiredService<CustomerService>().LookupAsync().ConfigureAwait(true);
        var expenseCategories = await sp.GetRequiredService<ExpenseService>().CategoriesAsync().ConfigureAwait(true);

        var products = await sp.GetRequiredService<ProductService>()
            .QueryAsync(new QueryRequest { PageSize = 200 }).ConfigureAwait(true);

        var invoices = await sp.GetRequiredService<SalesService>()
            .QueryAsync(new QueryRequest { PageSize = 20, Sort = [new SortRule { Field = "invoiceDate", Desc = true }] })
            .ConfigureAwait(true);

        var invoice = await sp.GetRequiredService<SalesService>()
            .GetAsync(invoices.Items[0].Id).ConfigureAwait(true);

        var bills = await sp.GetRequiredService<PurchaseService>()
            .QueryAsync(new QueryRequest { PageSize = 5 }).ConfigureAwait(true);

        var bill = await sp.GetRequiredService<PurchaseService>()
            .GetAsync(bills.Items[0].Id).ConfigureAwait(true);

        var levels = await sp.GetRequiredService<StockService>()
            .QueryLevelsAsync(new QueryRequest { PageSize = 5 }).ConfigureAwait(true);

        var ledger = await sp.GetRequiredService<Application.Features.Ledgers.LedgerService>()
            .CustomerLedgerAsync(customers[0].Id, null, null, null).ConfigureAwait(true);

        var user = await sp.GetRequiredService<UserService>()
            .GetAsync((await sp.GetRequiredService<UserService>()
                .QueryAsync(new QueryRequest { PageSize = 1 }).ConfigureAwait(true)).Items[0].Id)
            .ConfigureAwait(true);

        Check(harness, "Invoice (A4)", area, () => new InvoiceDetailDialog(invoice));
        Check(harness, "Invoice (80mm)", area, () => new InvoiceDetailDialog(invoice, InvoiceFormat.Receipt80));
        Check(harness, "Purchase bill", area, () => new PurchaseBillDialog(bill));
        Check(harness, "New purchase bill", area, () => new PurchaseBillEditor(
            sp.GetRequiredService<PurchaseService>(), shops, [.. suppliers], products.Items, shops[0].Id));
        Check(harness, "Customer ledger", area, () => new LedgerDialog("QA ledger", ledger));
        Check(harness, "Stock movements", area, () => new StockMovementsDialog(
            sp.GetRequiredService<StockService>(), null, null));
        Check(harness, "Edit user", area, () => new UserEditor(sp.GetRequiredService<UserService>(), shops, user));
        Check(harness, "Payment", area, () => new PaymentDialog(1234.56m, walkIn: true));
        Check(harness, "New product", area, () => new ProductEditor(
            sp.GetRequiredService<ProductService>(), categories, null));
        Check(harness, "New customer", area, () => new CustomerEditor(
            sp.GetRequiredService<CustomerService>(), null));
        Check(harness, "New expense", area, () => new ExpenseEditor(
            sp.GetRequiredService<ExpenseService>(), shops, expenseCategories, null));
        Check(harness, "Stock adjustment", area, () => new StockAdjustEditor(
            sp.GetRequiredService<StockService>(), levels.Items[0]));
    }

    private static void Check(Harness harness, string name, Rectangle area, Func<Form> build)
    {
        Form? dialog = null;

        try
        {
            dialog = build();
            dialog.ShowInTaskbar = false;
            dialog.Show();
            System.Windows.Forms.Application.DoEvents();

            harness.Expect($"{name}: fits the working area",
                dialog.Width <= area.Width && dialog.Height <= area.Height,
                $"{dialog.Width}x{dialog.Height} against {area.Width}x{area.Height} usable");

            harness.Expect($"{name}: sits fully on screen",
                dialog.Left >= area.Left && dialog.Top >= area.Top
                && dialog.Right <= area.Right && dialog.Bottom <= area.Bottom,
                $"at {dialog.Bounds}, screen is {area}");

            // The footer is what gets lost when a dialog is too tall, and it is the part
            // holding Save, Print and Close.
            var bottomMost = dialog.Controls.OfType<Control>()
                .Where(c => c.Dock == DockStyle.Bottom && c.Visible)
                .ToList();

            foreach (var footer in bottomMost)
            {
                harness.Expect($"{name}: footer is inside the client area",
                    footer.Bottom <= dialog.ClientSize.Height && footer.Top >= 0,
                    $"footer at {footer.Bounds} in client {dialog.ClientSize}");

                var buttons = footer.Controls.OfType<Control>().Where(c => c.Visible).ToList();

                harness.Expect($"{name}: footer controls are inside the window",
                    buttons.TrueForAll(b => b.Bottom <= footer.ClientSize.Height && b.Right <= footer.ClientSize.Width),
                    string.Join(", ", buttons
                        .Where(b => b.Bottom > footer.ClientSize.Height || b.Right > footer.ClientSize.Width)
                        .Select(b => $"{b.Text} at {b.Bounds} in {footer.ClientSize}")));
            }

            dialog.Close();
        }
        catch (Exception ex)
        {
            harness.Fail($"{name}: opens", ex.Message);
        }
        finally
        {
            dialog?.Dispose();
        }
    }
}
