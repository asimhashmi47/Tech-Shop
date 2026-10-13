using System.Drawing;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Money;
using TechShop.Application.Features.Shops;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Money;

/// <summary>Money out that is not a supplier bill: rent, wages, utilities, repairs.</summary>
public sealed class ExpensesPage : ListPage<ExpenseDto>
{
    public ExpensesPage()
        : base(Module.Expenses, "Expenses", withDateRange: true)
    {
    }

    protected override string DefaultSortField => "expenseDate";

    protected override string ItemNoun => "expense";

    protected override bool SupportsCreate => true;

    protected override bool SupportsEdit => true;

    protected override bool SupportsDelete => true;

    protected override Func<QueryRequest, Task<byte[]>>? Exporter
        => request => Service<ExpenseService>().ExportAsync(request);

    protected override IReadOnlyList<GridColumn<ExpenseDto>> Columns() =>
    [
        new("expenseDate", "Date", e => e.ExpenseDate)
        {
            Width = 110,
            Format = e => Format.Date(e.ExpenseDate),
        },

        new("shopName", "Shop", e => e.ShopName) { Width = 170 },
        new("categoryName", "Category", e => e.CategoryName) { Width = 140 },
        new("note", "Note", e => e.Note) { Fill = true },

        new("amount", Format.MoneyHeader("Amount"), e => e.Amount)
        {
            Width = 130,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = e => Format.Amount(e.Amount),
        },

        new("method", "Paid by", e => e.Method)
        {
            Width = 90,
            Chip = e =>
            {
                var (fill, ink) = Badge.ToneFor(e.Method);
                return (e.Method.ToString(), fill, ink);
            },
        },

        new("createdBy", "Entered by", e => e.CreatedBy) { Width = 110 },
    ];

    protected override Task<PagedResult<ExpenseDto>> QueryAsync(QueryRequest request)
        => Service<ExpenseService>().QueryAsync(request);

    protected override string Describe(ExpenseDto row)
        => $"the {row.CategoryName} expense of {Format.Money(row.Amount)} on {Format.Date(row.ExpenseDate)}";

    protected override async Task OnNewAsync()
    {
        var (shops, categories) = await LookupsAsync().ConfigureAwait(true);
        if (shops.Count == 0 || categories.Count == 0)
        {
            return;
        }

        using var editor = new ExpenseEditor(Service<ExpenseService>(), shops, categories, null);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "Expense recorded");
        }
    }

    protected override async Task OnEditAsync(ExpenseDto row)
    {
        var (shops, categories) = await LookupsAsync().ConfigureAwait(true);
        if (shops.Count == 0 || categories.Count == 0)
        {
            return;
        }

        using var editor = new ExpenseEditor(Service<ExpenseService>(), shops, categories, row);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "Expense saved");
        }
    }

    protected override Task OnDeleteAsync(ExpenseDto row)
        => Service<ExpenseService>().DeleteAsync(row.Id);

    private async Task<(IReadOnlyList<ShopDto> Shops, IReadOnlyList<ExpenseCategoryDto> Categories)> LookupsAsync()
    {
        var shops = await Service<ShopService>().LookupAsync().ConfigureAwait(true);
        var categories = await Service<ExpenseService>().CategoriesAsync().ConfigureAwait(true);

        if (shops.Count == 0)
        {
            Dialogs.Warn(FindForm(), "No shop", "This account is not assigned to a shop.");
        }
        else if (categories.Count == 0)
        {
            Dialogs.Warn(FindForm(), "No categories", "Add an expense category first.");
        }

        return (shops, categories);
    }
}
