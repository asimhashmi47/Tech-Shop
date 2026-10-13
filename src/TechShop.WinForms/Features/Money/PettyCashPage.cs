using System.Drawing;
using Krypton.Toolkit;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Money;
using TechShop.Application.Features.Shops;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Money;

/// <summary>
/// The cash drawer.
///
/// Most rows here are mirrors of money that moved somewhere else — a cash sale, a cash
/// receipt, a cash expense — and those are marked as such and cannot be edited, because
/// the document that caused them is the record. Only the entries someone typed directly
/// (a float, cash banked, a correction) can be removed.
/// </summary>
public sealed class PettyCashPage : ListPage<PettyCashEntryDto>
{
    public PettyCashPage()
        : base(Module.PettyCash, "Petty cash", withDateRange: true)
    {
    }

    protected override string DefaultSortField => "entryDate";

    protected override string ItemNoun => "entry";

    protected override bool SupportsCreate => true;

    protected override bool SupportsDelete => true;

    protected override Func<QueryRequest, Task<byte[]>>? Exporter
        => request => Service<PettyCashService>().ExportAsync(request);

    protected override IReadOnlyList<GridColumn<PettyCashEntryDto>> Columns() =>
    [
        new("entryDate", "Date", e => e.EntryDate)
        {
            Width = 120,
            Format = e => Format.Date(e.EntryDate),
        },

        new("shopName", "Shop", e => e.ShopName) { Width = 170 },
        new("note", "Detail", e => e.Note) { Fill = true },

        new("cashIn", Format.MoneyHeader("In"), e => e.CashIn)
        {
            Width = 120,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = e => e.CashIn == 0 ? string.Empty : Format.Amount(e.CashIn),
            Foreground = _ => Palette.EmeraldText,
        },

        new("cashOut", Format.MoneyHeader("Out"), e => e.CashOut)
        {
            Width = 120,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = e => e.CashOut == 0 ? string.Empty : Format.Amount(e.CashOut),
            Foreground = _ => Palette.DangerText,
        },

        new("fromExpense", "Source", e => e.FromExpense)
        {
            Width = 110,
            Sortable = false,
            Chip = e => e.FromExpense
                ? ("Automatic", Palette.SlateSoft, Palette.SlateText)
                : ("Entered", Palette.IndigoSoft, Palette.IndigoText),
        },

        new("createdBy", "By", e => e.CreatedBy) { Width = 100 },
    ];

    protected override Task<PagedResult<PettyCashEntryDto>> QueryAsync(QueryRequest request)
        => Service<PettyCashService>().QueryAsync(request);

    protected override string Describe(PettyCashEntryDto row)
        => row.CashIn > 0
            ? $"the {Format.Money(row.CashIn)} cash in on {Format.Date(row.EntryDate)}"
            : $"the {Format.Money(row.CashOut)} cash out on {Format.Date(row.EntryDate)}";

    protected override async Task OnNewAsync()
    {
        var shops = await Service<ShopService>().LookupAsync().ConfigureAwait(true);
        if (shops.Count == 0)
        {
            Dialogs.Warn(FindForm(), "No shop", "This account is not assigned to a shop.");
            return;
        }

        using var editor = new PettyCashEditor(Service<PettyCashService>(), shops, ActiveShopId);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "Drawer updated");
        }
    }

    protected override Task OnDeleteAsync(PettyCashEntryDto row)
    {
        if (row.FromExpense)
        {
            // Removing the mirror would leave the drawer disagreeing with the document
            // that produced it.
            Dialogs.Warn(
                FindForm(),
                "Cannot remove",
                "This entry mirrors a sale, receipt or expense. Remove that document instead and this row will follow.");

            return Task.CompletedTask;
        }

        return Service<PettyCashService>().DeleteAsync(row.Id);
    }
}

public sealed class PettyCashEditor : EditorForm
{
    private readonly PettyCashService _pettyCash;
    private readonly KryptonComboBox _shop;
    private readonly KryptonDateTimePicker _date;
    private readonly KryptonComboBox _direction;
    private readonly KryptonNumericUpDown _amount;
    private readonly KryptonTextBox _note;

    public PettyCashEditor(PettyCashService pettyCash, IReadOnlyList<ShopDto> shops, long? activeShopId)
        : base("Drawer entry", "Cash in or out that is not already recorded elsewhere.")
    {
        _pettyCash = pettyCash;

        _shop = Field(
            "Shop",
            Lookup(shops, s => s.Id, s => s.Name, activeShopId),
            name: nameof(SavePettyCashRequest.ShopId));

        _date = Field("Date", Theme.DatePicker(), name: nameof(SavePettyCashRequest.EntryDate));
        _date.Value = DateTime.Today;

        _direction = Field(
            "Direction",
            Pickers.Direction(),
            name: nameof(SavePettyCashRequest.Direction));

        _amount = Field(
            Format.MoneyHeader("Amount"),
            Theme.Numeric(),
            "Cash out cannot be more than the drawer holds.",
            nameof(SavePettyCashRequest.Amount));

        _note = Field(
            "Note",
            Theme.TextBox("Cash banked, float, correction…"),
            name: nameof(SavePettyCashRequest.Note));

        FinishLayout();
    }

    protected override async Task SaveAsync()
    {
        if (SelectedId(_shop) is not { } shopId)
        {
            ShowError("Choose a shop.");
            return;
        }

        await _pettyCash.CreateAsync(new SavePettyCashRequest(
            shopId,
            Format.ToUtc(_date.Value.Date),
            Pickers.Selected(_direction, PettyCashDirection.Out),
            _amount.Value,
            _note.Text)).ConfigureAwait(true);
    }
}
