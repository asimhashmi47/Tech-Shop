using System.Drawing;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Catalog;
using TechShop.Application.Features.Shops;
using TechShop.Application.Features.Stock;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Stock;

/// <summary>
/// What is on the shelf, per shop.
///
/// Stock is never typed over directly. Every change goes through an adjustment or a
/// transfer, both of which write a movement saying who changed it and why — which is what
/// makes the stock figure defensible when it disagrees with a physical count.
/// </summary>
public sealed class StockPage : ListPage<StockLevelDto>
{
    private bool _actionsAdded;

    public StockPage()
        : base(Module.Stock, "Stock", "On hand by shop")
    {
    }

    protected override string DefaultSortField => "qty";

    protected override bool DefaultSortDescending => false;

    protected override string ItemNoun => "line";

    protected override Func<QueryRequest, Task<byte[]>>? Exporter
        => request => Service<StockService>().ExportLevelsAsync(request);

    protected override IReadOnlyList<GridColumn<StockLevelDto>> Columns() =>
    [
        new("sku", "SKU", s => s.Sku) { Width = 130 },
        new("productName", "Product", s => s.ProductName) { Fill = true },
        new("categoryName", "Category", s => s.CategoryName) { Width = 120 },
        new("shopName", "Shop", s => s.ShopName) { Width = 170 },

        new("qty", "On hand", s => s.Qty)
        {
            Width = 90,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = s => Format.Qty(s.Qty),
            Foreground = s => s.Qty <= 0
                ? Palette.DangerText
                : s.IsLow ? Palette.WarningText : null,
        },

        new("reorderLevel", "Reorder at", s => s.ReorderLevel)
        {
            Width = 92,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = s => Format.Qty(s.ReorderLevel),
        },

        new("stockValue", Format.MoneyHeader("Value at cost"), s => s.StockValue)
        {
            Width = 140,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Format = s => Format.Amount(s.StockValue),
        },

        new("isLow", "Flag", s => s.IsLow)
        {
            Width = 100,
            Chip = s => s.Qty <= 0
                ? ("Out", Palette.RoseSoft, Palette.RoseText)
                : s.IsLow
                    ? ("Low", Palette.AmberSoft, Palette.AmberText)
                    : ("Ok", Palette.EmeraldSoft, Palette.EmeraldText),
        },
    ];

    protected override Task<PagedResult<StockLevelDto>> QueryAsync(QueryRequest request)
        => Service<StockService>().QueryLevelsAsync(request);

    protected override Task AfterLoadAsync()
    {
        if (_actionsAdded)
        {
            return Task.CompletedTask;
        }

        var movements = new AppButton { Variant = ButtonVariant.Secondary, Text = "Movements" };
        movements.FitToText(Metrics.Space3);
        movements.Click += (_, _) => ShowMovements();
        Grid.AddAction(movements);

        if (CanWrite)
        {
            var transfer = new AppButton { Variant = ButtonVariant.Secondary, Text = "Transfer" };
            transfer.FitToText(Metrics.Space3);
            transfer.Click += async (_, _) => await TransferAsync().ConfigureAwait(true);
            Grid.AddAction(transfer);

            var adjust = new AppButton { Variant = ButtonVariant.Primary, Text = "Adjust" };
            adjust.FitToText(Metrics.Space3);
            adjust.Click += async (_, _) => await AdjustAsync().ConfigureAwait(true);
            Grid.AddAction(adjust);
        }

        _actionsAdded = true;
        return Task.CompletedTask;
    }

    private void ShowMovements()
    {
        var selected = Grid.Selected;
        using var dialog = new StockMovementsDialog(Service<StockService>(), ActiveShopId, selected?.Sku);
        dialog.ShowDialog(FindForm());
    }

    private async Task AdjustAsync()
    {
        if (Grid.Selected is not { } row)
        {
            Dialogs.Info(FindForm(), "Nothing selected", "Choose a stock line to adjust.");
            return;
        }

        using var editor = new StockAdjustEditor(Service<StockService>(), row);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), $"{row.Sku} adjusted");
            await Grid.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task TransferAsync()
    {
        var shops = await Service<ShopService>().LookupAsync().ConfigureAwait(true);

        if (shops.Count < 2)
        {
            Dialogs.Warn(
                FindForm(),
                "Nothing to transfer between",
                "A transfer needs two shops this account can reach.");
            return;
        }

        var products = await Service<ProductService>()
            .QueryAsync(new QueryRequest { PageSize = 500, Sort = [new SortRule { Field = "name" }] })
            .ConfigureAwait(true);

        using var editor = new StockTransferEditor(
            Service<StockService>(),
            shops,
            products.Items,
            Grid.Selected);

        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "Stock transferred");
            await Grid.ReloadAsync().ConfigureAwait(true);
        }
    }
}
