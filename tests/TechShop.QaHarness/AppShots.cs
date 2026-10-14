using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Sales;
using TechShop.Domain.Enums;
using TechShop.WinForms;
using TechShop.WinForms.Features.Pos;
using TechShop.WinForms.Features.Sales;
using TechShop.WinForms.Shell;

namespace TechShop.QaHarness;

/// <summary>
/// Captures the application as a person actually sees it: the sign-in screen, then the
/// whole shell — rail, top bar, page and footer — for every menu entry, then the
/// dialogs worth showing.
///
/// Deliberately not the page on its own. A page rendered without its shell is useful
/// for spotting a layout bug and useless as a picture of the product, because it is
/// missing the navigation, the shop picker and the identity that give it context.
///
/// Every step here is synchronous. The harness never calls Application.Run, so there is
/// no synchronisation context to carry a continuation back to the UI thread: awaiting
/// anything resumes on a thread-pool thread, which is not STA, and the next Krypton
/// control to be constructed throws.
/// </summary>
internal static class AppShots
{
    /// <summary>
    /// A 16:10 window large enough to show the shell without crowding. Rendering is
    /// off-screen, so this does not have to fit the machine running the capture.
    /// </summary>
    private static readonly Size Window = new(1440, 900);

    public static void Run(Harness harness, string directory)
    {
        harness.Section("application screenshots");

        Directory.CreateDirectory(directory);

        // The sign-in screen is a fixed-size dialog, so it keeps its own dimensions.
        // Stretched to the shell's size it is mostly empty space.
        Capture(harness, directory, "01-login", () => new LoginForm(), size: null);

        using var shell = new MainForm
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-6000, -6000),
            ShowInTaskbar = false,
            WindowState = FormWindowState.Normal,
            ClientSize = Window,
        };

        shell.Show();

        // The shell fills itself from an async Load handler, so a shot taken before
        // that finishes is of an empty window.
        if (!PumpUntil(() => shell.ReadyForTest))
        {
            harness.Fail("the shell loads", "the profile never arrived");
            return;
        }

        harness.Pass("the shell loads");

        // The shop with the most on the shelf. Earlier tests create an empty branch
        // that sorts first alphabetically, and a till screenshot where every product
        // reads "Out" tells the reader nothing about the product.
        var tradingShopId = AppHost
            .InScopeAsync<Application.Features.Stock.StockService, long?>(async stock =>
            {
                var levels = await stock
                    .QueryLevelsAsync(new QueryRequest
                    {
                        PageSize = 400,
                        Sort = [new SortRule { Field = "qty", Desc = true }],
                    })
                    .ConfigureAwait(false);

                return levels.Items
                    .GroupBy(l => l.ShopId)
                    .OrderByDescending(g => g.Sum(l => l.Qty))
                    .Select(g => (long?)g.Key)
                    .FirstOrDefault();
            })
            .GetAwaiter().GetResult();

        var order = 2;

        foreach (var entry in shell.NavigableForTest)
        {
            shell.NavigateForTest(entry.Module);
            Settle();

            // The till refuses to sell from "all shops", and a cart with something in
            // it is the only screenshot of a till worth having.
            if (entry.Module == Module.Pos && tradingShopId is { } shopId)
            {
                shell.UseShopForTest(shopId).GetAwaiter().GetResult();
                Settle();

                if (shell.CurrentPageForTest is PosPage till)
                {
                    till.SearchForTestAsync(string.Empty).GetAwaiter().GetResult();
                    till.LoadMixedBasketForTest();
                }

                Settle();
            }

            Save(harness, shell, directory, $"{order:D2}-{Slug(entry.Label)}");
            order++;

            if (entry.Module == Module.Pos)
            {
                shell.UseShopForTest(null).GetAwaiter().GetResult();
            }
        }

        CaptureDialogs(harness, directory, order);

        shell.Close();
    }

    private static void CaptureDialogs(Harness harness, string directory, int order)
    {
        using var scope = AppHost.CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();

        var listed = sales.QueryAsync(new QueryRequest
        {
            PageSize = 40,
            Sort = [new SortRule { Field = "invoiceDate", Desc = true }],
        }).GetAwaiter().GetResult();

        // One with tax on some lines and not others shows both formats at their most
        // interesting.
        var chosen = listed.Items.FirstOrDefault(i => i.TaxTotal > 0m && i.ItemCount > 1)
            ?? (listed.Items.Count > 0 ? listed.Items[0] : null);

        if (chosen is null)
        {
            return;
        }

        var invoice = sales.GetAsync(chosen.Id).GetAwaiter().GetResult();

        Capture(
            harness,
            directory,
            $"{order:D2}-invoice-a4",
            () => new InvoiceDetailDialog(invoice),
            new Size(780, 660));

        Capture(
            harness,
            directory,
            $"{order + 1:D2}-invoice-receipt-80mm",
            () => new InvoiceDetailDialog(invoice, InvoiceFormat.Receipt80),
            new Size(420, 660));
    }

    private static void Capture(Harness harness, string directory, string name, Func<Form> build, Size? size)
    {
        Form? form = null;

        try
        {
            form = build();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-6000, -6000);
            form.ShowInTaskbar = false;

            if (size is { } wanted)
            {
                form.ClientSize = wanted;
            }

            form.Show();

            Settle();
            Save(harness, form, directory, name);
            form.Close();
        }
        catch (Exception ex)
        {
            harness.Fail($"{name}: captured", ex.Message);
        }
        finally
        {
            form?.Dispose();
        }
    }

    private static void Save(Harness harness, Form form, string directory, string name)
    {
        try
        {
            // The whole window, chrome included: DrawToBitmap paints the title bar, so
            // a client-sized bitmap crops that many pixels off the bottom.
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

            bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
            harness.Pass($"{name}.png", $"{bitmap.Width}x{bitmap.Height}");
        }
        catch (Exception ex)
        {
            harness.Fail($"{name}.png", ex.Message);
        }
    }

    private static bool PumpUntil(Func<bool> ready, int attempts = 80)
    {
        for (var i = 0; i < attempts; i++)
        {
            System.Windows.Forms.Application.DoEvents();

            if (ready())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return ready();
    }

    /// <summary>Lets queued work and repaints finish before the shutter.</summary>
    private static void Settle()
    {
        for (var i = 0; i < 8; i++)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(35);
        }
    }

    private static string Slug(string label)
        => label.Replace(' ', '-').Replace("/", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
