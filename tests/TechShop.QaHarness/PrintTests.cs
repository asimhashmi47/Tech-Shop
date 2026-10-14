using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Sales;
using TechShop.Domain.Enums;
using TechShop.WinForms;
using TechShop.WinForms.Features.Sales;

namespace TechShop.QaHarness;

/// <summary>
/// Checks the invoice prints in both formats.
///
/// A receipt and an A4 page are different documents, not the same one at two sizes: the
/// receipt stacks each item over its own arithmetic because three columns will not fit
/// across 80mm. Both are rendered here and measured, because a layout that runs off the
/// paper only shows up when someone prints it.
/// </summary>
internal static class PrintTests
{
    public static async Task RunAsync(Harness harness, string? shotDirectory)
    {
        harness.Section("invoice printing");

        using var scope = AppHost.CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesService>();

        var listed = await sales.QueryAsync(new QueryRequest
        {
            PageSize = 60,
            Sort = [new SortRule { Field = "invoiceDate", Desc = true }],
        }).ConfigureAwait(true);

        // An invoice with tax on some lines and not others exercises both layouts
        // hardest: the dash, the per-line tax row, and the tax total.
        var chosen = listed.Items.FirstOrDefault(i => i.TaxTotal > 0m && i.ItemCount > 1)
            ?? (listed.Items.Count > 0 ? listed.Items[0] : null);

        if (chosen is null)
        {
            harness.Fail("an invoice exists to print", "no invoices at all");
            return;
        }

        var invoice = await sales.GetAsync(chosen.Id).ConfigureAwait(true);

        foreach (var format in new[] { InvoiceFormat.A4, InvoiceFormat.Receipt80 })
        {
            Check(harness, invoice, format, shotDirectory);
        }
    }

    private static void Check(Harness harness, InvoiceDetailDto invoice, InvoiceFormat format, string? shotDirectory)
    {
        var name = format == InvoiceFormat.Receipt80 ? "80mm receipt" : "A4 page";

        try
        {
            // Sized before it is shown. Resizing a shown dialog and then capturing it
            // leaves the previous, wider layout behind in the bitmap.
            var width = format == InvoiceFormat.Receipt80 ? 380 : 780;

            using var dialog = new InvoiceDetailDialog(invoice, format);
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-6000, -6000);
            dialog.ShowInTaskbar = false;
            dialog.ClientSize = new Size(width, 900);
            dialog.Show();
            System.Windows.Forms.Application.DoEvents();

            harness.Expect($"{name}: the dialog opens in the chosen format",
                dialog.FormatForTest == format,
                $"asked for {format}, got {dialog.FormatForTest}");

            var height = dialog.MeasureForTest(format);

            harness.Expect($"{name}: the document has content",
                height > 120,
                $"measured only {height}px tall, which cannot hold an invoice");

            if (format == InvoiceFormat.Receipt80)
            {
                // Every line of a receipt has to fit the roll. Nothing here may rely on
                // the width of an A4 page.
                harness.Expect("80mm receipt: fits a till roll",
                    InvoiceRenderer.Receipt80Width <= 300,
                    $"the roll layout is {InvoiceRenderer.Receipt80Width}px wide");

                // A tall receipt is fine; an absurd one means the measure pass and the
                // draw pass disagree, which is how the last line gets cut off.
                harness.Expect("80mm receipt: height is proportionate to the item count",
                    height < 400 + (invoice.Items.Count * 120),
                    $"{invoice.Items.Count} items produced {height}px");
            }

            using var bitmap = new Bitmap(dialog.ClientSize.Width, dialog.ClientSize.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

            harness.Pass($"{name}: renders");

            if (shotDirectory is not null)
            {
                Directory.CreateDirectory(shotDirectory);
                var file = format == InvoiceFormat.Receipt80 ? "invoice-receipt-80mm" : "invoice-a4";
                bitmap.Save(Path.Combine(shotDirectory, file + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            dialog.Close();
        }
        catch (Exception ex)
        {
            harness.Fail($"{name}: renders", ex.ToString());
        }
    }
}
