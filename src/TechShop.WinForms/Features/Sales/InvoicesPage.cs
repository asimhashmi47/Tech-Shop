using System.Drawing;
using Krypton.Toolkit;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Sales;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Sales;

/// <summary>
/// Every sale raised. Open one to see it as it prints, take a payment against it, or void
/// it.
///
/// An invoice is never edited or deleted. Voiding reverses the stock and leaves the
/// document in place with a reason on it, because the shop's numbering has to stay
/// continuous for the books to be auditable.
/// </summary>
public sealed class InvoicesPage : ListPage<InvoiceListDto>
{
    private bool _actionsAdded;

    public InvoicesPage()
        : base(Module.Invoices, "Invoices", withDateRange: true)
    {
    }

    protected override string DefaultSortField => "invoiceDate";

    protected override string ItemNoun => "invoice";

    protected override bool SupportsEdit => true;

    protected override Func<QueryRequest, Task<byte[]>>? Exporter
        => request => Service<SalesService>().ExportAsync(request);

    protected override IReadOnlyList<GridColumn<InvoiceListDto>> Columns() =>
    [
        new("invoiceNo", "Invoice", i => i.InvoiceNo) { Width = 160 },

        new("invoiceDate", "Date", i => i.InvoiceDate)
        {
            Width = 120,
            Format = i => Format.Date(i.InvoiceDate),
        },

        new("shopName", "Shop", i => i.ShopName) { Width = 160 },
        new("customerName", "Customer", i => i.CustomerName) { Fill = true },

        new("itemCount", "Lines", i => i.ItemCount)
        {
            Width = 70,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = i => Format.Qty(i.ItemCount),
        },

        new("taxTotal", Format.MoneyHeader("Tax"), i => i.TaxTotal)
        {
            Width = 110,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = i => i.TaxTotal == 0m ? string.Empty : Format.Amount(i.TaxTotal),
        },

        new("total", Format.MoneyHeader("Total"), i => i.Total)
        {
            Width = 130,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = i => Format.Amount(i.Total),
        },

        new("outstanding", Format.MoneyHeader("Due"), i => i.Outstanding)
        {
            Width = 120,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = i => i.Outstanding == 0 ? string.Empty : Format.Amount(i.Outstanding),
            Foreground = _ => Palette.DangerText,
        },

        new("status", "Status", i => i.Status)
        {
            Width = 90,
            Chip = i =>
            {
                var (fill, ink) = Badge.ToneFor(i.Status);
                return (i.Status.ToString(), fill, ink);
            },
        },

        new("createdBy", "By", i => i.CreatedBy) { Width = 100 },
    ];

    protected override Task<PagedResult<InvoiceListDto>> QueryAsync(QueryRequest request)
        => Service<SalesService>().QueryAsync(request);

    /// <summary>Opening a row shows the invoice rather than an edit form.</summary>
    protected override Task OnEditAsync(InvoiceListDto row) => OpenAsync(row);

    protected override Task AfterLoadAsync()
    {
        if (_actionsAdded)
        {
            return Task.CompletedTask;
        }

        if (CanWrite)
        {
            var void_ = new AppButton { Variant = ButtonVariant.Danger, Text = "Void" };
            void_.FitToText(Metrics.Space3);
            void_.Click += async (_, _) => await VoidAsync().ConfigureAwait(true);
            Grid.AddAction(void_);

            var receive = new AppButton { Variant = ButtonVariant.Success, Text = "Take payment" };
            receive.FitToText(Metrics.Space3);
            receive.Click += async (_, _) => await ReceiveAsync().ConfigureAwait(true);
            Grid.AddAction(receive);
        }

        var open = new AppButton { Variant = ButtonVariant.Primary, Text = "Open" };
        open.FitToText(Metrics.Space3);
        open.Click += async (_, _) => await OpenSelectedAsync().ConfigureAwait(true);
        Grid.AddAction(open);

        _actionsAdded = true;
        return Task.CompletedTask;
    }

    private async Task OpenSelectedAsync()
    {
        if (Grid.Selected is not { } row)
        {
            Dialogs.Info(FindForm(), "Nothing selected", "Choose an invoice to open.");
            return;
        }

        await OpenAsync(row).ConfigureAwait(true);
    }

    private async Task OpenAsync(InvoiceListDto row)
        => await TryAsync(async () =>
        {
            var detail = await Service<SalesService>().GetAsync(row.Id).ConfigureAwait(true);
            using var dialog = new InvoiceDetailDialog(detail);
            dialog.ShowDialog(FindForm());
        }).ConfigureAwait(true);

