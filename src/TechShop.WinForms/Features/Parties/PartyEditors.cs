using Krypton.Toolkit;
using TechShop.Application.Features.Parties;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Parties;

public sealed class CustomerEditor : EditorForm
{
    private readonly CustomerService _customers;
    private readonly long? _id;
    private readonly KryptonTextBox _name;
    private readonly KryptonTextBox _phone;
    private readonly KryptonTextBox _address;
    private readonly KryptonCheckBox _active;

    public CustomerEditor(CustomerService customers, CustomerDto? existing)
        : base(
            existing is null ? "New customer" : "Edit customer",
            existing is null ? "Someone who buys on account or by name." : existing.Name)
    {
        _customers = customers;
        _id = existing?.Id;

        _name = Field("Name", Theme.TextBox(), name: nameof(SaveCustomerRequest.Name));
        _phone = Field("Phone", Theme.TextBox("Optional"), name: nameof(SaveCustomerRequest.Phone));
        _address = Field("Address", Theme.TextBox("Optional"), name: nameof(SaveCustomerRequest.Address));
        _active = Field("Status", Theme.CheckBox("Active"));

        if (existing is not null)
        {
            _name.Text = existing.Name;
            _phone.Text = existing.Phone ?? string.Empty;
            _address.Text = existing.Address ?? string.Empty;
            _active.Checked = existing.IsActive;

            if (existing.IsWalkIn)
            {
                // The walk-in account is what every cash sale is booked against; renaming
                // or retiring it would orphan the till.
                _name.Enabled = false;
                _active.Enabled = false;
                ShowError("This is the walk-in account used by the till. Its name and status are fixed.");
            }
        }
        else
        {
            _active.Checked = true;
        }

        FinishLayout();
    }

    protected override async Task SaveAsync()
    {
        var request = new SaveCustomerRequest(_name.Text, _phone.Text, _address.Text, _active.Checked);

        if (_id is { } id)
        {
            await _customers.UpdateAsync(id, request).ConfigureAwait(true);
        }
        else
        {
            await _customers.CreateAsync(request).ConfigureAwait(true);
        }
    }
}

public sealed class SupplierEditor : EditorForm
{
    private readonly SupplierService _suppliers;
    private readonly long? _id;
    private readonly KryptonTextBox _name;
    private readonly KryptonTextBox _phone;
    private readonly KryptonTextBox _address;
    private readonly KryptonCheckBox _active;

    public SupplierEditor(SupplierService suppliers, SupplierDto? existing)
        : base(
            existing is null ? "New supplier" : "Edit supplier",
            existing is null ? "Someone the shop buys stock from." : existing.Name)
    {
        _suppliers = suppliers;
        _id = existing?.Id;

        _name = Field("Name", Theme.TextBox(), name: nameof(SaveSupplierRequest.Name));
        _phone = Field("Phone", Theme.TextBox("Optional"), name: nameof(SaveSupplierRequest.Phone));
        _address = Field("Address", Theme.TextBox("Optional"), name: nameof(SaveSupplierRequest.Address));
        _active = Field("Status", Theme.CheckBox("Active"));

        if (existing is not null)
        {
            _name.Text = existing.Name;
            _phone.Text = existing.Phone ?? string.Empty;
            _address.Text = existing.Address ?? string.Empty;
            _active.Checked = existing.IsActive;
        }
        else
        {
            _active.Checked = true;
        }

        FinishLayout();
    }

    protected override async Task SaveAsync()
    {
        var request = new SaveSupplierRequest(_name.Text, _phone.Text, _address.Text, _active.Checked);

        if (_id is { } id)
        {
            await _suppliers.UpdateAsync(id, request).ConfigureAwait(true);
        }
        else
        {
            await _suppliers.CreateAsync(request).ConfigureAwait(true);
        }
    }
}
