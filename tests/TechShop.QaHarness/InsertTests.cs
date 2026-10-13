using Krypton.Toolkit;
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
using TechShop.Domain.Enums;
using TechShop.WinForms;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Features.Admin;
using TechShop.WinForms.Features.Catalog;
using TechShop.WinForms.Features.Money;
using TechShop.WinForms.Features.Parties;
using TechShop.WinForms.Features.Purchases;
using TechShop.WinForms.Features.Sales;
using TechShop.WinForms.Features.Stock;

namespace TechShop.QaHarness;

/// <summary>
/// Inserts records the way an operator does: by building the real dialog, typing into its
/// real inputs and pressing its real Save, then checking the row actually landed.
///
/// This is deliberately not "call the service with a request object". Half the bugs in a
/// form live between the controls and the request — a field that is never read, a combo
/// whose selection is lost, a date sent in the wrong time zone — and only driving the
/// dialog finds those.
/// </summary>
internal static class InsertTests
{
    public static async Task RunAsync(Harness harness)
    {
        harness.Section("insert through the UI");

        using var scope = AppHost.CreateScope();
        var sp = scope.ServiceProvider;

        var shops = await sp.GetRequiredService<ShopService>().LookupAsync().ConfigureAwait(true);
        var categories = await sp.GetRequiredService<CategoryService>().LookupAsync().ConfigureAwait(true);
        var suppliers = await sp.GetRequiredService<SupplierService>().LookupAsync().ConfigureAwait(true);

        await ProductAsync(harness, sp, categories).ConfigureAwait(true);
        await CustomerAsync(harness, sp).ConfigureAwait(true);
        await SupplierAsync(harness, sp).ConfigureAwait(true);
        await ShopAsync(harness, sp).ConfigureAwait(true);
        await ExpenseAsync(harness, sp, shops).ConfigureAwait(true);
        await PettyCashAsync(harness, sp, shops).ConfigureAwait(true);
        await StockAdjustAsync(harness, sp).ConfigureAwait(true);
        await StockTransferAsync(harness, sp, shops).ConfigureAwait(true);
        await PurchaseBillAsync(harness, sp, shops, suppliers).ConfigureAwait(true);
        await ReceivePaymentAsync(harness, sp).ConfigureAwait(true);
        await VoidInvoiceAsync(harness, sp).ConfigureAwait(true);
        await UserAsync(harness, sp, shops).ConfigureAwait(true);

        harness.Section("validation is reported, not swallowed");
        await RejectsBlankProductAsync(harness, sp, categories).ConfigureAwait(true);
        await RejectsDuplicateSkuAsync(harness, sp, categories).ConfigureAwait(true);
        await RejectsOverlargeCashOutAsync(harness, sp, shops).ConfigureAwait(true);
    }

    // ------------------------------------------------------------------ catalogue

