using System.Drawing;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Catalog;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Catalog;

/// <summary>The catalogue. What the shop sells, what it costs and what it sells for.</summary>
public sealed class ProductsPage : ListPage<ProductDto>
{
    public ProductsPage()
        : base(Module.Products, "Products", "The catalogue across every shop")
    {
    }

    protected override string DefaultSortField => "name";

    protected override bool DefaultSortDescending => false;

    protected override string ItemNoun => "product";

    protected override bool SupportsCreate => true;

    protected override bool SupportsEdit => true;

    protected override bool SupportsDelete => true;

    protected override Func<QueryRequest, Task<byte[]>>? Exporter
        => request => Service<ProductService>().ExportAsync(request);

    protected override IReadOnlyList<GridColumn<ProductDto>> Columns() =>
    [
        new("sku", "SKU", p => p.Sku) { Width = 130 },
        new("name", "Product", p => p.Name) { Fill = true },
        new("categoryName", "Category", p => p.CategoryName) { Width = 120 },
        new("brand", "Brand", p => p.Brand) { Width = 110 },

        new("costPrice", Format.MoneyHeader("Cost"), p => p.CostPrice)
        {
            Width = 110,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = p => Format.Amount(p.CostPrice),
        },

        new("salePrice", Format.MoneyHeader("Price"), p => p.SalePrice)
        {
            Width = 110,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = p => Format.Amount(p.SalePrice),
        },

        new("totalStock", "Stock", p => p.TotalStock)
        {
            Width = 80,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = p => Format.Qty(p.TotalStock),

            // The point of showing stock here is to spot what has run out, so the
            // figure itself carries the warning.
            Foreground = p => p.TotalStock <= 0
                ? Palette.DangerText
                : p.TotalStock <= p.ReorderLevel ? Palette.WarningText : null,
        },

        new("reorderLevel", "Reorder at", p => p.ReorderLevel)
        {
            Width = 90,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = p => Format.Qty(p.ReorderLevel),
        },

        new("isTaxable", "Tax", p => p.IsTaxable)
        {
            Width = 90,
            Chip = p => p.IsTaxable
                ? ($"{p.TaxRate:0.##}%", Palette.IndigoSoft, Palette.IndigoText)
                : ("Exempt", Palette.SlateSoft, Palette.SlateText),
        },

        new("isActive", "Status", p => p.IsActive)
        {
            Width = 90,
            Chip = p => p.IsActive
                ? ("Active", Palette.EmeraldSoft, Palette.EmeraldText)
                : ("Retired", Palette.SlateSoft, Palette.SlateText),
        },
    ];

    protected override Task<PagedResult<ProductDto>> QueryAsync(QueryRequest request)
        => Service<ProductService>().QueryAsync(request);

    protected override string Describe(ProductDto row) => $"{row.Sku} {row.Name}";

    protected override async Task OnNewAsync()
    {
        var categories = await Service<CategoryService>().LookupAsync().ConfigureAwait(true);

        if (categories.Count == 0)
        {
            Dialogs.Warn(
                FindForm(),
                "No categories",
                "Add a category first. Every product belongs to one.");
            return;
        }

        using var editor = new ProductEditor(Service<ProductService>(), categories, null);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "Product added");
        }
    }

    protected override async Task OnEditAsync(ProductDto row)
    {
        var categories = await Service<CategoryService>().LookupAsync().ConfigureAwait(true);
        using var editor = new ProductEditor(Service<ProductService>(), categories, row);

        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), $"{row.Sku} saved");
        }
    }

    protected override Task OnDeleteAsync(ProductDto row)
        => Service<ProductService>().DeleteAsync(row.Id);
}
