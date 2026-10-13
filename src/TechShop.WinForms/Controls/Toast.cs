using System.Drawing;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

public enum ToastTone
{
    Success,
    Info,
    Warning,
    Error,
}

/// <summary>
/// A brief confirmation in the corner of the window: "Invoice saved", "Stock adjusted".
///
/// Used instead of a modal for anything that worked. A dialog that only says yes costs the
/// operator a click for no information, and at a till that click is in the way of the next
/// customer. Anything that failed still gets a dialog.
/// </summary>
public sealed class Toast : Form
{
    private const int Lifetime = 2600;

    // Named Gap rather than Margin: Control already has a Margin property.
    private const int Gap = Metrics.Space5;

    private readonly System.Windows.Forms.Timer _timer;
    private readonly string _message;
    private readonly ToastTone _tone;

    private Toast(string message, ToastTone tone)
    {
        _message = message;
        _tone = tone;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Palette.Neutral900;
        Opacity = 0d;
        TopMost = true;

        var width = Math.Min(420, TextRenderer.MeasureText(message, Typography.Get(Typography.SizeBase)).Width + 96);
        Size = new Size(Math.Max(240, width), 52);

        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        Click += (_, _) => Close();
    }

    public static void Show(IWin32Window? owner, string message, ToastTone tone = ToastTone.Success)
    {
        if (owner is not Form form || form.IsDisposed)
        {
            return;
        }

        var toast = new Toast(message, tone);
        toast.Place(form);
        toast.Show(form);
        toast.BeginLife();
    }

    private void Place(Form owner)
    {
        var area = owner.RectangleToScreen(owner.ClientRectangle);
        Location = new Point(
            area.Right - Width - Gap,
            area.Bottom - Height - Gap - 40);
    }

    private void BeginLife()
    {
        var elapsed = 0;

        _timer.Tick += (_, _) =>
        {
            elapsed += _timer.Interval;

            // Fade in over the first 140ms, hold, then fade out over the last 200ms.
            // Ease-out on both ends; nothing bounces.
            Opacity = elapsed switch
            {
                < 140 => Ease(elapsed / 140d),
                > Lifetime - 200 => Ease(Math.Max(0d, (Lifetime - elapsed) / 200d)),
                _ => 1d,
            };

            if (elapsed >= Lifetime)
            {
                _timer.Stop();
                Close();
            }
        };

        _timer.Start();
    }

    private static double Ease(double t) => 1d - Math.Pow(1d - Math.Clamp(t, 0d, 1d), 3d);

    protected override bool ShowWithoutActivation => true;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Render.UseQualityRendering(g);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Render.FillRounded(g, bounds, Metrics.RadiusMd, Palette.Neutral900);

        var (glyph, accent) = _tone switch
        {
            ToastTone.Success => (Icons.Success, Palette.Emerald),
            ToastTone.Warning => (Icons.Warning, Palette.Amber),
            ToastTone.Error => (Icons.Error, Palette.Rose),
            _ => (Icons.Info, Palette.Cyan),
        };

        var dot = new Rectangle(Metrics.Space4, (Height - 22) / 2, 22, 22);
        Render.FillRounded(g, dot, Metrics.RadiusSm, accent);
        Icons.Draw(g, glyph, dot, Color.White, 9f);

        var text = Rectangle.FromLTRB(dot.Right + Metrics.Space3, 0, Width - Metrics.Space4, Height);
        TextRenderer.DrawText(
            g,
            _message,
            Typography.Get(Typography.SizeBase),
            text,
            Palette.Neutral0,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
