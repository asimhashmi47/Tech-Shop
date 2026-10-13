using Krypton.Toolkit;
using TechShop.Application.Features.Money;
using TechShop.Application.Features.Shops;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Money;

public sealed class ExpenseEditor : EditorForm
{
    private readonly ExpenseService _expenses;
    private readonly long? _id;
    private readonly KryptonComboBox _shop;
    private readonly KryptonComboBox _category;
    private readonly KryptonDateTimePicker _date;
    private readonly KryptonNumericUpDown _amount;
    private readonly KryptonComboBox _method;
    private readonly KryptonTextBox _note;

    public ExpenseEditor(
        ExpenseService expenses,
        IReadOnlyList<ShopDto> shops,
        IReadOnlyList<ExpenseCategoryDto> categories,
        ExpenseDto? existing)
        : base(
            existing is null ? "New expense" : "Edit expense",
            existing is null ? "Money going out that is not a supplier bill." : existing.CategoryName)
    {
        _expenses = expenses;
        _id = existing?.Id;

        _shop = Field(
            "Shop",
            Lookup(shops, s => s.Id, s => s.Name, existing?.ShopId),
            name: nameof(SaveExpenseRequest.ShopId));

        _category = Field(
            "Category",
            Lookup(categories, c => c.Id, c => c.Name, existing?.ExpenseCategoryId),
            name: nameof(SaveExpenseRequest.ExpenseCategoryId));

        _date = Field("Date", Theme.DatePicker(), name: nameof(SaveExpenseRequest.ExpenseDate));

        _amount = Field(
            Format.MoneyHeader("Amount"),
            Theme.Numeric(),
            name: nameof(SaveExpenseRequest.Amount));

        _method = Field(
            "Paid by",
            Pickers.PaymentMethod(existing?.Method),
            "Cash comes out of the drawer and shows on the petty cash screen.",
            nameof(SaveExpenseRequest.Method));

        _note = Field("Note", Theme.TextBox("Optional"), name: nameof(SaveExpenseRequest.Note));

        if (existing is not null)
        {
            _date.Value = Format.ToLocal(existing.ExpenseDate);
            _amount.Value = existing.Amount;
            _note.Text = existing.Note ?? string.Empty;
        }
        else
        {
            _date.Value = DateTime.Today;
        }

        FinishLayout();
    }

    protected override async Task SaveAsync()
    {
        if (SelectedId(_shop) is not { } shopId)
        {
            ShowError("This account is not assigned to a shop.");
            return;
        }

        if (SelectedId(_category) is not { } categoryId)
        {
            ShowError("Add an expense category first.");
            return;
        }

        var request = new SaveExpenseRequest(
            shopId,
            categoryId,
            Format.ToUtc(_date.Value.Date),
            _amount.Value,
            Pickers.Selected(_method, PaymentMethod.Cash),
            _note.Text);

        if (_id is { } id)
        {
            await _expenses.UpdateAsync(id, request).ConfigureAwait(true);
        }
        else
        {
            await _expenses.CreateAsync(request).ConfigureAwait(true);
        }
    }
}
