using System.Diagnostics;
using System.Drawing;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// The application footer, shown on the sign-in screen and under every page in the shell.
/// The site and the address are links; everything else is quiet, because a footer that
/// competes with the page it sits under is a footer in the wrong place.
/// </summary>
public sealed class AppFooter : Panel
{
    // Named SiteUrl rather than Site: Control already has a Site property.
    private const string SiteUrl = "http://masimaziz.com";
    private const string Email = "asimhashmi47@gmail.com";
    private const string Notice = "Copyright reserved 2026";

    private readonly LinkLabel _site;
    private readonly LinkLabel _email;
    private readonly Label _notice;
    private readonly Label _firstSeparator;
    private readonly Label _secondSeparator;

    public AppFooter()
    {
        // Built before Dock or Height are touched: either of those raises a layout pass,
        // and OnLayout below reads these fields.
        _notice = Caption(Notice);
        _firstSeparator = Caption("|");
        _site = Link(SiteUrl, SiteUrl);
        _secondSeparator = Caption("|");
        _email = Link(Email, "mailto:" + Email);

        Controls.AddRange([_notice, _firstSeparator, _site, _secondSeparator, _email]);

        BackColor = Palette.SurfaceSunken;
        Height = 34;
        Dock = DockStyle.Bottom;
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Palette.InkMuted,
        Font = Typography.Get(Typography.SizeXs),
        BackColor = Color.Transparent,
    };

    private static LinkLabel Link(string text, string target)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            Font = Typography.Get(Typography.SizeXs),
            LinkColor = Palette.PrimaryText,
            ActiveLinkColor = Palette.PrimaryActive,
            VisitedLinkColor = Palette.PrimaryText,
            LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = Color.Transparent,
        };

        link.LinkClicked += (_, _) =>
        {
            try
            {
                // UseShellExecute hands the target to the default browser or mail client
                // rather than trying to run it as a process.
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                // No handler registered for http or mailto. Not worth interrupting the
                // operator over a footer link.
            }
        };

        return link;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        var parts = new Control[] { _notice, _firstSeparator, _site, _secondSeparator, _email };
        var total = parts.Sum(p => p.PreferredSize.Width) + (Metrics.Space2 * (parts.Length - 1));
        var x = Math.Max(Metrics.Space4, (Width - total) / 2);

        foreach (var part in parts)
        {
            part.Location = new Point(x, (Height - part.PreferredSize.Height) / 2);
            x += part.PreferredSize.Width + Metrics.Space2;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Palette.BorderSubtle);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }
}