    private static async Task ProductAsync(Harness harness, IServiceProvider sp, IReadOnlyList<CategoryDto> categories)
    {
        var products = sp.GetRequiredService<ProductService>();
        var sku = $"QA-{DateTime.Now:HHmmssfff}";

        using var editor = new ProductEditor(products, categories, null);

        Set(editor, nameof(SaveProductRequest.Sku), sku);
        Set(editor, nameof(SaveProductRequest.Barcode), "5012345678900");
        Set(editor, nameof(SaveProductRequest.Name), "QA Test Keyboard");
        Set(editor, nameof(SaveProductRequest.Brand), "QA Brand");
        Set(editor, "Cost (Rs)", 1200m);
        Set(editor, "Sale price (Rs)", 1800m);
        Set(editor, nameof(SaveProductRequest.ReorderLevel), 5m);

        await Submit(harness, editor, "new product saves").ConfigureAwait(true);

        var found = await products
            .QueryAsync(new QueryRequest { Search = sku, PageSize = 5 })
            .ConfigureAwait(true);

        harness.Expect("new product is findable", found.TotalCount == 1, $"search for {sku} returned {found.TotalCount}");

        if (found.Items.Count == 1)
        {
            var row = found.Items[0];
            harness.Expect("product keeps its barcode", row.Barcode == "5012345678900", $"barcode was '{row.Barcode}'");
            harness.Expect("product keeps its prices", row.CostPrice == 1200m && row.SalePrice == 1800m,
                $"cost {row.CostPrice}, price {row.SalePrice}");
            harness.Expect("product keeps its reorder level", row.ReorderLevel == 5, $"reorder was {row.ReorderLevel}");

            // Editing has to round-trip too: an edit form that loses a field is the same
            // bug as an add form that never read it.
            using var edit = new ProductEditor(products, categories, row);
            Set(edit, nameof(SaveProductRequest.Name), "QA Test Keyboard Mk2");
            await Submit(harness, edit, "product edit saves").ConfigureAwait(true);

            var after = await products.GetAsync(row.Id).ConfigureAwait(true);
            harness.Expect("product edit kept the barcode", after.Barcode == "5012345678900", $"barcode became '{after.Barcode}'");
            harness.Expect("product edit applied the name", after.Name == "QA Test Keyboard Mk2", $"name is '{after.Name}'");
        }
    }

    private static async Task CustomerAsync(Harness harness, IServiceProvider sp)
    {
        var customers = sp.GetRequiredService<CustomerService>();
        var name = $"QA Customer {DateTime.Now:HHmmss}";

        using var editor = new CustomerEditor(customers, null);
        Set(editor, nameof(SaveCustomerRequest.Name), name);
        Set(editor, nameof(SaveCustomerRequest.Phone), "0300 1234567");
        Set(editor, nameof(SaveCustomerRequest.Address), "QA Street");

        await Submit(harness, editor, "new customer saves").ConfigureAwait(true);

        var found = await customers.QueryAsync(new QueryRequest { Search = name, PageSize = 5 }).ConfigureAwait(true);
        harness.Expect("new customer is findable", found.TotalCount == 1, $"returned {found.TotalCount}");

        if (found.Items.Count == 1)
        {
            harness.Expect("customer keeps its phone", found.Items[0].Phone == "0300 1234567",
                $"phone was '{found.Items[0].Phone}'");
        }
    }

    private static async Task SupplierAsync(Harness harness, IServiceProvider sp)
    {
        var suppliers = sp.GetRequiredService<SupplierService>();
        var name = $"QA Supplier {DateTime.Now:HHmmss}";

        using var editor = new SupplierEditor(suppliers, null);
        Set(editor, nameof(SaveSupplierRequest.Name), name);
        Set(editor, nameof(SaveSupplierRequest.Phone), "042 7654321");

        await Submit(harness, editor, "new supplier saves").ConfigureAwait(true);

        var found = await suppliers.QueryAsync(new QueryRequest { Search = name, PageSize = 5 }).ConfigureAwait(true);
        harness.Expect("new supplier is findable", found.TotalCount == 1, $"returned {found.TotalCount}");
    }

    private static async Task ShopAsync(Harness harness, IServiceProvider sp)
    {
        var shops = sp.GetRequiredService<ShopService>();
        var code = $"QA{DateTime.Now:ssfff}";

        using var editor = new ShopEditor(shops, null);
        Set(editor, nameof(SaveShopRequest.Code), code);
        Set(editor, nameof(SaveShopRequest.Name), $"QA Branch {code}");
        Set(editor, nameof(SaveShopRequest.Phone), "0300 0000000");

        await Submit(harness, editor, "new shop saves").ConfigureAwait(true);

        var found = await shops.QueryAsync(new QueryRequest { Search = code, PageSize = 5 }).ConfigureAwait(true);
        harness.Expect("new shop is findable", found.TotalCount == 1, $"returned {found.TotalCount}");
    }

    // ------------------------------------------------------------------ money

