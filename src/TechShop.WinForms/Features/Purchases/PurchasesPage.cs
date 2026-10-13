using System.Drawing;
using Krypton.Toolkit;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Parties;
using TechShop.Application.Features.Purchases;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Purchases;

/// <summary>
/// Supplier bills. Entering one puts the stock on the shelf and anything unpaid onto the
/// supplier's account, in a single transaction — so the shelf and the books cannot
/// disagree about a delivery.
/// </summary>
public sealed class PurchasesPage : ListPage<PurchaseBillListDto>
{
    private bool _actionsAdded;

    public PurchasesPage()
        : base(Module.Purchases, "Purchases", withDateRange: true)
    {
    }

    protected override string DefaultSortField => "billDate";

    protected override string ItemNoun => "bill";

    protected override bool SupportsCreate => true;

    protected override bool SupportsEdit => true;

    protected override Func<QueryRequest, Task<byte[]>>? Exporter
        => request => Service<PurchaseService>().ExportAsync(request);

    protected override IReadOnlyList<GridColumn<PurchaseBillListDto>> Columns() =>
    [
        new("billNo", "Bill", b => b.BillNo) { Width = 170 },

        new("billDate", "Date", b => b.BillDate)
        {
            Width = 120,
            Format = b => Format.Date(b.BillDate),
        },

        new("shopName", "Shop", b => b.ShopName) { Width = 160 },
        new("supplierName", "Supplier", b => b.SupplierName) { Fill = true },

        new("itemCount", "Lines", b => b.ItemCount)
        {
            Width = 70,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = b => Format.Qty(b.ItemCount),
        },

        new("total", Format.MoneyHeader("Total"), b => b.Total)
        {
            Width = 130,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = b => Format.Amount(b.Total),
        },

        new("paidAmount", Format.MoneyHeader("Paid"), b => b.PaidAmount)
        {
            Width = 130,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = b => Format.Amount(b.PaidAmount),
        },

        new("outstanding", Format.MoneyHeader("Owed"), b => b.Outstanding)
        {
            Width = 130,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = b => b.Outstanding == 0 ? string.Empty : Format.Amount(b.Outstanding),
            Foreground = _ => Palette.WarningText,
        },

        new("createdBy", "By", b => b.CreatedBy) { Width = 100 },
    ];

    protected override Task<PagedResult<PurchaseBillListDto>> QueryAsync(QueryRequest request)
        => Service<PurchaseService>().QueryAsync(request);

    protected override Task OnEditAsync(PurchaseBillListDto row) => OpenAsync(row);

    protected override async Task OnNewAsync()
    {
        var shops = await Service<Application.Features.Shops.ShopService>().LookupAsync().ConfigureAwait(true);
        var suppliers = await Service<SupplierService>().LookupAsync().ConfigureAwait(true);

        if (shops.Count == 0)
        {
            Dialogs.Warn(FindForm(), "No shop", "This account is not assigned to a shop.");
            return;
        }

        if (suppliers.Count == 0)
        {
            Dialogs.Warn(FindForm(), "No suppliers", "Add a supplier before entering a bill.");
            return;
        }

        var products = await Service<Application.Features.Catalog.ProductService>()
            .QueryAsync(new QueryRequest { PageSize = 500, Sort = [new SortRule { Field = "name" }] })
            .ConfigureAwait(true);

        using var editor = new PurchaseBillEditor(
            Service<PurchaseService>(),
            shops,
            suppliers,
            products.Items,
            ActiveShopId);

        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "Bill entered, stock received");
        }
    }

    protected override Task AfterLoadAsync()
    {
        if (_actionsAdded)
        {
            return Task.CompletedTask;
        }

        if (CanWrite)
        {
            var pay = new AppButton { Variant = ButtonVariant.Success, Text = "Pay supplier" };
            pay.FitToText(Metrics.Space3);
            pay.Click += async (_, _) => await PayAsync().ConfigureAwait(true);
            Grid.AddAction(pay);
        }

        var open = new AppButton { Variant = ButtonVariant.Secondary, Text = "Open" };
        open.FitToText(Metrics.Space3);
        open.Click += async (_, _) =>
        {
            if (Grid.Selected is { } row)
            {
                await OpenAsync(row).ConfigureAwait(true);
            }
            else
            {
                Dialogs.Info(FindForm(), "Nothing selected", "Choose a bill to open.");
            }
        };
        Grid.AddAction(open);

        _actionsAdded = true;
        return Task.CompletedTask;
    }

    private async Task OpenAsync(PurchaseBillListDto row)
        => await TryAsync(async () =>
        {
            var detail = await Service<PurchaseService>().GetAsync(row.Id).ConfigureAwait(true);
            using var dialog = new PurchaseBillDialog(detail);
            dialog.ShowDialog(FindForm());
        }).ConfigureAwait(true);

    private async Task PayAsync()
    {
        if (Grid.Selected is not { } row)
        {
            Dialogs.Info(FindForm(), "Nothing selected", "Choose a bill to pay against.");
            return;
        }

        if (row.Outstanding <= 0)
        {
            Dialogs.Info(FindForm(), "Nothing owed", $"{row.BillNo} is already settled.");
            return;
        }

        using var editor = new SupplierPaymentEditor(Service<PurchaseService>(), row);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "Payment recorded");
            await Grid.ReloadAsync().ConfigureAwait(true);
        }
    }
}

/// <summary>Pays a supplier against one bill.</summary>
public sealed class SupplierPaymentEditor : EditorForm
{
    private readonly PurchaseService _purchases;
    private readonly PurchaseBillListDto _bill;
    private readonly KryptonDateTimePicker _date;
    private readonly KryptonNumericUpDown _amount;
    private readonly KryptonComboBox _method;
    private readonly KryptonTextBox _note;

    public SupplierPaymentEditor(PurchaseService purchases, PurchaseBillListDto bill)
        : base("Pay supplier", $"{bill.BillNo} · {bill.SupplierName}")
    {
        _purchases = purchases;
        _bill = bill;

        var owed = Theme.TextBox();
        owed.Text = Format.Money(bill.Outstanding);
        owed.ReadOnly = true;
        Field("Outstanding", owed);

        _date = Field("Paid on", Theme.DatePicker(), name: nameof(CreateSupplierPaymentRequest.PaidAt));
        _date.Value = DateTime.Today;

        _amount = Field(
            Format.MoneyHeader("Amount"),
            Theme.Numeric(),
            "Cannot be more than the amount owed on this bill.",
            nameof(CreateSupplierPaymentRequest.Amount));
        _amount.Maximum = 9_999_999_999m;
        _amount.Value = bill.Outstanding;

        _method = Field("Method", Pickers.PaymentMethod(), name: nameof(CreateSupplierPaymentRequest.Method));
        _note = Field("Note", Theme.TextBox("Optional"), name: nameof(CreateSupplierPaymentRequest.Note));

        FinishLayout();
    }

    protected override async Task SaveAsync()
        => await _purchases.PayAsync(new CreateSupplierPaymentRequest(
            _bill.ShopId,
            _bill.SupplierId,
            _bill.Id,
            Format.ToUtc(_date.Value.Date),
            _amount.Value,
            Pickers.Selected(_method, PaymentMethod.Bank),
            _note.Text)).ConfigureAwait(true);
}
