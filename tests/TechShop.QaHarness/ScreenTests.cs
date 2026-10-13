using System.Drawing;
using TechShop.Domain.Enums;
using TechShop.WinForms;
using TechShop.WinForms.Shell;

namespace TechShop.QaHarness;

/// <summary>
/// Builds every screen the shell can route to, loads it against real data, and forces a
/// paint. A screen that compiles but throws when painted is the failure mode this catches.
/// </summary>
internal static class ScreenTests
{
    /// <summary>
    /// Two window sizes: a desktop, and a 1366x768 laptop minus the shell's chrome,
    /// which is the smallest screen this app is likely to meet on a shop counter. Most
    /// layout bugs only show at the small one.
    /// </summary>
    private static readonly (string Name, Size Size)[] Viewports =
    [
        ("desktop", new Size(1400, 860)),
        ("laptop", new Size(1120, 620)),
    ];

    /// <summary>
    /// When set, each screen's rendered bitmap is written here. Passing --shots &lt;dir&gt;
    /// turns it on, which is how the UI gets reviewed without a person sitting in front
    /// of the running app.
    /// </summary>
    public static string? ShotDirectory { get; set; }

    public static void Run(Harness harness)
    {
        foreach (var (name, size) in Viewports)
        {
            harness.Section($"screens load and paint ({name} {size.Width}x{size.Height})");

            foreach (var entry in NavRegistry.All)
            {
                Build(harness, entry.Module, entry.Label, size);
            }
        }
    }

    private static void Build(Harness harness, Module module, string label, Size viewport)
    {
        PageBase? page = null;

        try
        {
            page = PageFactory.Create(module);

            // A page is normally hosted in the shell, which gives it a size and a parent.
            //
            // The host is actually shown, off the side of the desktop. Some containers —
            // TabControl in particular — do not lay their children out until the window
            // is realised, so measuring an unshown form reports sizes that never occur
            // in the real app.
            using var host = new Form
            {
                ClientSize = viewport,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-6000, -6000),
                ShowInTaskbar = false,
            };

            host.Controls.Add(page);
            host.Show();

            // The till refuses to sell from "all shops", so it is rendered with one
            // selected — otherwise its screenshot only ever shows the empty state.
            if (module == Module.Pos)
            {
                var session = AppHost.Get<Infrastructure.Services.UserSession>();
                session.ActiveShopId = session.AllowedShopIds.Count > 0 ? session.AllowedShopIds[0] : null;
            }

            // The shell calls this straight after adding the page; it loads the grid.
            page.InitialiseAsync(canWrite: true).GetAwaiter().GetResult();

            // Let any continuation posted back to the UI thread actually run.
            System.Windows.Forms.Application.DoEvents();
            host.PerformLayout();
            System.Windows.Forms.Application.DoEvents();

            using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
            host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.ClientSize));

            if (ShotDirectory is not null)
            {
                Directory.CreateDirectory(ShotDirectory);
                var name = label.Replace(' ', '-').ToLowerInvariant();
                bitmap.Save(
                    Path.Combine(ShotDirectory, $"{name}-{viewport.Width}x{viewport.Height}.png"),
                    System.Drawing.Imaging.ImageFormat.Png);
            }

            // A placeholder paints perfectly well and proves nothing, so it is reported
            // as unbuilt rather than quietly counted as a passing screen.
            if (page is PlaceholderPage)
            {
                harness.Fail($"{label} is implemented", "this module is still a placeholder");
            }
            else
            {
                harness.Pass($"{label} loads and paints");
                CheckLayout(harness, label, page, host.ClientSize);
            }
        }
        catch (Exception ex)
        {
            harness.Fail($"{label} loads and paints", Root(ex));
        }
        finally
        {
            page?.Dispose();
        }
    }

    /// <summary>
    /// Checks the screen is actually laid out, not merely free of exceptions.
    ///
    /// "It painted something" passed every screen while every list grid was rendering as
    /// a 200x100 box in the corner of its card, because the page header and the card
    /// border were enough to make the bitmap non-uniform. These assertions are what that
    /// bug should have tripped.
    /// </summary>
    private static void CheckLayout(Harness harness, string label, Control page, Size viewport)
    {
        var grids = Descendants(page).OfType<DataGridView>().ToList();

        foreach (var grid in grids)
        {
            // A grid that is not docked keeps its default size. Any real grid on a
            // 1400px page is far wider than this.
            harness.Expect(
                $"{label}: grid is sized",
                grid.Width >= 400 && grid.Height >= 150,
                $"a grid is {grid.Width}x{grid.Height}. Ancestors: {Chain(grid)}");
        }

        // Nothing should need the page itself to scroll: headers and filters dock to the
        // top, the grid fills the rest and scrolls internally.
        if (page is ScrollableControl scrollable && scrollable.AutoScroll)
        {
            harness.Expect(
                $"{label}: fits without page scrolling",
                scrollable.VerticalScroll.Visible == false,
                "the page itself scrolls, which pushes the header off the top");
        }

        // A child sticking out past the page is a layout mistake, not a design choice.
        var overflowing = page.Controls
            .OfType<Control>()
            .Where(c => c.Visible && (c.Right > viewport.Width + 2 || c.Bottom > viewport.Height + 2))
            .Select(c => $"{c.GetType().Name} to {c.Right}x{c.Bottom}")
            .ToList();

        harness.Expect(
            $"{label}: content stays inside the window",
            overflowing.Count == 0,
            $"overflows {viewport.Width}x{viewport.Height}: {string.Join(", ", overflowing)}");
    }

    /// <summary>
    /// The chain of parents with each one's size and dock, so a squeezed control says
    /// which ancestor squeezed it rather than leaving it to be guessed.
    /// </summary>
    private static string Chain(Control control)
    {
        var parts = new List<string>();

        for (var c = control.Parent; c is not null; c = c.Parent)
        {
            parts.Add($"{c.GetType().Name}[{c.Dock}] {c.Width}x{c.Height}");
        }

        return string.Join(" < ", parts);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static string Root(Exception ex)
    {
        var root = ex;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
        }

        var frame = root.StackTrace?
            .Split('\n')
            .FirstOrDefault(l => l.Contains("TechShop", StringComparison.Ordinal))
            ?.Trim();

        return $"{root.GetType().Name}: {root.Message}{(frame is null ? "" : $"  at {frame}")}";
    }
}