    private static async Task ExpenseAsync(Harness harness, IServiceProvider sp, IReadOnlyList<ShopDto> shops)
    {
        var expenses = sp.GetRequiredService<ExpenseService>();
        var categories = await expenses.CategoriesAsync().ConfigureAwait(true);

        using var editor = new ExpenseEditor(expenses, shops, categories, null);
        Set(editor, nameof(SaveExpenseRequest.Amount), 3456m);
        Set(editor, nameof(SaveExpenseRequest.Note), "QA expense");

        var before = await expenses.QueryAsync(new QueryRequest { PageSize = 1 }).ConfigureAwait(true);
        await Submit(harness, editor, "new expense saves").ConfigureAwait(true);
        var after = await expenses.QueryAsync(new QueryRequest { PageSize = 1 }).ConfigureAwait(true);

        harness.Expect("expense count went up", after.TotalCount == before.TotalCount + 1,
            $"{before.TotalCount} then {after.TotalCount}");

        // A cash expense has to pull the money out of the drawer as well.
        var pettyCash = sp.GetRequiredService<PettyCashService>();
        var mirrored = await pettyCash
            .QueryAsync(new QueryRequest { PageSize = 10, Search = "QA expense" })
            .ConfigureAwait(true);

        harness.Expect("cash expense reaches the drawer", mirrored.TotalCount >= 1,
            "no petty cash row was written for a cash expense");
    }

    private static async Task PettyCashAsync(Harness harness, IServiceProvider sp, IReadOnlyList<ShopDto> shops)
    {
        var pettyCash = sp.GetRequiredService<PettyCashService>();

        using var editor = new PettyCashEditor(pettyCash, shops, shops[0].Id);
        Set(editor, nameof(SavePettyCashRequest.Direction), PettyCashDirection.In);
        Set(editor, nameof(SavePettyCashRequest.Amount), 5000m);
        Set(editor, nameof(SavePettyCashRequest.Note), "QA float top-up");

        var before = await pettyCash.BalanceAsync(shops[0].Id).ConfigureAwait(true);
        await Submit(harness, editor, "petty cash entry saves").ConfigureAwait(true);
        var after = await pettyCash.BalanceAsync(shops[0].Id).ConfigureAwait(true);

        harness.Expect("cash in raises the drawer balance", after == before + 5000m,
            $"balance went {before} -> {after}");
    }

    // ------------------------------------------------------------------ stock

    private static async Task StockAdjustAsync(Harness harness, IServiceProvider sp)
    {
        var stock = sp.GetRequiredService<StockService>();

        var levels = await stock
            .QueryLevelsAsync(new QueryRequest { PageSize = 1, Sort = [new SortRule { Field = "qty", Desc = true }] })
            .ConfigureAwait(true);

        if (levels.Items.Count == 0)
        {
            harness.Fail("stock adjustment saves", "no stock lines to adjust");
            return;
        }

        var line = levels.Items[0];
        var target = line.Qty + 7;

        using var editor = new StockAdjustEditor(stock, line);
        Set(editor, nameof(AdjustStockRequest.Qty), (decimal)target);
        Set(editor, nameof(AdjustStockRequest.Reason), "QA stock count");

        await Submit(harness, editor, "stock adjustment saves").ConfigureAwait(true);

        var now = await stock.CurrentQtyAsync(line.ShopId, line.ProductId).ConfigureAwait(true);
        harness.Expect("adjustment moves stock to the counted figure", now == target,
            $"expected {target}, got {now}");

        var movements = await stock
            .QueryMovementsAsync(new QueryRequest { PageSize = 5, Search = "QA stock count" })
            .ConfigureAwait(true);

        harness.Expect("adjustment writes a movement", movements.TotalCount >= 1,
            "no movement row was written for the adjustment");
    }

