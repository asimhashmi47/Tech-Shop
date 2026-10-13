using System.Drawing;
using System.Drawing.Printing;
using TechShop.Application.Features.Sales;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Sales;

/// <summary>
/// One invoice, laid out the way it prints: shop at the top, customer, the lines, then
/// the totals. The same layout is used on screen and on paper, so what the operator
/// checks is what the customer receives.
/// </summary>
public sealed class InvoiceDetailDialog : Form
{
    private readonly InvoiceDetailDto _invoice;

    public InvoiceDetailDialog(InvoiceDetailDto invoice)
    {
        _invoice = invoice;

        Text = invoice.InvoiceNo;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 720);
        MinimumSize = new Size(560, 520);
        BackColor = Palette.AppBackground;
        Font = Typography.Body;
        ShowIcon = false;
        MinimizeBox = false;

        var sheet = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            AutoScroll = true,
            Padding = new Padding(Metrics.Space6),
        };
        sheet.Paint += (_, e) => Draw(e.Graphics, sheet.Width, forPrint: false);

        var print = new AppButton { Variant = ButtonVariant.Primary, Text = "Print", Width = 110 };
        print.Click += (_, _) => Print();

        var close = new AppButton { Variant = ButtonVariant.Secondary, Text = "Close", Width = 100 };
        close.Click += (_, _) => Close();

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Palette.SurfaceSunken };
        footer.Controls.Add(print);
        footer.Controls.Add(close);
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

        Controls.Add(sheet);
        Controls.Add(footer);
        CancelButton = close;
    }

    private void Print()
    {
        using var document = new PrintDocument { DocumentName = _invoice.InvoiceNo };
        document.PrintPage += (_, e) =>
        {
            if (e.Graphics is not null)
            {
                Draw(e.Graphics, e.MarginBounds.Width + (Metrics.Space6 * 2), forPrint: true, e.MarginBounds.Left, e.MarginBounds.Top);
            }
        };

        using var preview = new PrintPreviewDialog
        {
            Document = document,
            StartPosition = FormStartPosition.CenterParent,
            Width = 820,
            Height = 900,
        };

        // Preview rather than straight to the printer: a shop has one printer and
        // usually the wrong paper in it.
        preview.ShowDialog(this);
    }

    /// <summary>
    /// Draws the invoice. The same routine serves the screen and the printer, which is
    /// why it takes an origin and a width rather than reading them off a control.
    /// </summary>
    private void Draw(Graphics g, int width, bool forPrint, int originX = 0, int originY = 0)
    {
        Render.UseQualityRendering(g);

        var left = originX + Metrics.Space6;
        var right = originX + width - Metrics.Space6;
        var y = originY + (forPrint ? 0 : Metrics.Space5);

        var ink = Palette.InkStrong;
        var muted = Palette.InkMuted;
        var body = Palette.InkBody;

        // ---- shop
        y = Line(g, _invoice.ShopName, Typography.H3, ink, left, y, right - left) + 2;

        if (!string.IsNullOrWhiteSpace(_invoice.ShopAddress))
        {
            y = Line(g, _invoice.ShopAddress, Typography.Get(Typography.SizeSm), muted, left, y, right - left);
        }

        if (!string.IsNullOrWhiteSpace(_invoice.ShopPhone))
        {
            y = Line(g, _invoice.ShopPhone, Typography.Get(Typography.SizeSm), muted, left, y, right - left);
        }

        y += Metrics.Space4;
        y = Rule(g, left, right, y);
        y += Metrics.Space3;

        // ---- invoice meta, two columns
        var metaTop = y;
        Label(g, "Invoice", _invoice.InvoiceNo, left, ref y, 240);
        Label(g, "Date", Format.DateTimeShort(_invoice.InvoiceDate), left, ref y, 240);
        Label(g, "Raised by", _invoice.CreatedBy, left, ref y, 240);

        var metaBottom = y;
        y = metaTop;
        var rightColumn = left + ((right - left) / 2);
        Label(g, "Customer", _invoice.CustomerName, rightColumn, ref y, right - rightColumn);

        if (!string.IsNullOrWhiteSpace(_invoice.CustomerPhone))
        {
            Label(g, "Phone", _invoice.CustomerPhone, rightColumn, ref y, right - rightColumn);
        }

        Label(g, "Payment", Format.Humanise(_invoice.PaymentMethod.ToString()), rightColumn, ref y, right - rightColumn);

        y = Math.Max(metaBottom, y) + Metrics.Space3;

        // A void invoice has to say so before anything else is read.
        if (_invoice.Status == InvoiceStatus.Void)
        {
            var banner = new Rectangle(left, y, right - left, 42);
            Render.FillRounded(g, banner, Metrics.RadiusMd, Palette.RoseSoft);
            TextRenderer.DrawText(
                g,
                $"VOID  —  {_invoice.VoidReason}",
                Typography.Get(Typography.SizeBase, FontStyle.Bold),
                banner,
                Palette.RoseText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            y = banner.Bottom + Metrics.Space3;
        }

        // ---- lines
        var qtyX = right - 400;
        var priceX = right - 330;
        var taxX = right - 220;
        var totalX = right - 120;

        var headerRow = new Rectangle(left, y, right - left, 28);
        Render.FillRounded(g, headerRow, Metrics.RadiusXs, Palette.SurfaceSunken);

        Cell(g, "Item", Typography.Get(Typography.Size2Xs, FontStyle.Bold), muted, left + 8, y, qtyX - left - 16, 28, false);
        Cell(g, "Qty", Typography.Get(Typography.Size2Xs, FontStyle.Bold), muted, qtyX, y, 60, 28, true);
        Cell(g, Format.MoneyHeader("Price"), Typography.Get(Typography.Size2Xs, FontStyle.Bold), muted, priceX, y, 100, 28, true);
        Cell(g, Format.MoneyHeader("Tax"), Typography.Get(Typography.Size2Xs, FontStyle.Bold), muted, taxX, y, 100, 28, true);
        Cell(g, Format.MoneyHeader("Total"), Typography.Get(Typography.Size2Xs, FontStyle.Bold), muted, totalX, y, 112, 28, true);
        y = headerRow.Bottom;

        foreach (var item in _invoice.Items)
        {
            var rowHeight = 30;
            Cell(g, $"{item.Sku}  {item.ProductName}", Typography.Get(Typography.SizeSm), body, left + 8, y, qtyX - left - 16, rowHeight, false);
            Cell(g, Format.Qty(item.Qty), Typography.Numeric(Typography.SizeSm), body, qtyX, y, 60, rowHeight, true);
            Cell(g, Format.Amount(item.UnitPrice), Typography.Numeric(Typography.SizeSm), body, priceX, y, 100, rowHeight, true);

            // A dash, not 0.00: an exempt line and a line that happened to round to
            // nothing are different things, and a customer may well ask which.
            Cell(
                g,
                item.TaxAmount == 0m ? "—" : Format.Amount(item.TaxAmount),
                Typography.Numeric(Typography.SizeSm),
                body,
                taxX,
                y,
                100,
                rowHeight,
                true);

            Cell(g, Format.Amount(item.LineTotal), Typography.Numeric(Typography.SizeSm), ink, totalX, y, 112, rowHeight, true);

            y += rowHeight;
            using var pen = new Pen(Palette.BorderSubtle);
            g.DrawLine(pen, left, y, right, y);
        }

        y += Metrics.Space4;

        // ---- totals
        Total(g, "Subtotal", _invoice.SubTotal, priceX, totalX, ref y, false);

        if (_invoice.Discount > 0)
        {
            Total(g, "Discount", -_invoice.Discount, priceX, totalX, ref y, false);
        }

        if (_invoice.TaxTotal > 0)
        {
            Total(g, "Sales tax", _invoice.TaxTotal, priceX, totalX, ref y, false);
        }

        Total(g, "Total", _invoice.Total, priceX, totalX, ref y, true);
        Total(g, "Paid", _invoice.PaidAmount, priceX, totalX, ref y, false);

        if (_invoice.Outstanding > 0)
        {
            Total(g, "Outstanding", _invoice.Outstanding, priceX, totalX, ref y, true, Palette.DangerText);
        }

        if (!string.IsNullOrWhiteSpace(_invoice.Note))
        {
            y += Metrics.Space4;
            y = Rule(g, left, right, y) + Metrics.Space2;
            Line(g, _invoice.Note, Typography.Get(Typography.SizeSm), muted, left, y, right - left);
        }
    }

    private static int Line(Graphics g, string text, Font font, Color colour, int x, int y, int width)
    {
        var size = TextRenderer.MeasureText(g, text, font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak);
        TextRenderer.DrawText(
            g,
            text,
            font,
            new Rectangle(x, y, width, size.Height),
            colour,
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return y + size.Height;
    }

    private static void Label(Graphics g, string caption, string value, int x, ref int y, int width)
    {
        TextRenderer.DrawText(
            g,
            caption,
            Typography.Get(Typography.Size2Xs, FontStyle.Bold),
            new Rectangle(x, y, width, 14),
            Palette.InkMuted,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            value,
            Typography.Get(Typography.SizeBase),
            new Rectangle(x, y + 14, width, 20),
            Palette.Ink,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        y += 38;
    }

    private static void Cell(
        Graphics g,
        string text,
        Font font,
        Color colour,
        int x,
        int y,
        int width,
        int height,
        bool rightAlign)
        => TextRenderer.DrawText(
            g,
            text,
            font,
            new Rectangle(x, y, width, height),
            colour,
            (rightAlign ? TextFormatFlags.Right : TextFormatFlags.Left)
            | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

    private static void Total(
        Graphics g,
        string label,
        decimal value,
        int labelX,
        int valueX,
        ref int y,
        bool strong,
        Color? colour = null)
    {
        var font = strong
            ? Typography.Get(Typography.SizeMd, FontStyle.Bold)
            : Typography.Get(Typography.SizeBase);

        var numeric = strong
            ? Typography.Numeric(Typography.SizeMd, FontStyle.Bold)
            : Typography.Numeric(Typography.SizeBase);

        TextRenderer.DrawText(
            g,
            label,
            font,
            new Rectangle(labelX - 60, y, 170, 26),
            colour ?? (strong ? Palette.InkStrong : Palette.InkMuted),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            Format.Amount(value),
            numeric,
            new Rectangle(valueX, y, 112, 26),
            colour ?? Palette.InkStrong,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        y += 26;
    }

    private static int Rule(Graphics g, int left, int right, int y)
    {
        using var pen = new Pen(Palette.Border);
        g.DrawLine(pen, left, y, right, y);
        return y + 1;
    }
}
