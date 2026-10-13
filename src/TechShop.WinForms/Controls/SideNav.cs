using System.Drawing;
using TechShop.Domain.Enums;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

public sealed record NavEntry(Module Module, string Label, string Glyph, string Section);

/// <summary>
/// The left rail: brand, grouped navigation, collapse toggle and a build stamp.
///
/// The rail only ever shows what the signed-in user may read. Hiding a row is a courtesy —
/// the service layer refuses the call either way — but a menu full of rows that error on
/// click is a worse product than a short menu.
/// </summary>
public sealed class SideNav : Panel
{
    private readonly FlowLayoutPanel _items;
    private readonly Panel _brand;
    private readonly Label _buildStamp;
    private readonly AppButton _toggle;
    private readonly List<NavItem> _navItems = [];
    private readonly List<NavSectionLabel> _sections = [];
    private bool _collapsed;

    public SideNav()
    {
        Dock = DockStyle.Left;
        Width = Metrics.SidebarWidth;
        BackColor = Palette.SidebarBackground;
        Padding = new Padding(0);

        _brand = BuildBrand();

        _toggle = new AppButton
        {
            Variant = ButtonVariant.Ghost,
            Text = Icons.ChevronLeft,
            Font = Icons.Font(10f),
            Width = 30,
            Height = 30,
            CornerRadius = Metrics.RadiusSm,
        };
        _toggle.Click += (_, _) => Collapsed = !Collapsed;
        _brand.Controls.Add(_toggle);

        _items = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Palette.SidebarBackground,
            Padding = new Padding(0, Metrics.Space2, 0, Metrics.Space2),
        };

        _buildStamp = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            Text = "TechShop",
            ForeColor = Palette.Neutral400,
            Font = Typography.Get(Typography.Size2Xs),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(Metrics.Space5, 0, 0, 0),
            BackColor = Palette.SidebarBackground,
        };

        Controls.Add(_items);
        Controls.Add(_buildStamp);
        Controls.Add(_brand);
    }

    public event EventHandler<Module>? Navigated;

    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value)
            {
                return;
            }

            _collapsed = value;
            Width = value ? Metrics.SidebarWidthCollapsed : Metrics.SidebarWidth;
            _toggle.Text = value ? Icons.ChevronRight : Icons.ChevronLeft;
            _buildStamp.Visible = !value;

            foreach (var item in _navItems)
            {
                item.Collapsed = value;
                item.Width = ItemWidth;
            }

            foreach (var section in _sections)
            {
                section.Collapsed = value;
                section.Width = ItemWidth;
            }

            _brand.Invalidate();
        }
    }

    private int ItemWidth => Width - Metrics.Space2 - SystemInformation.VerticalScrollBarWidth;

    /// <summary>Rebuilds the rail for the given entries, which the shell has already filtered.</summary>
    public void Build(IReadOnlyList<NavEntry> entries)
    {
        _items.SuspendLayout();
        foreach (Control existing in _items.Controls)
        {
            existing.Dispose();
        }

        _items.Controls.Clear();
        _navItems.Clear();
        _sections.Clear();

        string? section = null;

        foreach (var entry in entries)
        {
            if (!string.Equals(section, entry.Section, StringComparison.Ordinal))
            {
                section = entry.Section;
                var label = new NavSectionLabel(entry.Section)
                {
                    Dock = DockStyle.None,
                    Width = ItemWidth,
                    Collapsed = _collapsed,
                };
                _sections.Add(label);
                _items.Controls.Add(label);
            }

            var item = new NavItem(entry.Module, entry.Label, entry.Glyph)
            {
                Dock = DockStyle.None,
                Width = ItemWidth,
                Collapsed = _collapsed,
            };
            item.Click += (_, _) => Navigated?.Invoke(this, item.Module);
            _navItems.Add(item);
            _items.Controls.Add(item);
        }

        _items.ResumeLayout();
    }

    public void SetActive(Module module)
    {
        foreach (var item in _navItems)
        {
            item.Active = item.Module == module;
        }
    }

    private static Panel BuildBrand()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = Metrics.TopBarHeight,
            BackColor = Palette.SidebarBackground,
        };

        panel.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Render.UseQualityRendering(g);

            var mark = new Rectangle(Metrics.Space4, (panel.Height - 34) / 2, 34, 34);
            Render.FillGradientRounded(
                g,
                mark,
                Metrics.RadiusMd,
                Palette.Indigo,
                Render.GradientEnd(Palette.Indigo));
            Icons.Draw(g, Icons.Pos, mark, Color.White, 13f);

            if (panel.Width < Metrics.SidebarWidth - 20)
            {
                return;
            }

            var nameRect = new Rectangle(mark.Right + Metrics.Space3, 10, panel.Width - mark.Right - 60, 20);
            TextRenderer.DrawText(
                g,
                "TechShop",
                Typography.Get(Typography.SizeMd, FontStyle.Bold),
                nameRect,
                Palette.InkStrong,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            var tagRect = new Rectangle(mark.Right + Metrics.Space3, 29, panel.Width - mark.Right - 60, 16);
            TextRenderer.DrawText(
                g,
                "Shop operations",
                Typography.Get(Typography.Size2Xs),
                tagRect,
                Palette.InkMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            using var pen = new Pen(Palette.BorderSubtle);
            g.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
        };

        panel.Resize += (_, _) =>
        {
            var toggle = panel.Controls.OfType<AppButton>().FirstOrDefault();
            if (toggle is not null)
            {
                toggle.Location = new Point(panel.Width - toggle.Width - Metrics.Space3, (panel.Height - toggle.Height) / 2);
                toggle.Visible = panel.Width >= Metrics.SidebarWidth - 20;
            }
        };

        return panel;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // A single hairline separates the rail from the content, in place of a shadow.
        using var pen = new Pen(Palette.Border);
        e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }
}
