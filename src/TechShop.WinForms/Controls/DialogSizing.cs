using System.Drawing;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// Keeps a dialog inside the screen it opens on.
///
/// A dialog sized for the developer's monitor is a dialog whose buttons are below the
/// bottom edge on a shop's laptop. A 1366x768 screen has roughly 720 usable pixels once
/// the taskbar has taken its share, so anything asking for more loses its footer — and
/// the footer is where Save, Print and Close live.
///
/// Applied on Load rather than in a constructor, because until the window has an owner
/// there is no way to know which screen it will appear on.
/// </summary>
public static class DialogSizing
{
    /// <summary>Breathing room left around the dialog so it does not sit edge to edge.</summary>
    private const int Margin = Metrics.Space10;

    public static void KeepOnScreen(this Form form)
        => form.Load += (_, _) => Fit(form);

    private static void Fit(Form form)
    {
        var area = (form.Owner is not null ? Screen.FromControl(form.Owner) : Screen.FromControl(form)).WorkingArea;

        var maxWidth = area.Width - Margin;
        var maxHeight = area.Height - Margin;

        // A minimum larger than the screen would defeat the clamp below, so it is
        // lowered first. A dialog that cannot fit is better cramped than cut off.
        if (form.MinimumSize.Width > maxWidth || form.MinimumSize.Height > maxHeight)
        {
            form.MinimumSize = new Size(
                Math.Min(form.MinimumSize.Width, maxWidth),
                Math.Min(form.MinimumSize.Height, maxHeight));
        }

        var width = Math.Min(form.Width, maxWidth);
        var height = Math.Min(form.Height, maxHeight);

        if (width != form.Width || height != form.Height)
        {
            form.Size = new Size(width, height);
        }

        // Re-centre: shrinking a form leaves it where its original, larger centring put
        // it, which can still hang off the bottom.
        var x = area.Left + ((area.Width - form.Width) / 2);
        var y = area.Top + ((area.Height - form.Height) / 2);

        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
    }

    /// <summary>The largest client height a dialog can expect, for sizing defaults.</summary>
    public static int MaxClientHeight(IWin32Window? owner)
    {
        var area = owner is Control control
            ? Screen.FromControl(control).WorkingArea
            : Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 720);

        // Allow for the title bar and the border the form adds around its client area.
        return area.Height - Margin - 40;
    }
}
