using System.Drawing;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Ledgers;
using TechShop.Application.Features.Parties;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Parties;

/// <summary>Who the shop buys from, and what it still owes each of them.</summary>
public sealed class SuppliersPage : ListPage<SupplierDto>
{
    private bool _ledgerAdded;

    public SuppliersPage()
        : base(Module.Suppliers, "Suppliers", "Accounts and balances")
    {
    }

    protected override string DefaultSortField => "payable";

    protected override string ItemNoun => "supplier";

    protected override bool SupportsCreate => true;

    protected override bool SupportsEdit => true;

    protected override bool SupportsDelete => true;

    protected override Func<QueryRequest, Task<byte[]>>? Exporter
        => request => Service<SupplierService>().ExportAsync(request);

    protected override IReadOnlyList<GridColumn<SupplierDto>> Columns() =>
    [
        new("name", "Supplier", s => s.Name) { Fill = true },
        new("phone", "Phone", s => s.Phone) { Width = 130 },
        new("address", "Address", s => s.Address) { Width = 220 },

        new("payable", Format.MoneyHeader("Owed"), s => s.Payable)
        {
            Width = 130,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = s => Format.Amount(s.Payable),
            Foreground = s => s.Payable > 0 ? Palette.WarningText : Palette.InkMuted,
        },

        new("isActive", "Status", s => s.IsActive)
        {
            Width = 90,
            Chip = s => s.IsActive
                ? ("Active", Palette.EmeraldSoft, Palette.EmeraldText)
                : ("Inactive", Palette.SlateSoft, Palette.SlateText),
        },
    ];

    protected override Task<PagedResult<SupplierDto>> QueryAsync(QueryRequest request)
        => Service<SupplierService>().QueryAsync(request);

    protected override string Describe(SupplierDto row) => row.Name;

    protected override Task OnNewAsync()
    {
        using var editor = new SupplierEditor(Service<SupplierService>(), null);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "Supplier added");
        }

        return Task.CompletedTask;
    }

    protected override Task OnEditAsync(SupplierDto row)
    {
        using var editor = new SupplierEditor(Service<SupplierService>(), row);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), $"{row.Name} saved");
        }

        return Task.CompletedTask;
    }

    protected override Task OnDeleteAsync(SupplierDto row)
        => Service<SupplierService>().DeleteAsync(row.Id);

    protected override Task AfterLoadAsync()
    {
        if (!_ledgerAdded)
        {
            var ledger = new AppButton { Variant = ButtonVariant.Secondary, Text = "Ledger" };
            ledger.FitToText(Metrics.Space3);
            ledger.Click += async (_, _) => await ShowLedgerAsync().ConfigureAwait(true);
            Grid.AddAction(ledger);
            _ledgerAdded = true;
        }

        return Task.CompletedTask;
    }

    private async Task ShowLedgerAsync()
    {
        if (Grid.Selected is not { } supplier)
        {
            Dialogs.Info(FindForm(), "Nothing selected", "Choose a supplier to see their ledger.");
            return;
        }

        await TryAsync(async () =>
        {
            var ledger = await Service<LedgerService>()
                .SupplierLedgerAsync(supplier.Id, ActiveShopId, null, null)
                .ConfigureAwait(true);

            using var view = new LedgerDialog($"{supplier.Name} ledger", ledger);
            view.ShowDialog(FindForm());
        }).ConfigureAwait(true);
    }
}
