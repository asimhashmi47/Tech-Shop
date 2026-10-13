using System.Drawing;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Ledgers;
using TechShop.Application.Features.Parties;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Parties;

/// <summary>
/// Who the shop sells to, and what each of them still owes. The balance column is the
/// reason this screen exists, so it is the one that gets the colour.
/// </summary>
public sealed class CustomersPage : ListPage<CustomerDto>
{
    public CustomersPage()
        : base(Module.Customers, "Customers", "Accounts and balances")
    {
    }

    protected override string DefaultSortField => "receivable";

    protected override string ItemNoun => "customer";

    protected override bool SupportsCreate => true;

    protected override bool SupportsEdit => true;

    protected override bool SupportsDelete => true;

    protected override Func<QueryRequest, Task<byte[]>>? Exporter
        => request => Service<CustomerService>().ExportAsync(request);

    protected override IReadOnlyList<GridColumn<CustomerDto>> Columns() =>
    [
        new("name", "Customer", c => c.Name) { Fill = true },
        new("phone", "Phone", c => c.Phone) { Width = 130 },
        new("address", "Address", c => c.Address) { Width = 200 },

        new("receivable", Format.MoneyHeader("Owes"), c => c.Receivable)
        {
            Width = 130,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = c => Format.Amount(c.Receivable),
            Foreground = c => c.Receivable > 0 ? Palette.DangerText : Palette.InkMuted,
        },

        new("isWalkIn", "Type", c => c.IsWalkIn)
        {
            Width = 90,
            Chip = c => c.IsWalkIn
                ? ("Walk-in", Palette.IndigoSoft, Palette.IndigoText)
                : ("Account", Palette.SlateSoft, Palette.SlateText),
        },

        new("isActive", "Status", c => c.IsActive)
        {
            Width = 90,
            Chip = c => c.IsActive
                ? ("Active", Palette.EmeraldSoft, Palette.EmeraldText)
                : ("Inactive", Palette.SlateSoft, Palette.SlateText),
        },
    ];

    protected override Task<PagedResult<CustomerDto>> QueryAsync(QueryRequest request)
        => Service<CustomerService>().QueryAsync(request);

    protected override string Describe(CustomerDto row) => row.Name;

    protected override void ScopeRequest(QueryRequest request)
    {
        // A ledger is per shop, but a customer is not: the balance shown is what they owe
        // across every shop the operator can see.
    }

    protected override Task OnNewAsync()
    {
        using var editor = new CustomerEditor(Service<CustomerService>(), null);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "Customer added");
        }

        return Task.CompletedTask;
    }

    protected override Task OnEditAsync(CustomerDto row)
    {
        using var editor = new CustomerEditor(Service<CustomerService>(), row);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), $"{row.Name} saved");
        }

        return Task.CompletedTask;
    }

    protected override Task OnDeleteAsync(CustomerDto row)
        => Service<CustomerService>().DeleteAsync(row.Id);

    protected override Task AfterLoadAsync()
    {
        if (!_ledgerAdded)
        {
            // Added after the first load so it sits beside the actions the base built.
            var ledger = new AppButton { Variant = ButtonVariant.Secondary, Text = "Ledger" };
            ledger.FitToText(Metrics.Space3);
            ledger.Click += async (_, _) => await ShowLedgerAsync().ConfigureAwait(true);
            Grid.AddAction(ledger);
            _ledgerAdded = true;
        }

        return Task.CompletedTask;
    }

    private bool _ledgerAdded;

    private async Task ShowLedgerAsync()
    {
        if (Grid.Selected is not { } customer)
        {
            Dialogs.Info(FindForm(), "Nothing selected", "Choose a customer to see their ledger.");
            return;
        }

        await TryAsync(async () =>
        {
            var ledger = await Service<LedgerService>()
                .CustomerLedgerAsync(customer.Id, ActiveShopId, null, null)
                .ConfigureAwait(true);

            using var view = new LedgerDialog($"{customer.Name} ledger", ledger);
            view.ShowDialog(FindForm());
        }).ConfigureAwait(true);
    }
}