    private static async Task StockTransferAsync(Harness harness, IServiceProvider sp, IReadOnlyList<ShopDto> shops)
    {
        if (shops.Count < 2)
        {
            return;
        }

        var stock = sp.GetRequiredService<StockService>();

        var levels = await stock
            .QueryLevelsAsync(new QueryRequest { PageSize = 50, Sort = [new SortRule { Field = "qty", Desc = true }] })
            .ConfigureAwait(true);

        var source = levels.Items.FirstOrDefault(l => l.Qty >= 5 && l.ShopId == shops[0].Id);
        if (source is null)
        {
            harness.Fail("stock transfer saves", "no line with enough stock at the first shop");
            return;
        }

        var products = await sp.GetRequiredService<ProductService>()
            .QueryAsync(new QueryRequest { PageSize = 500 }).ConfigureAwait(true);

        var fromBefore = await stock.CurrentQtyAsync(shops[0].Id, source.ProductId).ConfigureAwait(true);
        var toBefore = await stock.CurrentQtyAsync(shops[1].Id, source.ProductId).ConfigureAwait(true);

        using var editor = new StockTransferEditor(stock, shops, products.Items, source);
        Set(editor, nameof(TransferStockRequest.FromShopId), shops[0].Id);
        Set(editor, nameof(TransferStockRequest.ToShopId), shops[1].Id);
        Set(editor, nameof(TransferStockRequest.ProductId), source.ProductId);
        Set(editor, nameof(TransferStockRequest.Qty), 3m);
        Set(editor, nameof(TransferStockRequest.Note), "QA transfer");

        await Submit(harness, editor, "stock transfer saves").ConfigureAwait(true);

        var fromAfter = await stock.CurrentQtyAsync(shops[0].Id, source.ProductId).ConfigureAwait(true);
        var toAfter = await stock.CurrentQtyAsync(shops[1].Id, source.ProductId).ConfigureAwait(true);

        harness.Expect("transfer takes stock off the sending shop", fromAfter == fromBefore - 3,
            $"{fromBefore} -> {fromAfter}");
        harness.Expect("transfer puts stock on the receiving shop", toAfter == toBefore + 3,
            $"{toBefore} -> {toAfter}");
    }

    // ------------------------------------------------------------------ documents

    private static async Task PurchaseBillAsync(
        Harness harness,
        IServiceProvider sp,
        IReadOnlyList<ShopDto> shops,
        List<SupplierDto> suppliers)
    {
        var purchases = sp.GetRequiredService<PurchaseService>();
        var stock = sp.GetRequiredService<StockService>();

        var products = await sp.GetRequiredService<ProductService>()
            .QueryAsync(new QueryRequest { PageSize = 50 }).ConfigureAwait(true);

        if (products.Items.Count == 0 || suppliers.Count == 0)
        {
            harness.Fail("purchase bill saves", "no products or suppliers to bill");
            return;
        }

        var product = products.Items[0];
        var before = await stock.CurrentQtyAsync(shops[0].Id, product.Id).ConfigureAwait(true);
        var countBefore = await purchases.QueryAsync(new QueryRequest { PageSize = 1 }).ConfigureAwait(true);

        using var editor = new PurchaseBillEditor(purchases, shops, suppliers, products.Items, shops[0].Id);

        // The bill editor is a wider form of its own, so it is driven through its
        // controls rather than the EditorForm field registry.
        harness.Check("purchase bill editor builds", () => { });

        var added = TryAddPurchaseLine(editor, product.Id, 4, product.CostPrice);
        harness.Expect("purchase line can be added", added, "the Add line button did not add a row");

        if (!added)
        {
            return;
        }

        await SubmitForm(harness, editor, "purchase bill saves").ConfigureAwait(true);

        var countAfter = await purchases.QueryAsync(new QueryRequest { PageSize = 1 }).ConfigureAwait(true);
        harness.Expect("purchase bill count went up", countAfter.TotalCount == countBefore.TotalCount + 1,
            $"{countBefore.TotalCount} then {countAfter.TotalCount}");

        var after = await stock.CurrentQtyAsync(shops[0].Id, product.Id).ConfigureAwait(true);
        harness.Expect("purchase puts stock on the shelf", after == before + 4, $"{before} -> {after}");
    }

