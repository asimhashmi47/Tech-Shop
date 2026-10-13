using System.Drawing;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// A dashboard figure: gradient wash, a translucent icon tile, the label, the number and
/// a line saying what the number actually counts. That last line is the point of the card
/// — "Rs 2,737,165" means nothing without "sales less cost of goods" under it.
///
/// Clicking a card raises <see cref="Click"/>, which the dashboard uses to send the
/// operator to the rows behind the figure.
/// </summary>
public sealed class KpiCard : Control
{
    private bool _hovered;
    private string _label = string.Empty;
    private string _value = string.Empty;
    private string _caption = string.Empty;
    private string _glyph = Icons.Dashboard;
    private Color _accent = Palette.Indigo;

    public KpiCard()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);

        Size = new Size(Metrics.KpiCardWidth, Metrics.KpiCardHeight);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public string Label
    {
        get => _label;
        set { _label = value; Invalidate(); }
    }

    public string Value
    {
        get => _value;
        set { _value = value; Invalidate(); }
    }

    public string Caption
    {
        get => _caption;
        set { _caption = value; Invalidate(); }
    }

    public string Glyph
    {
        get => _glyph;
        set { _glyph = value; Invalidate(); }
    }

    public Color Accent
    {
        get => _accent;
        set { _accent = value; Invalidate(); }
    }

    /// <summary>True when the card links somewhere. A card that does not should not look clickable.</summary>
    public bool IsLink
    {
        get => Cursor == Cursors.Hand;
        set => Cursor = value ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Render.UseQualityRendering(g);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        // Hover lifts the whole wash a little rather than moving the card. Nudging
        // geometry on hover makes a grid of cards twitch as the pointer crosses it.
        var lift = _hovered && IsLink ? 0.06d : 0d;
        var from = Palette.Mix(_accent, Palette.Neutral0, lift);
        var to = Palette.Mix(Render.GradientEnd(_accent), Palette.Neutral0, lift);

        Render.FillGradientRounded(g, bounds, Metrics.RadiusLg, from, to);

        // The card is laid out from what it has rather than from fixed offsets, because
        // the dashboard shrinks these to fit a laptop. Below roughly 132px the tile and
        // the caption are the first things to go: the figure and its label are the two
        // parts that carry the meaning.
        var tight = Height < 132;
        var pad = tight ? Metrics.Space2 + 2 : Metrics.Space3;
        var tileSize = tight ? 0 : 28;

        var labelLeft = pad;

        if (tileSize > 0)
        {
            var tile = new Rectangle(pad, pad, tileSize, tileSize);
            Render.FillRounded(g, tile, Metrics.RadiusSm, Color.FromArgb(48, 255, 255, 255));
            Icons.Draw(g, _glyph, tile, Color.FromArgb(235, 255, 255, 255), 10f);
            labelLeft = tile.Right + Metrics.Space2;
        }

        var labelHeight = tight ? 16 : tileSize;

        TextRenderer.DrawText(
            g,
            _label,
            Typography.Get(tight ? Typography.Size2Xs : Typography.SizeXs, FontStyle.Bold),
            new Rectangle(labelLeft, pad, Width - labelLeft - pad, labelHeight),
            Color.FromArgb(230, 255, 255, 255),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.EndEllipsis);

        // The caption only earns its place if there is room left after the figure.
        var captionHeight = tight ? 0 : 16;
        var valueTop = pad + labelHeight + (tight ? 2 : Metrics.Space2);
        var valueHeight = Math.Max(22, Height - valueTop - pad - captionHeight);

        // The figure is the reason the card exists, so it gets the largest size that
        // still fits on one line. Shrinking beats wrapping here: a wrapped total reads
        // as two numbers, and a clipped one is worse than either.
        var valueRect = new Rectangle(pad, valueTop, Width - (pad * 2), valueHeight);

        TextRenderer.DrawText(
            g,
            _value,
            FitToWidth(g, _value, valueRect.Width, valueHeight),
            valueRect,
            Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine);

        if (captionHeight == 0)
        {
            return;
        }

        TextRenderer.DrawText(
            g,
            _caption,
            Typography.Get(Typography.Size2Xs),
            new Rectangle(pad, Height - pad - captionHeight, Width - (pad * 2), captionHeight),
            Color.FromArgb(205, 255, 255, 255),
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// The largest step on the type scale at which the figure still fits on one line.
    /// A shop's totals run to millions, and "Rs 184,535,625.00" is a very different
    /// width from "37".
    /// </summary>
    private static Font FitToWidth(Graphics g, string text, int width, int height)
    {
        foreach (var size in new[] { Typography.Size2Xl, Typography.SizeXl, Typography.SizeLg, Typography.SizeMd })
        {
            var font = Typography.Get(size, FontStyle.Bold);
            var measured = TextRenderer.MeasureText(g, text, font);

            if (measured.Width <= width && measured.Height <= height)
            {
                return font;
            }
        }

        return Typography.Get(Typography.SizeBase, FontStyle.Bold);
    }
}

