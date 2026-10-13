using Krypton.Toolkit;
using TechShop.Application.Features.Shops;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Admin;

public sealed class ShopEditor : EditorForm
{
    private readonly ShopService _shops;
    private readonly long? _id;
    private readonly KryptonTextBox _code;
    private readonly KryptonTextBox _name;
    private readonly KryptonTextBox _address;
    private readonly KryptonTextBox _phone;
    private readonly KryptonCheckBox _active;

    public ShopEditor(ShopService shops, ShopDto? existing)
        : base(
            existing is null ? "New shop" : "Edit shop",
            existing is null ? "A branch with its own stock and cash drawer." : existing.Name)
    {
        _shops = shops;
        _id = existing?.Id;

        _code = Field(
            "Code",
            Theme.TextBox("MAIN"),
            "Short and unique. It prefixes every invoice and bill number from this shop.",
            nameof(SaveShopRequest.Code));

        _name = Field("Shop name", Theme.TextBox(), name: nameof(SaveShopRequest.Name));
        _address = Field("Address", Theme.TextBox("Optional"), name: nameof(SaveShopRequest.Address));
        _phone = Field("Phone", Theme.TextBox("Optional"), name: nameof(SaveShopRequest.Phone));
        _active = Field("Status", Theme.CheckBox("Open and trading"));

        if (existing is not null)
        {
            _code.Text = existing.Code;
            _name.Text = existing.Name;
            _address.Text = existing.Address ?? string.Empty;
            _phone.Text = existing.Phone ?? string.Empty;
            _active.Checked = existing.IsActive;

            // Changing the code after it has been stamped onto documents would make the
            // numbering inconsistent with the history.
            _code.Enabled = false;
        }
        else
        {
            _active.Checked = true;
        }

        FinishLayout();
    }

    protected override async Task SaveAsync()
    {
        var request = new SaveShopRequest(
            _code.Text,
            _name.Text,
            _address.Text,
            _phone.Text,
            _active.Checked);

        if (_id is { } id)
        {
            await _shops.UpdateAsync(id, request).ConfigureAwait(true);
        }
        else
        {
            await _shops.CreateAsync(request).ConfigureAwait(true);
        }
    }
}
