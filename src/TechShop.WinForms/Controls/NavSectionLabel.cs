using System.Drawing;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// A sidebar section caption: OVERVIEW, SALES, INVENTORY, ACCOUNTING, ADMIN. Small, tracked
/// out, muted. Hidden when the rail is collapsed, where a rule stands in for it instead.
/// </summary>
public sealed class NavSectionLabel : Control
{
    private bool _collapsed;

    public NavSectionLabel(string text)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);

        Text = text.ToUpperInvariant();
        Height = 34;
        Dock = DockStyle.Top;
        TabStop = false;
    }

    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            _collapsed = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Render.UseQualityRendering(g);

        using (var background = new SolidBrush(Palette.SidebarBackground))
        {
            g.FillRectangle(background, ClientRectangle);
        }

        if (_collapsed)
        {
            // A hairline keeps the grouping legible without room for the word.
            using var pen = new Pen(Palette.BorderSubtle);
            var y = Height / 2;
            g.DrawLine(pen, Metrics.Space4, y, Width - Metrics.Space4, y);
            return;
        }

        var rect = Rectangle.FromLTRB(Metrics.Space5, 0, Width - Metrics.Space3, Height);

        // GDI+ has no letter-spacing, so the tracking the web app uses is approximated by
        // spacing the characters by hand. At 11px over one short word this is cheap.
        var tracked = string.Join(" ", Text.ToCharArray());

        TextRenderer.DrawText(
            g,
            tracked,
            Typography.Get(Typography.Size2Xs, FontStyle.Bold),
            rect,
            Palette.SidebarInkMuted,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
