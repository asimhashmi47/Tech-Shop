using Krypton.Toolkit;
using TechShop.Application.Features.Catalog;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Catalog;

/// <summary>Add or change a catalogue line.</summary>
public sealed class ProductEditor : EditorForm
{
    private readonly ProductService _products;
    private readonly long? _id;

    private readonly KryptonTextBox _sku;
    private readonly KryptonTextBox _barcode;
    private readonly KryptonTextBox _name;
    private readonly KryptonComboBox _category;
    private readonly KryptonTextBox _brand;
    private readonly KryptonNumericUpDown _cost;
    private readonly KryptonNumericUpDown _price;
    private readonly KryptonNumericUpDown _reorder;
    private readonly KryptonCheckBox _taxable;
    private readonly KryptonNumericUpDown _taxRate;
    private readonly KryptonCheckBox _active;

    public ProductEditor(
        ProductService products,
        IReadOnlyList<CategoryDto> categories,
        ProductDto? existing)
        : base(
            existing is null ? "New product" : "Edit product",
            existing is null ? "Adds a line to the catalogue." : existing.Sku)
    {
        _products = products;
        _id = existing?.Id;

        _sku = Field("SKU", Theme.TextBox("PC-RYZ5-001"), name: nameof(SaveProductRequest.Sku));
        _barcode = Field(
            "Barcode",
            Theme.TextBox("Scan or type"),
            "Optional. The till can find a product by scanning this.",
            nameof(SaveProductRequest.Barcode));

        _name = Field("Product name", Theme.TextBox(), name: nameof(SaveProductRequest.Name));

        _category = Field(
            "Category",
            Lookup(categories, c => c.Id, c => c.Name, existing?.CategoryId),
            name: nameof(SaveProductRequest.CategoryId));

        _brand = Field("Brand", Theme.TextBox("Optional"), name: nameof(SaveProductRequest.Brand));

        (_cost, _price) = FieldPair(
            Format.MoneyHeader("Cost"),
            Theme.Numeric(),
            Format.MoneyHeader("Sale price"),
            Theme.Numeric());

        _reorder = Field(
            "Reorder level",
            Theme.Numeric(0),
            "The dashboard flags the product once stock reaches this.",
            nameof(SaveProductRequest.ReorderLevel));

        (_taxable, _taxRate) = FieldPair(
            "Sales tax",
            Theme.CheckBox("This product is taxable"),
            "Tax rate (%)",
            Theme.Numeric(2));

        _taxRate.Maximum = 100m;

        // A rate on a non-taxable product looks live but never applies, so the box
        // follows the tick rather than sitting there inviting a value.
        _taxable.CheckedChanged += (_, _) => _taxRate.Enabled = _taxable.Checked;

        _active = Field("Status", Theme.CheckBox("Active and sellable"));

        if (existing is not null)
        {
            _sku.Text = existing.Sku;
            _barcode.Text = existing.Barcode ?? string.Empty;
            _name.Text = existing.Name;
            _brand.Text = existing.Brand ?? string.Empty;
            _cost.Value = existing.CostPrice;
            _price.Value = existing.SalePrice;
            _reorder.Value = existing.ReorderLevel;
            _taxable.Checked = existing.IsTaxable;
            _taxRate.Value = existing.TaxRate;
            _active.Checked = existing.IsActive;
        }
        else
        {
            _active.Checked = true;
        }

        _taxRate.Enabled = _taxable.Checked;

        FinishLayout();
    }

    protected override async Task SaveAsync()
    {
        if (SelectedId(_category) is not { } categoryId)
        {
            ShowError("Add a category before adding products.");
            return;
        }

        var request = new SaveProductRequest(
            _sku.Text,
            _barcode.Text,
            _name.Text,
            categoryId,
            _brand.Text,
            _cost.Value,
            _price.Value,
            (int)_reorder.Value,
            _taxable.Checked,
            _taxRate.Value,
            _active.Checked);

        if (_id is { } id)
        {
            await _products.UpdateAsync(id, request).ConfigureAwait(true);
        }
        else
        {
            await _products.CreateAsync(request).ConfigureAwait(true);
        }
    }
}