    private async Task VoidAsync()
    {
        if (Grid.Selected is not { } row)
        {
            Dialogs.Info(FindForm(), "Nothing selected", "Choose an invoice to void.");
            return;
        }

        if (row.Status == InvoiceStatus.Void)
        {
            Dialogs.Info(FindForm(), "Already void", $"{row.InvoiceNo} has already been voided.");
            return;
        }

        using var editor = new VoidInvoiceEditor(Service<SalesService>(), row);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), $"{row.InvoiceNo} voided, stock returned", ToastTone.Warning);
            await Grid.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task ReceiveAsync()
    {
        if (Grid.Selected is not { } row)
        {
            Dialogs.Info(FindForm(), "Nothing selected", "Choose an invoice to take a payment against.");
            return;
        }

        if (row.Outstanding <= 0)
        {
            Dialogs.Info(
                FindForm(),
                "Nothing due",
                row.Status == InvoiceStatus.Void
                    ? $"{row.InvoiceNo} is void, so nothing is owed on it."
                    : $"{row.InvoiceNo} is already paid in full.");
            return;
        }

        using var editor = new ReceivePaymentEditor(Service<SalesService>(), row);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "Payment received");
            await Grid.ReloadAsync().ConfigureAwait(true);
        }
    }
}

/// <summary>
/// Voiding asks for a reason and will not proceed without one. A void with no reason is
/// indistinguishable from a mistake six months later.
/// </summary>
public sealed class VoidInvoiceEditor : EditorForm
{
    private readonly SalesService _sales;
    private readonly InvoiceListDto _invoice;
    private readonly KryptonTextBox _reason;

    public VoidInvoiceEditor(SalesService sales, InvoiceListDto invoice)
        : base("Void invoice", $"{invoice.InvoiceNo} · {invoice.CustomerName} · {Format.Money(invoice.Total)}")
    {
        _sales = sales;
        _invoice = invoice;

        _reason = Field(
            "Reason",
            Theme.TextBox("Why is this being voided?"),
            "The stock on this invoice goes back on the shelf and the document stays, marked void.",
            nameof(VoidInvoiceRequest.Reason));

        FinishLayout();
    }

    protected override async Task SaveAsync()
        => await _sales.VoidAsync(_invoice.Id, new VoidInvoiceRequest(_reason.Text)).ConfigureAwait(true);
}

/// <summary>Takes money against one invoice.</summary>
public sealed class ReceivePaymentEditor : EditorForm
{
    private readonly SalesService _sales;
    private readonly InvoiceListDto _invoice;
    private readonly KryptonDateTimePicker _date;
    private readonly KryptonNumericUpDown _amount;
    private readonly KryptonComboBox _method;
    private readonly KryptonTextBox _note;

    public ReceivePaymentEditor(SalesService sales, InvoiceListDto invoice)
        : base("Take payment", $"{invoice.InvoiceNo} · {invoice.CustomerName}")
    {
        _sales = sales;
        _invoice = invoice;

        var due = Theme.TextBox();
        due.Text = Format.Money(invoice.Outstanding);
        due.ReadOnly = true;
        Field("Outstanding", due);

        _date = Field("Received", Theme.DatePicker(), name: nameof(CreateCustomerPaymentRequest.ReceivedAt));
        _date.Value = DateTime.Today;

        _amount = Field(
            Format.MoneyHeader("Amount"),
            Theme.Numeric(),
            "Cannot be more than the amount outstanding.",
            nameof(CreateCustomerPaymentRequest.Amount));

        // Prefilled with the full amount due, which is what most payments are.
        _amount.Maximum = 9_999_999_999m;
        _amount.Value = invoice.Outstanding;

        _method = Field(
            "Method",
            Pickers.PaymentMethod(),
            name: nameof(CreateCustomerPaymentRequest.Method));

        _note = Field("Note", Theme.TextBox("Optional"), name: nameof(CreateCustomerPaymentRequest.Note));

        FinishLayout();
    }

    protected override async Task SaveAsync()
        => await _sales.ReceiveAsync(new CreateCustomerPaymentRequest(
            _invoice.ShopId,
            _invoice.CustomerId,
            _invoice.Id,
            Format.ToUtc(_date.Value.Date),
            _amount.Value,
            Pickers.Selected(_method, PaymentMethod.Cash),
            _note.Text)).ConfigureAwait(true);
}
