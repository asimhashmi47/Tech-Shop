using System.Drawing;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// The money block at the foot of the till's cart: subtotal, discount, tax, total.
///
/// Drawn as one control rather than assembled from labels. Six separately positioned
/// labels is how the alignment drifted in the first place — the values were anchored to
/// the right edge while their captions stayed where they were first placed, so "Tax" sat
/// at the far left of its own figure.
///
/// Laid out like a receipt: captions on the left, figures hard against the right, a rule
/// above the total, and the total itself large enough to read from standing height.
/// </summary>
public sealed class CartSummary : Control
{
    private decimal _subTotal;
    private decimal _discount;
    private decimal _tax;
    private bool _taxApplies;

    public CartSummary()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Height = 132;
        TabStop = false;
    }

    public decimal Total => _subTotal - _discount + _tax;

    /// <summary>
    /// <paramref name="taxApplies"/> is separate from a non-zero amount: a cart whose
    /// items are all exempt should say so with a dash rather than silently hiding the
    /// line, so the cashier can tell the till did consider tax.
    /// </summary>
    public void Show(decimal subTotal, decimal discount, decimal tax, bool taxApplies)
    {
        _subTotal = subTotal;
        _discount = discount;
        _tax = tax;
        _taxApplies = taxApplies;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Render.UseQualityRendering(g);

        var right = Width;
        var y = 0;

        Row(g, "Subtotal", Format.Amount(_subTotal), right, ref y, Palette.InkBody);

        if (_discount > 0m)
        {
            // Shown as a negative, because that is what it does to the bill.
            Row(g, "Discount", "-" + Format.Amount(_discount), right, ref y, Palette.DangerText);
        }

        if (_taxApplies || _tax > 0m)
        {
            Row(
                g,
                "Sales tax",
                _tax > 0m ? Format.Amount(_tax) : "—",
                right,
                ref y,
                _tax > 0m ? Palette.InkBody : Palette.InkMuted);
        }

        y += Metrics.Space2;

        using (var pen = new Pen(Palette.Border))
        {
            g.DrawLine(pen, 0, y, right, y);
        }

        y += Metrics.Space3;

        // The total is the one figure read from across the counter, so it gets the room
        // and shrinks only when the number is genuinely too wide for the panel.
        var caption = "TOTAL";
        var captionFont = Typography.Get(Typography.SizeXs, FontStyle.Bold);
        var captionWidth = TextRenderer.MeasureText(g, caption, captionFont).Width;

        var money = Format.Money(Total);
        var moneyFont = FitTotal(g, money, right - captionWidth - Metrics.Space4);
        var moneyHeight = TextRenderer.MeasureText(g, money, moneyFont).Height;

        TextRenderer.DrawText(
            g,
            caption,
            captionFont,
            new Rectangle(0, y, captionWidth, moneyHeight),
            Palette.InkMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            money,
            moneyFont,
            new Rectangle(captionWidth, y, right - captionWidth, moneyHeight),
            Palette.InkStrong,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var wanted = y + moneyHeight + Metrics.Space1;
        if (Height != wanted)
        {
            Height = wanted;
        }
    }

    private static void Row(Graphics g, string caption, string value, int right, ref int y, Color valueColour)
    {
        const int height = 24;

        TextRenderer.DrawText(
            g,
            caption,
            Typography.Get(Typography.SizeBase),
            new Rectangle(0, y, right / 2, height),
            Palette.InkMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            value,
            Typography.Numeric(Typography.SizeBase),
            new Rectangle(right / 2, y, right - (right / 2), height),
            valueColour,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        y += height;
    }

    private static Font FitTotal(Graphics g, string text, int width)
    {
        foreach (var size in new[] { Typography.Size3Xl, Typography.Size2Xl, Typography.SizeXl })
        {
            var font = Typography.Numeric(size, FontStyle.Bold);

            if (TextRenderer.MeasureText(g, text, font).Width <= width)
            {
                return font;
            }
        }

        return Typography.Numeric(Typography.SizeLg, FontStyle.Bold);
    }
}
