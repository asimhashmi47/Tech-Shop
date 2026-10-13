using System.Drawing;
using TechShop.Domain.Enums;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// A status chip: soft tinted pill, accent text. Status is never carried by colour alone
/// here — the word is always in the chip — so it still reads for a colour-blind operator
/// and in a black-and-white print of an invoice.
/// </summary>
public sealed class Badge : Control
{
    private Color _fill = Palette.SlateSoft;
    private Color _ink = Palette.SlateText;

    public Badge()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);

        Font = Typography.Get(Typography.Size2Xs, FontStyle.Bold);
        BackColor = Color.Transparent;
        Height = 22;
        TabStop = false;
    }

    public void SetTone(Color fill, Color ink)
    {
        _fill = fill;
        _ink = ink;
        Invalidate();
    }

    public static (Color Fill, Color Ink) ToneFor(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Paid => (Palette.EmeraldSoft, Palette.EmeraldText),
        InvoiceStatus.Partial => (Palette.AmberSoft, Palette.AmberText),
        InvoiceStatus.Unpaid => (Palette.RoseSoft, Palette.RoseText),
        _ => (Palette.SlateSoft, Palette.SlateText),
    };

    public static (Color Fill, Color Ink) ToneFor(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => (Palette.EmeraldSoft, Palette.EmeraldText),
        PaymentMethod.Card => (Palette.IndigoSoft, Palette.IndigoText),
        PaymentMethod.Bank => (Palette.CyanSoft, Palette.CyanText),
        _ => (Palette.AmberSoft, Palette.AmberText),
    };

    public void Show(InvoiceStatus status)
    {
        var (fill, ink) = ToneFor(status);
        Text = status.ToString();
        SetTone(fill, ink);
        FitToText();
    }

    public void Show(PaymentMethod method)
    {
        var (fill, ink) = ToneFor(method);
        Text = method.ToString();
        SetTone(fill, ink);
        FitToText();
    }

    public void FitToText()
    {
        var size = TextRenderer.MeasureText(Text, Font);
        Width = size.Width + (Metrics.Space3 * 2);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Render.UseQualityRendering(g);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Render.FillRounded(g, bounds, Height / 2, _fill);

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            bounds,
            _ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

