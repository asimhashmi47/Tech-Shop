using System.Drawing;
using System.Drawing.Drawing2D;

namespace TechShop.WinForms.Theming;

/// <summary>
/// The drawing primitives every custom control in the app shares: rounded rectangles,
/// soft elevation and the two-stop gradients the KPI cards use. Keeping them in one place
/// is what stops twelve controls each inventing their own corner radius maths.
/// </summary>
public static class Render
{
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        // Clamp so a tall radius on a short control degrades to a pill rather than tearing.
        var d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (d <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color fill)
    {
        using var path = RoundedRect(bounds, radius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedBorder(Graphics g, Rectangle bounds, int radius, Color colour, float width = 1f)
    {
        // Inset by half the pen so the stroke lands inside the control, not half outside it.
        var inset = Rectangle.Inflate(bounds, -(int)Math.Ceiling(width / 2f), -(int)Math.Ceiling(width / 2f));
        using var path = RoundedRect(inset, radius);
        using var pen = new Pen(colour, width);
        g.DrawPath(pen, path);
    }

    /// <summary>
    /// The card wash: a diagonal two-stop gradient. The second stop is derived from the
    /// first rather than given, so a card only ever names one colour.
    /// </summary>
    public static void FillGradientRounded(Graphics g, Rectangle bounds, int radius, Color from, Color to)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var path = RoundedRect(bounds, radius);
        using var brush = new LinearGradientBrush(
            new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            from,
            to,
            LinearGradientMode.ForwardDiagonal);
        g.FillPath(brush, path);
    }

    /// <summary>
    /// A cheap stand-in for a CSS box-shadow: concentric rounded strokes fading outward.
    /// GDI+ has no blur, and a real one costs a bitmap per repaint, which a grid cannot
    /// afford. Three rings at low alpha reads as elevation at normal viewing distance.
    /// </summary>
    public static void DrawElevation(Graphics g, Rectangle bounds, int radius, int depth = 3, int alpha = 10)
    {
        for (var i = depth; i >= 1; i--)
        {
            var ring = Rectangle.Inflate(bounds, i, i);
            using var path = RoundedRect(ring, radius + i);
            using var pen = new Pen(Color.FromArgb(alpha, Palette.Neutral900), 1f);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Darkens a solid accent for the far end of a card gradient.</summary>
    public static Color GradientEnd(Color accent) => Palette.Mix(accent, Palette.Neutral900, 0.28d);

    public static void UseQualityRendering(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }
}

