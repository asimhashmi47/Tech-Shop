using System.Drawing;
using System.Drawing.Printing;
using TechShop.Application.Features.Sales;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Sales;

/// <summary>
/// One invoice, laid out the way it prints, in whichever format is selected.
///
/// The counter prints an 80mm till roll and the office prints A4, so both are offered
/// and the preview switches with the choice. What is on screen is what the printer gets:
/// the same renderer draws both, so the preview cannot quietly disagree with the paper.
/// </summary>
public sealed class InvoiceDetailDialog : Form
{
    private readonly InvoiceDetailDto _invoice;
    private readonly Panel _sheet;
    private readonly AppButton _a4;
    private readonly AppButton _receipt;

    private InvoiceFormat _format;

    public InvoiceDetailDialog(InvoiceDetailDto invoice, InvoiceFormat format = InvoiceFormat.A4)
    {
        _invoice = invoice;
        _format = format;

        Text = invoice.InvoiceNo;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 760);
        MinimumSize = new Size(560, 540);
        BackColor = Palette.AppBackground;
        Font = Typography.Body;
        ShowIcon = false;
        MinimizeBox = false;

        _sheet = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            AutoScroll = true,
            Padding = new Padding(Metrics.Space6),
        };
        _sheet.Paint += PaintSheet;
        _sheet.Resize += (_, _) => ResizeSheet();

        // A segmented choice rather than two print buttons: the operator picks the
        // paper, checks it, then prints — instead of printing and finding out.
        _a4 = Segment("A4 page", InvoiceFormat.A4);
        _receipt = Segment("80mm receipt", InvoiceFormat.Receipt80);

        var chooser = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Palette.Surface };
        var chooserLabel = Theme.FieldLabel("Paper");
        chooserLabel.Location = new Point(Metrics.Space6, Metrics.Space3);
        chooser.Controls.AddRange([chooserLabel, _a4, _receipt]);
        chooser.Resize += (_, _) =>
        {
            _a4.Location = new Point(Metrics.Space6 + 56, Metrics.Space2);
            _receipt.Location = new Point(_a4.Right + Metrics.Space2, Metrics.Space2);
        };
        chooser.Paint += (_, e) =>
        {
            using var pen = new Pen(Palette.BorderSubtle);
            e.Graphics.DrawLine(pen, 0, chooser.Height - 1, chooser.Width, chooser.Height - 1);
        };

        var print = new AppButton { Variant = ButtonVariant.Primary, Text = "Print", Width = 110 };
        print.Click += (_, _) => Print();

        var close = new AppButton { Variant = ButtonVariant.Secondary, Text = "Close", Width = 100 };
        close.Click += (_, _) => Close();

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Palette.SurfaceSunken };
        footer.Controls.AddRange([print, close]);
        footer.Resize += (_, _) =>
        {
            var y = (footer.Height - print.Height) / 2;
            print.Location = new Point(footer.Width - print.Width - Metrics.Space5, y);
            close.Location = new Point(print.Left - close.Width - Metrics.Space2, y);
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Palette.BorderSubtle);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        Controls.Add(_sheet);
        Controls.Add(chooser);
        Controls.Add(footer);

        CancelButton = close;
        ApplyFormat(_format);
    }

    private AppButton Segment(string text, InvoiceFormat format)
    {
        var button = new AppButton { Variant = ButtonVariant.Secondary, Text = text, Height = Metrics.ControlHeightSmall };
        button.FitToText(Metrics.Space3);
        button.Click += (_, _) => ApplyFormat(format);
        return button;
    }

    private void ApplyFormat(InvoiceFormat format)
    {
        _format = format;

        _a4.Variant = format == InvoiceFormat.A4 ? ButtonVariant.Primary : ButtonVariant.Secondary;
        _receipt.Variant = format == InvoiceFormat.Receipt80 ? ButtonVariant.Primary : ButtonVariant.Secondary;

        _sheet.AutoScrollPosition = Point.Empty;
        ResizeSheet();
        _sheet.Invalidate();
    }

    private void PaintSheet(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var scroll = _sheet.AutoScrollPosition;

        var available = _sheet.ClientSize.Width - _sheet.Padding.Horizontal;
        var width = _format == InvoiceFormat.Receipt80
            ? Math.Min(InvoiceRenderer.Receipt80Width, available)
            : available;

        // A receipt is narrower than the dialog, so it is centred on the sheet with the
        // paper edges drawn — otherwise it reads as a broken layout rather than a roll.
        var x = _sheet.Padding.Left + ((available - width) / 2) + scroll.X;
        var y = _sheet.Padding.Top + scroll.Y;

        if (_format == InvoiceFormat.Receipt80)
        {
            var height = InvoiceRenderer.Measure(_invoice, _format, width);
            var paper = new Rectangle(x - Metrics.Space3, y - Metrics.Space3, width + (Metrics.Space3 * 2), height + (Metrics.Space3 * 2));

            Render.FillRounded(g, paper, Metrics.RadiusSm, Palette.Surface);
            Render.DrawRoundedBorder(g, paper, Metrics.RadiusSm, Palette.Border);
        }

        InvoiceRenderer.Draw(g, _invoice, _format, x, y, width);
    }

    /// <summary>
    /// Works out how tall the scrollable area has to be.
    ///
    /// Deliberately not done inside the paint handler: assigning AutoScrollMinSize
    /// triggers a layout, the layout triggers another paint, and the document ends up
    /// drawn twice at two scroll offsets, overlapping itself.
    /// </summary>
    private void ResizeSheet()
    {
        if (_sheet.ClientSize.Width <= 0)
        {
            return;
        }

        var available = _sheet.ClientSize.Width - _sheet.Padding.Horizontal;
        var width = _format == InvoiceFormat.Receipt80
            ? Math.Min(InvoiceRenderer.Receipt80Width, available)
            : available;

        var height = InvoiceRenderer.Measure(_invoice, _format, width);
        var wanted = new Size(0, height + _sheet.Padding.Vertical + Metrics.Space6);

        if (_sheet.AutoScrollMinSize != wanted)
        {
            _sheet.AutoScrollMinSize = wanted;
        }

        // The document is centred, so widening the panel moves it. Without a full
        // invalidate WinForms repaints only the newly exposed strip and the previous
        // drawing stays behind, leaving the invoice rendered twice side by side.
        _sheet.Invalidate();
    }

    private void Print()
    {
        using var document = new PrintDocument { DocumentName = _invoice.InvoiceNo };

        if (_format == InvoiceFormat.Receipt80)
        {
            // A till roll is continuous, so the page is sized to the content. Left on a
            // fixed page height the printer feeds, and then cuts, a strip of blank paper
            // after every sale.
            var height = InvoiceRenderer.Measure(_invoice, InvoiceFormat.Receipt80, InvoiceRenderer.Receipt80Width);

            // Graphics units are 1/96in on screen and 1/100in on the page.
            var paperHeight = (int)Math.Ceiling(height * 100d / 96d) + 20;

            document.DefaultPageSettings.PaperSize =
                new PaperSize("Receipt 80mm", InvoiceRenderer.Receipt80PaperWidth, paperHeight);
            document.DefaultPageSettings.Margins = new Margins(10, 10, 10, 10);
            document.OriginAtMargins = true;
        }
        else
        {
            document.DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169);
            document.DefaultPageSettings.Margins = new Margins(60, 60, 60, 60);
        }

        var format = _format;

        document.PrintPage += (_, e) =>
        {
            if (e.Graphics is null)
            {
                return;
            }

            var bounds = format == InvoiceFormat.Receipt80
                ? new Rectangle(0, 0, InvoiceRenderer.Receipt80Width, int.MaxValue)
                : e.MarginBounds;

            InvoiceRenderer.Draw(e.Graphics, _invoice, format, bounds.X, bounds.Y, bounds.Width);
        };

        using var preview = new PrintPreviewDialog
        {
            Document = document,
            StartPosition = FormStartPosition.CenterParent,
            Width = format == InvoiceFormat.Receipt80 ? 560 : 820,
            Height = 900,
        };

        // Preview rather than straight to the printer: a shop has one printer and
        // usually the wrong paper in it.
        preview.ShowDialog(this);
    }

    // ---------------------------------------------------------------- test seams

    internal InvoiceFormat FormatForTest => _format;

    internal void UseFormatForTest(InvoiceFormat format) => ApplyFormat(format);

    /// <summary>The height the chosen format needs, so the QA harness can check it is sane.</summary>
    internal int MeasureForTest(InvoiceFormat format)
    {
        var width = format == InvoiceFormat.Receipt80
            ? InvoiceRenderer.Receipt80Width
            : _sheet.ClientSize.Width - _sheet.Padding.Horizontal;

        return InvoiceRenderer.Measure(_invoice, format, width);
    }
}