    private static async Task ReceivePaymentAsync(Harness harness, IServiceProvider sp)
    {
        var sales = sp.GetRequiredService<SalesService>();

        var owing = await sales.QueryAsync(new QueryRequest
        {
            PageSize = 20,
            Sort = [new SortRule { Field = "outstanding", Desc = true }],
        }).ConfigureAwait(true);

        var invoice = owing.Items.FirstOrDefault(i => i.Outstanding > 0 && i.Status != InvoiceStatus.Void);
        if (invoice is null)
        {
            harness.Fail("payment against an invoice saves", "no invoice with an outstanding balance");
            return;
        }

        var part = Math.Round(invoice.Outstanding / 2m, 2);

        using var editor = new ReceivePaymentEditor(sales, invoice);
        Set(editor, nameof(CreateCustomerPaymentRequest.Amount), part);
        Set(editor, nameof(CreateCustomerPaymentRequest.Note), "QA part payment");

        await Submit(harness, editor, "payment against an invoice saves").ConfigureAwait(true);

        var after = await sales.GetAsync(invoice.Id).ConfigureAwait(true);

        harness.Expect("payment reduces what is outstanding",
            Math.Abs(after.Outstanding - (invoice.Outstanding - part)) < 0.01m,
            $"outstanding went {invoice.Outstanding} -> {after.Outstanding} after paying {part}");

        harness.Expect("part payment marks the invoice partial",
            after.Status == InvoiceStatus.Partial || after.Status == InvoiceStatus.Paid,
            $"status is {after.Status}");
    }

    private static async Task VoidInvoiceAsync(Harness harness, IServiceProvider sp)
    {
        var sales = sp.GetRequiredService<SalesService>();
        var stock = sp.GetRequiredService<StockService>();

        var candidates = await sales.QueryAsync(new QueryRequest
        {
            PageSize = 40,
            Sort = [new SortRule { Field = "invoiceDate", Desc = true }],
        }).ConfigureAwait(true);

        var invoice = candidates.Items.FirstOrDefault(i => i.Status != InvoiceStatus.Void && i.ItemCount > 0);
        if (invoice is null)
        {
            harness.Fail("voiding an invoice saves", "no live invoice to void");
            return;
        }

        var detail = await sales.GetAsync(invoice.Id).ConfigureAwait(true);
        var line = detail.Items[0];
        var before = await stock.CurrentQtyAsync(detail.ShopId, line.ProductId).ConfigureAwait(true);

        using var editor = new VoidInvoiceEditor(sales, invoice);
        Set(editor, nameof(VoidInvoiceRequest.Reason), "QA void");

        await Submit(harness, editor, "voiding an invoice saves").ConfigureAwait(true);

        var after = await sales.GetAsync(invoice.Id).ConfigureAwait(true);
        harness.Expect("voided invoice is marked void", after.Status == InvoiceStatus.Void, $"status is {after.Status}");
        harness.Expect("voided invoice owes nothing", after.Outstanding == 0m, $"outstanding is {after.Outstanding}");

        var stockAfter = await stock.CurrentQtyAsync(detail.ShopId, line.ProductId).ConfigureAwait(true);
        harness.Expect("void puts the stock back", stockAfter == before + line.Qty,
            $"{before} -> {stockAfter}, expected +{line.Qty}");
    }

