using TechShop.Application.Common.Query;
using TechShop.Application.Features.Shops;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Admin;

/// <summary>
/// The branches. A shop is never deleted — stock, invoices and the books all point at it
/// — so it is retired instead, which is why there is no Delete here.
/// </summary>
public sealed class ShopsPage : ListPage<ShopDto>
{
    public ShopsPage()
        : base(Module.Shops, "Shops", "Branches this account can see")
    {
    }

    protected override string DefaultSortField => "name";

    protected override bool DefaultSortDescending => false;

    protected override string ItemNoun => "shop";

    protected override bool SupportsCreate => true;

    protected override bool SupportsEdit => true;

    protected override Func<QueryRequest, Task<byte[]>>? Exporter
        => request => Service<ShopService>().ExportAsync(request);

    protected override IReadOnlyList<GridColumn<ShopDto>> Columns() =>
    [
        new("code", "Code", s => s.Code) { Width = 110 },
        new("name", "Shop", s => s.Name) { Fill = true },
        new("address", "Address", s => s.Address) { Width = 260 },
        new("phone", "Phone", s => s.Phone) { Width = 140 },

        new("isActive", "Status", s => s.IsActive)
        {
            Width = 100,
            Chip = s => s.IsActive
                ? ("Trading", Palette.EmeraldSoft, Palette.EmeraldText)
                : ("Closed", Palette.SlateSoft, Palette.SlateText),
        },
    ];

    protected override Task<PagedResult<ShopDto>> QueryAsync(QueryRequest request)
        => Service<ShopService>().QueryAsync(request);

    protected override string Describe(ShopDto row) => $"{row.Code} {row.Name}";

    protected override Task OnNewAsync()
    {
        using var editor = new ShopEditor(Service<ShopService>(), null);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "Shop added");
        }

        return Task.CompletedTask;
    }

    protected override Task OnEditAsync(ShopDto row)
    {
        using var editor = new ShopEditor(Service<ShopService>(), row);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), $"{row.Name} saved");
        }

        return Task.CompletedTask;
    }
}
