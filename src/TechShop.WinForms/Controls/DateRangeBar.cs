using System.Drawing;
using Krypton.Toolkit;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// The From/To pair with the presets the dashboard and the reports share.
///
/// Presets are on the right rather than hidden in a dropdown, because "last 30 days" is
/// what an operator wants nine times out of ten and it should cost one click.
/// </summary>
public sealed class DateRangeBar : Panel
{
    private readonly KryptonDateTimePicker _from;
    private readonly KryptonDateTimePicker _to;
    private readonly AppButton _apply;
    private readonly FlowLayoutPanel _presets;
    private readonly List<(AppButton Button, int Days)> _presetButtons = [];
    private int _activePreset = 30;

    public DateRangeBar()
    {
        var fromLabel = Theme.FieldLabel("From");
        fromLabel.Location = new Point(Metrics.Space4, Metrics.Space2);

        _from = Theme.DatePicker();
        _from.Location = new Point(Metrics.Space4, 24);
        _from.Width = 150;

        var toLabel = Theme.FieldLabel("To");
        toLabel.Location = new Point(190, Metrics.Space2);

        _to = Theme.DatePicker();
        _to.Location = new Point(190, 24);
        _to.Width = 150;

        _apply = new AppButton { Variant = ButtonVariant.Primary, Text = "Apply", Width = 84 };
        _apply.Location = new Point(352, 24);
        _apply.Click += (_, _) =>
        {
            _activePreset = 0;
            PaintPresets();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        _presets = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
        };

        foreach (var (label, days) in new[] { ("7 days", 7), ("30 days", 30), ("90 days", 90), ("12 months", 365) })
        {
            var button = new AppButton
            {
                Variant = ButtonVariant.Ghost,
                Text = label,
                Height = Metrics.ControlHeightSmall,
                CornerRadius = Metrics.ControlHeightSmall / 2,
            };
            button.FitToText(Metrics.Space3);
            button.Margin = new Padding(Metrics.Space1, 0, 0, 0);

            var captured = days;
            button.Click += (_, _) => Apply(captured);

            _presetButtons.Add((button, days));
            _presets.Controls.Add(button);
        }

        Controls.AddRange([fromLabel, _from, toLabel, _to, _apply, _presets]);

        BackColor = Palette.Surface;
        Height = 72;
        Dock = DockStyle.Top;

        Apply(30, raise: false);
    }

    public event EventHandler? Changed;

    /// <summary>Inclusive start, in UTC, as the services expect.</summary>
    public DateTime FromUtc => Format.ToUtc(_from.Value.Date);

    /// <summary>Inclusive end, in UTC.</summary>
    public DateTime ToUtc => Format.ToUtc(_to.Value.Date);

    public int Days => Math.Max(1, (int)(_to.Value.Date - _from.Value.Date).TotalDays + 1);

    /// <summary>The line the page header shows, so a figure always states what it covers.</summary>
    public string Describe(string scope)
        => $"{scope}, {Format.Date(FromUtc)} to {Format.Date(ToUtc)}, {Days} days";

    private void Apply(int days, bool raise = true)
    {
        _activePreset = days;
        var today = DateTime.Today;
        _to.Value = today;
        _from.Value = days >= 365 ? today.AddMonths(-12).AddDays(1) : today.AddDays(-(days - 1));

        PaintPresets();

        if (raise)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PaintPresets()
    {
        foreach (var (button, days) in _presetButtons)
        {
            // The chosen preset reads as selected; a custom range deselects all of them,
            // which is the honest state once the operator has typed their own dates.
            button.Variant = days == _activePreset ? ButtonVariant.Primary : ButtonVariant.Ghost;
        }
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        _presets.Location = new Point(
            Math.Max(_apply.Right + Metrics.Space4, Width - _presets.PreferredSize.Width - Metrics.Space4),
            24);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Render.UseQualityRendering(g);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Render.FillRounded(g, bounds, Metrics.RadiusLg, Palette.Surface);
        Render.DrawRoundedBorder(g, bounds, Metrics.RadiusLg, Palette.BorderSubtle);
    }
}