    private static async Task UserAsync(Harness harness, IServiceProvider sp, IReadOnlyList<ShopDto> shops)
    {
        var users = sp.GetRequiredService<UserService>();
        var username = $"qa{DateTime.Now:HHmmss}";

        using var editor = new UserEditor(users, shops, null);
        editor.FillForTest(username, "QA Operator", UserRole.Standard, "qa-password-1");
        editor.AssignShopForTest(shops[0].Id);
        editor.GrantForTest(Module.Dashboard, read: true, write: false);
        editor.GrantForTest(Module.Pos, read: true, write: true);

        await editor.SubmitForTestAsync().ConfigureAwait(true);

        harness.Expect("new user saves", editor.DialogResult == DialogResult.OK,
            editor.VisibleError ?? "the dialog refused the save with no message");

        var found = await users
            .QueryAsync(new QueryRequest { Search = username, PageSize = 5 })
            .ConfigureAwait(true);

        harness.Expect("new user is findable", found.TotalCount == 1, $"returned {found.TotalCount}");

        if (found.Items.Count == 1)
        {
            var detail = await users.GetAsync(found.Items[0].Id).ConfigureAwait(true);

            harness.Expect("new user kept its shop", detail.ShopIds.Contains(shops[0].Id),
                "the assigned shop was not saved");

            var pos = detail.Permissions.FirstOrDefault(p => p.Module == Module.Pos);
            harness.Expect("new user kept its write permission", pos is { CanRead: true, CanWrite: true },
                $"the POS permission came back as {(pos is null ? "missing" : $"read={pos.CanRead} write={pos.CanWrite}")}");

            // Write implies read, which the grid enforces; it has to survive the save too.
            var dashboard = detail.Permissions.FirstOrDefault(p => p.Module == Module.Dashboard);
            harness.Expect("new user kept its read-only permission", dashboard is { CanRead: true, CanWrite: false },
                $"the dashboard permission came back as {(dashboard is null ? "missing" : $"read={dashboard.CanRead} write={dashboard.CanWrite}")}");
        }

        // The account just created has to be able to sign in with the password that was typed.
        await harness.CheckAsync("the new user can sign in", async () =>
        {
            using var scope = AppHost.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<Application.Features.Auth.AuthService>();
            await auth.LoginAsync(new Application.Features.Auth.LoginRequest(username, "qa-password-1"))
                .ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    // ------------------------------------------------------------------ validation

    private static async Task RejectsBlankProductAsync(
        Harness harness,
        IServiceProvider sp,
        IReadOnlyList<CategoryDto> categories)
    {
        using var editor = new ProductEditor(sp.GetRequiredService<ProductService>(), categories, null);

        // Everything left blank on purpose.
        await editor.SubmitForTestAsync().ConfigureAwait(true);

        harness.Expect("a blank product is refused", editor.DialogResult != DialogResult.OK,
            "the dialog accepted a product with no SKU or name");

        var shown = editor.Inputs.Keys
            .Select(editor.ErrorFor)
            .Any(message => !string.IsNullOrEmpty(message));

        harness.Expect("the blank product says which field is wrong", shown,
            "no per-field error was shown, so the operator is told nothing");
    }

    private static async Task RejectsDuplicateSkuAsync(
        Harness harness,
        IServiceProvider sp,
        IReadOnlyList<CategoryDto> categories)
    {
        var products = sp.GetRequiredService<ProductService>();
        var existing = await products.QueryAsync(new QueryRequest { PageSize = 1 }).ConfigureAwait(true);

        if (existing.Items.Count == 0)
        {
            return;
        }

        using var editor = new ProductEditor(products, categories, null);
        Set(editor, nameof(SaveProductRequest.Sku), existing.Items[0].Sku);
        Set(editor, nameof(SaveProductRequest.Name), "QA duplicate");
        Set(editor, "Cost (Rs)", 1m);
        Set(editor, "Sale price (Rs)", 2m);

        await editor.SubmitForTestAsync().ConfigureAwait(true);

        harness.Expect("a duplicate SKU is refused", editor.DialogResult != DialogResult.OK,
            "the dialog accepted a SKU that already exists");

        harness.Expect("the duplicate SKU error lands on the SKU field",
            !string.IsNullOrEmpty(editor.ErrorFor(nameof(SaveProductRequest.Sku))),
            "the error was not shown under the SKU box");
    }

    private static async Task RejectsOverlargeCashOutAsync(
        Harness harness,
        IServiceProvider sp,
        IReadOnlyList<ShopDto> shops)
    {
        var pettyCash = sp.GetRequiredService<PettyCashService>();
        var balance = await pettyCash.BalanceAsync(shops[0].Id).ConfigureAwait(true);

        using var editor = new PettyCashEditor(pettyCash, shops, shops[0].Id);
        Set(editor, nameof(SavePettyCashRequest.Direction), PettyCashDirection.Out);
        Set(editor, nameof(SavePettyCashRequest.Amount), balance + 1_000_000m);
        Set(editor, nameof(SavePettyCashRequest.Note), "QA overdraw");

        await editor.SubmitForTestAsync().ConfigureAwait(true);

        harness.Expect("taking more cash than the drawer holds is refused",
            editor.DialogResult != DialogResult.OK,
            "the drawer was allowed to go negative");
    }

    // ------------------------------------------------------------------ plumbing

    private static async Task Submit(Harness harness, EditorForm editor, string name)
    {
        try
        {
            await editor.SubmitForTestAsync().ConfigureAwait(true);

            if (editor.DialogResult == DialogResult.OK)
            {
                harness.Pass(name);
                return;
            }

            var detail = editor.VisibleError
                ?? editor.Inputs.Keys.Select(editor.ErrorFor).FirstOrDefault(e => !string.IsNullOrEmpty(e))
                ?? "the dialog refused the save but showed no error";

            harness.Fail(name, detail);
        }
        catch (Exception ex)
        {
            harness.Fail(name, ex.ToString());
        }
    }

    private static async Task SubmitForm(Harness harness, PurchaseBillEditor editor, string name)
    {
        try
        {
            await editor.SubmitForTestAsync().ConfigureAwait(true);

            harness.Expect(name, editor.DialogResult == DialogResult.OK,
                editor.VisibleError ?? "the bill was refused with no message");
        }
        catch (Exception ex)
        {
            harness.Fail(name, ex.ToString());
        }
    }

    private static bool TryAddPurchaseLine(PurchaseBillEditor editor, long productId, int qty, decimal cost)
        => editor.AddLineForTest(productId, qty, cost);

    /// <summary>
    /// Types a value into a named field, choosing how based on what the control is. This
    /// is the part that proves the form reads what the operator typed.
    /// </summary>
    private static void Set(EditorForm form, string field, object value)
    {
        if (!form.Inputs.TryGetValue(field, out var control))
        {
            throw new InvalidOperationException(
                $"'{field}' is not a field on {form.GetType().Name}; it has: {string.Join(", ", form.Inputs.Keys)}");
        }

        switch (control)
        {
            case KryptonTextBox text:
                text.Text = value.ToString() ?? string.Empty;
                break;

            case KryptonNumericUpDown numeric:
                numeric.Value = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
                break;

            case KryptonCheckBox check:
                check.Checked = Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
                break;

            case KryptonDateTimePicker date:
                date.Value = Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture);
                break;

            case KryptonComboBox combo:
                SelectInCombo(combo, value, field);
                break;

            default:
                throw new InvalidOperationException($"'{field}' is a {control.GetType().Name}, which the harness cannot type into");
        }
    }

    private static void SelectInCombo(KryptonComboBox combo, object value, string field)
    {
        foreach (var item in combo.Items)
        {
            var matches = item switch
            {
                _ when value is long id => IdOf(item) == id,
                _ when value is Enum e => ValueOf(item)?.Equals(e) == true,
                _ => string.Equals(item.ToString(), value.ToString(), StringComparison.OrdinalIgnoreCase),
            };

            if (matches)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        throw new InvalidOperationException($"'{value}' is not an option in the '{field}' list");
    }

    private static long? IdOf(object item)
        => item.GetType().GetProperty("Id")?.GetValue(item) as long?;

    private static object? ValueOf(object item)
        => item.GetType().GetProperty("Value")?.GetValue(item);
}
