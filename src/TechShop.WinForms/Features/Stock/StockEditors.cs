using Krypton.Toolkit;
using TechShop.Application.Features.Catalog;
using TechShop.Application.Features.Shops;
using TechShop.Application.Features.Stock;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Stock;

/// <summary>
/// Corrects one stock line. The operator types the quantity they actually counted, and
/// the difference becomes the adjustment — which is less error-prone than asking for a
/// signed delta, and it is what someone holding a clipboard already knows.
/// </summary>
public sealed class StockAdjustEditor : EditorForm
{
    private readonly StockService _stock;
    private readonly StockLevelDto _line;
    private readonly KryptonNumericUpDown _counted;
    private readonly KryptonTextBox _reason;

    public StockAdjustEditor(StockService stock, StockLevelDto line)
        : base("Adjust stock", $"{line.Sku} · {line.ProductName} at {line.ShopName}")
    {
        _stock = stock;
        _line = line;

        var onHand = Theme.TextBox();
        onHand.Text = Format.Qty(line.Qty);
        onHand.ReadOnly = true;
        Field("Currently on hand", onHand);

        _counted = Field(
            "Counted quantity",
            Theme.Numeric(0),
            "The difference is written to the movement history as an adjustment.",
            nameof(AdjustStockRequest.Qty));
        _counted.Maximum = 9_999_999m;
        _counted.Value = line.Qty;

        _reason = Field(
            "Reason",
            Theme.TextBox("Stock count, damage, shrinkage…"),
            "Required. This is what makes the adjustment auditable.",
            nameof(AdjustStockRequest.Reason));

        FinishLayout();
    }

    protected override async Task SaveAsync()
    {
        var difference = (int)_counted.Value - _line.Qty;

        if (difference == 0)
        {
            ShowError("The counted quantity matches what is on hand, so there is nothing to adjust.");
            return;
        }

        await _stock.AdjustAsync(new AdjustStockRequest(
            _line.ShopId,
            _line.ProductId,
            difference,
            _reason.Text)).ConfigureAwait(true);
    }
}

/// <summary>Moves stock from one branch to another, as a matched pair of movements.</summary>
public sealed class StockTransferEditor : EditorForm
{
    private readonly StockService _stock;
    private readonly KryptonComboBox _from;
    private readonly KryptonComboBox _to;
    private readonly KryptonComboBox _product;
    private readonly KryptonNumericUpDown _qty;
    private readonly KryptonTextBox _note;

    public StockTransferEditor(
        StockService stock,
        IReadOnlyList<ShopDto> shops,
        IReadOnlyList<ProductDto> products,
        StockLevelDto? selected)
        : base("Transfer stock", "Moves goods between branches and records both sides.")
    {
        _stock = stock;

        _from = Field(
            "From shop",
            Lookup(shops, s => s.Id, s => s.Name, selected?.ShopId),
            name: nameof(TransferStockRequest.FromShopId));

        _to = Field(
            "To shop",
            Lookup(shops, s => s.Id, s => s.Name, shops.FirstOrDefault(s => s.Id != selected?.ShopId)?.Id),
            name: nameof(TransferStockRequest.ToShopId));

        _product = Field(
            "Product",
            Lookup(products, p => p.Id, p => $"{p.Sku}  {p.Name}", selected?.ProductId),
            name: nameof(TransferStockRequest.ProductId));

        _qty = Field(
            "Quantity",
            Theme.Numeric(0),
            "Cannot be more than the sending shop holds.",
            nameof(TransferStockRequest.Qty));
        _qty.Minimum = 1m;
        _qty.Value = 1m;

        _note = Field("Note", Theme.TextBox("Optional"), name: nameof(TransferStockRequest.Note));

        FinishLayout();
    }

    protected override async Task SaveAsync()
    {
        if (SelectedId(_from) is not { } fromId || SelectedId(_to) is not { } toId)
        {
            ShowError("Choose both shops.");
            return;
        }

        if (SelectedId(_product) is not { } productId)
        {
            ShowError("Choose a product.");
            return;
        }

        if (fromId == toId)
        {
            ShowError("Choose two different shops.");
            return;
        }

        await _stock.TransferAsync(new TransferStockRequest(
            fromId,
            toId,
            productId,
            (int)_qty.Value,
            _note.Text)).ConfigureAwait(true);
    }
}
