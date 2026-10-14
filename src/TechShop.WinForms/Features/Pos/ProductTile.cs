using System.Drawing;
using TechShop.Application.Features.Catalog;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Pos;

/// <summary>
/// One product in the till's picker: name, price, and what is left on the shelf.
///
/// Sized for a finger rather than a cursor, and it shows stock because the commonest
/// question at the counter is "have we got one". Out of stock is drawn as unavailable
/// rather than hidden, so the cashier can tell the difference between "we don't sell it"
/// and "we're out".
/// </summary>
public sealed class ProductTile : Control
{
    private readonly PosProductDto _product;
    private readonly Action<PosProductDto> _onPick;
    private bool _hovered;
    private bool _pressed;

    public ProductTile(PosProductDto product, Action<PosProductDto> onPick)
    {
        _product = product;
        _onPick = onPick;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);

        Size = new Size(MinWidth, 104);
        Margin = new Padding(0, 0, Metrics.Space3, Metrics.Space3);
        Cursor = product.Qty > 0 ? Cursors.Hand : Cursors.Default;
        TabStop = false;
    }

    /// <summary>
    /// The narrowest a tile may be before the product name stops being readable. The
    /// till widens tiles beyond this so a row fills the shelf area exactly, rather than
    /// leaving a column of dead space on the right.
    /// </summary>
    public const int MinWidth = 190;

    /// <summary>
    /// Past this a tile is mostly empty space. A search that matches one product gets a
    /// normal-sized tile and some room beside it, rather than one stretched banner.
    /// </summary>
    public const int MaxWidth = 300;

    private bool InStock => _product.Qty > 0;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnClick(EventArgs e)
    {
        if (InStock)
        {
            _onPick(_product);
        }

        base.OnClick(e);
    }

    internal bool IsTaxableForTest => _product.IsTaxable;

    internal bool InStockForTest => InStock;

    /// <summary>Picks this tile as a tap would, for the QA harness.</summary>
    internal void PickForTest()
    {
        if (InStock)
        {
            _onPick(_product);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Render.UseQualityRendering(g);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        var fill = !InStock
            ? Palette.Neutral50
            : _pressed
                ? Palette.PrimarySoft
                : _hovered ? Palette.SurfaceHover : Palette.Surface;

        Render.FillRounded(g, bounds, Metrics.RadiusMd, fill);
        Render.DrawRoundedBorder(
            g,
            bounds,
            Metrics.RadiusMd,
            _hovered && InStock ? Palette.Primary : Palette.Border);

        var ink = InStock ? Palette.Ink : Palette.Neutral400;
        var pad = Metrics.Space3;

        TextRenderer.DrawText(
            g,
            _product.Name,
            Typography.Get(Typography.SizeSm, FontStyle.Bold),
            new Rectangle(pad, pad, Width - (pad * 2), 34),
            ink,
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            g,
            _product.Sku,
            Typography.Get(Typography.Size2Xs),
            new Rectangle(pad, 46, Width - (pad * 2), 16),
            Palette.InkMuted,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            g,
            Format.Money(_product.SalePrice),
            Typography.Numeric(Typography.SizeBase, FontStyle.Bold),
            new Rectangle(pad, Height - pad - 22, Width - (pad * 2) - 60, 22),
            InStock ? Palette.PrimaryText : Palette.Neutral400,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var (stockText, stockFill, stockInk) = _product.Qty switch
        {
            <= 0 => ("Out", Palette.RoseSoft, Palette.RoseText),
            <= 3 => ($"{_product.Qty} left", Palette.AmberSoft, Palette.AmberText),
            _ => ($"{_product.Qty}", Palette.EmeraldSoft, Palette.EmeraldText),
        };

        var chipWidth = TextRenderer.MeasureText(stockText, Typography.Get(Typography.Size2Xs, FontStyle.Bold)).Width + 16;
        var chip = new Rectangle(Width - pad - chipWidth, Height - pad - 22, chipWidth, 20);
        Render.FillRounded(g, chip, 10, stockFill);

        TextRenderer.DrawText(
            g,
            stockText,
            Typography.Get(Typography.Size2Xs, FontStyle.Bold),
            chip,
            stockInk,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
