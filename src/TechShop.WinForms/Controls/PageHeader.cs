using System.Drawing;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// The title block at the top of a screen: what this is, what it is currently showing,
/// and the actions that apply to the whole screen.
///
/// The subtitle carries the scope — "All shops, 12 Sept to 11 Oct 2026, 30 days" — so an
/// operator reading a figure can see what it covers without looking at the filters.
/// </summary>
public sealed class PageHeader : Panel
{
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly FlowLayoutPanel _actions;

    public PageHeader(string title, string subtitle = "")
    {
        _title = new Label
        {
            Text = title,
            AutoSize = true,
            Font = Typography.H1,
            ForeColor = Palette.InkStrong,
            Location = new Point(0, 0),
            BackColor = Color.Transparent,
        };

        _subtitle = new Label
        {
            Text = subtitle,
            AutoSize = true,
            Font = Typography.Get(Typography.SizeBase),
            ForeColor = Palette.InkMuted,
            Location = new Point(2, 44),
            BackColor = Color.Transparent,
        };

        _actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.Transparent,
        };

        Controls.Add(_title);
        Controls.Add(_subtitle);
        Controls.Add(_actions);

        // Set last: assigning Height raises a resize, and OnResize reads _actions.
        BackColor = Color.Transparent;
        Height = 72;
        Dock = DockStyle.Top;
    }

    public string Subtitle
    {
        get => _subtitle.Text;
        set => _subtitle.Text = value;
    }

    /// <summary>Adds a screen-level action. The first one added sits furthest right.</summary>
    public PageHeader AddAction(AppButton button)
    {
        button.Margin = new Padding(Metrics.Space2, 0, 0, 0);
        _actions.Controls.Add(button);
        return this;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        _actions.Location = new Point(Width - _actions.PreferredSize.Width, 6);
    }
}
