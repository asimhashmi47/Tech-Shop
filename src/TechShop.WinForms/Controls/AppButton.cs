using System.ComponentModel;
using System.Drawing;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

public enum ButtonVariant
{
    /// <summary>The one action the screen wants. At most one per view.</summary>
    Primary,

    /// <summary>Bordered, white fill. The usual choice.</summary>
    Secondary,

    /// <summary>No fill, no border until hover. For toolbar and row actions.</summary>
    Ghost,

    /// <summary>Destructive. Void, delete, revoke.</summary>
    Danger,

    /// <summary>Quiet success, used for Take payment on the till.</summary>
    Success,
}

/// <summary>
/// A flat, owner-drawn button. WinForms' own flat style has a one-pixel border it draws
/// square, and no way to round a corner, so the whole surface is drawn here instead.
/// Hover and press are a blend toward the darker end of the accent rather than a second
/// hard-coded colour, which keeps a new variant to one line.
/// </summary>
public sealed class AppButton : Control, IButtonControl
{
    private ButtonVariant _variant = ButtonVariant.Secondary;
    private bool _hovered;
    private bool _pressed;
    private Image? _glyph;
    private DialogResult _dialogResult = DialogResult.None;

    public AppButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);

        Height = Metrics.ControlHeight;
        Width = 120;
        Cursor = Cursors.Hand;
        Font = Typography.Get(Typography.SizeBase, FontStyle.Bold);
        BackColor = Color.Transparent;
        TabStop = true;
    }

    [DefaultValue(ButtonVariant.Secondary)]
    public ButtonVariant Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            Invalidate();
        }
    }

    public int CornerRadius { get; set; } = Metrics.RadiusMd;

    /// <summary>Optional leading glyph, drawn at the text's cap height.</summary>
    public Image? Glyph
    {
        get => _glyph;
        set
        {
            _glyph = value;
            Invalidate();
        }
    }

    public DialogResult DialogResult
    {
        get => _dialogResult;
        set => _dialogResult = value;
    }

    public void NotifyDefault(bool value)
    {
        // The app marks its default action with Variant.Primary, so there is nothing
        // extra to draw when the form nominates this button as its accept button.
    }

    public void PerformClick()
    {
        if (Enabled)
        {
            OnClick(EventArgs.Empty);
        }
    }

    protected override Size DefaultSize => new(120, Metrics.ControlHeight);

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
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Focus();
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            PerformClick();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Render.UseQualityRendering(g);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        var (fill, ink, border) = Resolve();

        if (fill.A > 0)
        {
            Render.FillRounded(g, bounds, CornerRadius, fill);
        }

        if (border.A > 0)
        {
            Render.DrawRoundedBorder(g, bounds, CornerRadius, border);
        }

        if (Focused && Enabled)
        {
            // A visible focus ring, because a till gets driven from the keyboard.
            var ring = Rectangle.Inflate(bounds, -2, -2);
            using var pen = new Pen(Color.FromArgb(140, Palette.Primary), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            using var path = Render.RoundedRect(ring, Math.Max(CornerRadius - 2, 2));
            g.DrawPath(pen, path);
        }

        var textBounds = bounds;

        if (_glyph is not null)
        {
            var size = Math.Min(16, Height - 14);
            var glyphRect = new Rectangle(Metrics.Space3, (Height - size) / 2, size, size);
            g.DrawImage(_glyph, glyphRect);
            textBounds = Rectangle.FromLTRB(glyphRect.Right + Metrics.Space2, bounds.Top, bounds.Right, bounds.Bottom);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            textBounds,
            ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
    }

    private (Color Fill, Color Ink, Color Border) Resolve()
    {
        if (!Enabled)
        {
            return _variant switch
            {
                ButtonVariant.Ghost => (Color.Transparent, Palette.Neutral400, Color.Transparent),
                ButtonVariant.Secondary => (Palette.Neutral50, Palette.Neutral400, Palette.Border),
                _ => (Palette.Neutral200, Palette.Neutral500, Color.Transparent),
            };
        }

        // One blend amount for hover, a deeper one for press, applied to every variant.
        var shift = _pressed ? 0.22d : _hovered ? 0.12d : 0d;

        return _variant switch
        {
            ButtonVariant.Primary => (
                Palette.Mix(Palette.Primary, Palette.Neutral900, shift),
                Palette.InkInverse,
                Color.Transparent),

            ButtonVariant.Danger => (
                Palette.Mix(Palette.Danger, Palette.Neutral900, shift),
                Palette.InkInverse,
                Color.Transparent),

            ButtonVariant.Success => (
                Palette.Mix(Palette.Success, Palette.Neutral900, shift),
                Palette.InkInverse,
                Color.Transparent),

            ButtonVariant.Ghost => (
                shift > 0 ? Palette.SurfaceHover : Color.Transparent,
                Palette.InkBody,
                shift > 0 ? Palette.Border : Color.Transparent),

            _ => (
                shift > 0 ? Palette.SurfaceHover : Palette.Surface,
                Palette.InkBody,
                Palette.Border),
        };
    }

    /// <summary>Sizes the button to its text, which is what every toolbar wants.</summary>
    public AppButton FitToText(int horizontalPadding = Metrics.Space5)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        var glyph = _glyph is null ? 0 : 16 + Metrics.Space2;
        Width = text.Width + glyph + (horizontalPadding * 2);
        return this;
    }
}

