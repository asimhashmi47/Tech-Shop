using System.Drawing;
using TechShop.Domain.Enums;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>One row in the sidebar. Active state is a soft fill plus a left rule.</summary>
public sealed class NavItem : Control
{
    private bool _hovered;
    private bool _active;
    private bool _collapsed;

    public NavItem(Module module, string label, string glyph)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);

        Module = module;
        Glyph = glyph;
        Text = label;
        Height = 40;
        Dock = DockStyle.Top;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public Module Module { get; }

    public string Glyph { get; }

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }

            _active = value;
            Invalidate();
        }
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

        using (var background = new SolidBrush(Palette.SidebarBackground))
        {
            g.FillRectangle(background, ClientRectangle);
        }

        var pill = new Rectangle(Metrics.Space2, 2, Width - (Metrics.Space2 * 2) - 1, Height - 5);

        if (_active)
        {
            Render.FillRounded(g, pill, Metrics.RadiusMd, Palette.SidebarActiveFill);

            // The left rule is what makes the active row findable at a glance when
            // several soft fills are on screen at once.
            var rule = new Rectangle(pill.X, pill.Y + 6, 3, pill.Height - 12);
            Render.FillRounded(g, rule, 2, Palette.Primary);
        }
        else if (_hovered)
        {
            Render.FillRounded(g, pill, Metrics.RadiusMd, Palette.SidebarHoverFill);
        }

        var ink = _active ? Palette.SidebarActiveInk : Palette.SidebarInk;

        var iconRect = new Rectangle(pill.X + Metrics.Space3, pill.Y, 20, pill.Height);
        Icons.Draw(g, Glyph, iconRect, ink, 11f);

        if (_collapsed)
        {
            return;
        }

        var textRect = Rectangle.FromLTRB(iconRect.Right + Metrics.Space3, pill.Y, pill.Right - Metrics.Space2, pill.Bottom);
        TextRenderer.DrawText(
            g,
            Text,
            Typography.Get(Typography.SizeBase, _active ? FontStyle.Bold : FontStyle.Regular),
            textRect,
            ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
    }
}
