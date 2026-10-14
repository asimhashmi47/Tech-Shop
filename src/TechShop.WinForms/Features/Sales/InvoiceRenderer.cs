using System.Drawing;
using TechShop.Application.Features.Sales;
using TechShop.Domain.Enums;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Sales;

public enum InvoiceFormat
{
    /// <summary>A full page, for the office copy, the customer file or the accountant.</summary>
    A4,

    /// <summary>An 80mm till roll, which is what the counter actually prints.</summary>
    Receipt80,
}

/// <summary>
/// Draws an invoice, in either format, onto any <see cref="Graphics"/>.
///
/// One routine serves the on-screen preview and the printer, so the two cannot drift
/// apart: what the operator checks is exactly what comes out. Every method returns the
/// height it used, which is what lets a receipt size its own paper — a till roll is
/// continuous, and printing a fixed page height would feed blank paper after every sale.
/// </summary>
public static class InvoiceRenderer
{
    /// <summary>Printable width of an 80mm roll at 96dpi. The rest of the 80mm is feed margin.</summary>
    public const int Receipt80Width = 280;

    /// <summary>Paper width in hundredths of an inch, for a <see cref="System.Drawing.Printing.PaperSize"/>.</summary>
    public const int Receipt80PaperWidth = 315;

    public static int Draw(Graphics g, InvoiceDetailDto invoice, InvoiceFormat format, int x, int y, int width)
        => format == InvoiceFormat.Receipt80
            ? DrawReceipt(g, invoice, x, y, Math.Min(width, Receipt80Width))
            : DrawA4(g, invoice, x, y, width);

    /// <summary>The height the document needs, without putting it on screen.</summary>
    public static int Measure(InvoiceDetailDto invoice, InvoiceFormat format, int width)
    {
        // Measured by drawing onto a throwaway surface, so there is one definition of
        // the layout and no second measuring pass to drift out of step with it.
        //
        // It has to be a real off-screen bitmap. Clipping the caller's Graphics to an
        // empty region does not work: TextRenderer.DrawText goes to the device context
        // through GDI and ignores the GDI+ clip, so a "measure" against the live
        // surface painted a whole second copy of the document at the origin.
        using var surface = new Bitmap(1, 1);
        using var g = Graphics.FromImage(surface);

        return Draw(g, invoice, format, 0, 0, width);
    }

    // ------------------------------------------------------------------ 80mm roll

    private static int DrawReceipt(Graphics g, InvoiceDetailDto invoice, int x, int y, int width)
    {
        Render.UseQualityRendering(g);

        var ink = Palette.InkStrong;
        var muted = Palette.InkMuted;
        var top = y;

        var tiny = Typography.Get(Typography.Size2Xs);
        var tinyBold = Typography.Get(Typography.Size2Xs, FontStyle.Bold);
        var small = Typography.Get(Typography.SizeXs);
        var mono = Typography.Numeric(Typography.Size2Xs);
        var monoBold = Typography.Numeric(Typography.SizeXs, FontStyle.Bold);

        y = Centred(g, invoice.ShopName, Typography.Get(Typography.SizeMd, FontStyle.Bold), ink, x, y, width) + 2;

        if (!string.IsNullOrWhiteSpace(invoice.ShopAddress))
        {
            y = Centred(g, invoice.ShopAddress, tiny, muted, x, y, width);
        }

        if (!string.IsNullOrWhiteSpace(invoice.ShopPhone))
        {
            y = Centred(g, invoice.ShopPhone, tiny, muted, x, y, width);
        }

        y += 6;
        y = Dashes(g, x, y, width);
        y += 4;

        y = Row(g, "Invoice", invoice.InvoiceNo, tiny, tinyBold, muted, ink, x, y, width);
        y = Row(g, "Date", Format.DateTimeShort(invoice.InvoiceDate), tiny, tiny, muted, ink, x, y, width);
        y = Row(g, "Customer", invoice.CustomerName, tiny, tiny, muted, ink, x, y, width);
        y = Row(g, "Served by", invoice.CreatedBy, tiny, tiny, muted, ink, x, y, width);

        y += 4;
        y = Dashes(g, x, y, width);
        y += 4;

        if (invoice.Status == InvoiceStatus.Void)
        {
            y = Centred(g, "*** VOID ***", Typography.Get(Typography.SizeXs, FontStyle.Bold), Palette.DangerText, x, y, width);
            y += 4;
        }

        // Two lines per item: the name on its own, then the arithmetic. Three columns
        // of a table do not fit across 80mm without truncating product names into
        // uselessness, which defeats the point of a receipt.
        foreach (var item in invoice.Items)
        {
            y = Wrapped(g, item.ProductName, small, ink, x, y, width);

            var detail = $"{item.Qty} x {Format.Amount(item.UnitPrice)}";
            y = Row(g, detail, Format.Amount(item.LineTotal), mono, mono, muted, ink, x, y, width);

            if (item.TaxAmount > 0m)
            {
                y = Row(g, "   tax", Format.Amount(item.TaxAmount), mono, mono, muted, muted, x, y, width);
            }

            y += 2;
        }

        y = Dashes(g, x, y, width);
        y += 4;

        y = Row(g, "Subtotal", Format.Amount(invoice.SubTotal), tiny, mono, muted, ink, x, y, width);

        if (invoice.Discount > 0m)
        {
            y = Row(g, "Discount", "-" + Format.Amount(invoice.Discount), tiny, mono, muted, ink, x, y, width);
        }

        if (invoice.TaxTotal > 0m)
        {
            y = Row(g, "Sales tax", Format.Amount(invoice.TaxTotal), tiny, mono, muted, ink, x, y, width);
        }

        y += 2;
        y = Dashes(g, x, y, width);
        y += 2;

        y = Row(
            g,
            "TOTAL",
            Format.Money(invoice.Total),
            Typography.Get(Typography.SizeXs, FontStyle.Bold),
            monoBold,
            ink,
            ink,
            x,
            y,
            width);

        y = Row(
            g,
            Format.Humanise(invoice.PaymentMethod.ToString()),
            Format.Amount(invoice.PaidAmount),
            tiny,
            mono,
            muted,
            ink,
            x,
            y,
            width);

        if (invoice.Outstanding > 0m)
        {
            y = Row(g, "Balance due", Format.Amount(invoice.Outstanding), tinyBold, mono, Palette.DangerText, Palette.DangerText, x, y, width);
        }

        y += 6;
        y = Dashes(g, x, y, width);
        y += 6;

        y = Centred(g, "Thank you for your custom", tiny, muted, x, y, width);
        y = Centred(g, "Exchange within 7 days with this receipt", tiny, muted, x, y, width);

        // Feed past the tear bar, or the last line is still inside the printer.
        y += 24;

        return y - top;
    }

    // ------------------------------------------------------------------ A4

    private static int DrawA4(Graphics g, InvoiceDetailDto invoice, int x, int y, int width)
    {
        Render.UseQualityRendering(g);

        var ink = Palette.InkStrong;
        var muted = Palette.InkMuted;
        var body = Palette.InkBody;

        var left = x;
        var right = x + width;
        var top = y;

        y = Wrapped(g, invoice.ShopName, Typography.H3, ink, left, y, width) + 2;

        if (!string.IsNullOrWhiteSpace(invoice.ShopAddress))
        {
            y = Wrapped(g, invoice.ShopAddress, Typography.Get(Typography.SizeSm), muted, left, y, width);
        }

        if (!string.IsNullOrWhiteSpace(invoice.ShopPhone))
        {
            y = Wrapped(g, invoice.ShopPhone, Typography.Get(Typography.SizeSm), muted, left, y, width);
        }

        y += Metrics.Space4;
        y = Rule(g, left, right, y) + Metrics.Space3;

        var metaTop = y;
        Labelled(g, "Invoice", invoice.InvoiceNo, left, ref y, 240);
        Labelled(g, "Date", Format.DateTimeShort(invoice.InvoiceDate), left, ref y, 240);
        Labelled(g, "Raised by", invoice.CreatedBy, left, ref y, 240);
        var metaBottom = y;

        y = metaTop;
        var column = left + (width / 2);
        Labelled(g, "Customer", invoice.CustomerName, column, ref y, right - column);

        if (!string.IsNullOrWhiteSpace(invoice.CustomerPhone))
        {
            Labelled(g, "Phone", invoice.CustomerPhone, column, ref y, right - column);
        }

        Labelled(g, "Payment", Format.Humanise(invoice.PaymentMethod.ToString()), column, ref y, right - column);

        y = Math.Max(metaBottom, y) + Metrics.Space3;

        if (invoice.Status == InvoiceStatus.Void)
        {
            var banner = new Rectangle(left, y, width, 42);
            Render.FillRounded(g, banner, Metrics.RadiusMd, Palette.RoseSoft);
            TextRenderer.DrawText(
                g,
                $"VOID  —  {invoice.VoidReason}",
                Typography.Get(Typography.SizeBase, FontStyle.Bold),
                banner,
                Palette.RoseText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            y = banner.Bottom + Metrics.Space3;
        }

        var qtyX = right - 400;
        var priceX = right - 330;
        var taxX = right - 220;
        var totalX = right - 120;

        var header = new Rectangle(left, y, width, 28);
        Render.FillRounded(g, header, Metrics.RadiusXs, Palette.SurfaceSunken);

        var headFont = Typography.Get(Typography.Size2Xs, FontStyle.Bold);
        Cell(g, "Item", headFont, muted, left + 8, y, qtyX - left - 16, 28, false);
        Cell(g, "Qty", headFont, muted, qtyX, y, 60, 28, true);
        Cell(g, Format.MoneyHeader("Price"), headFont, muted, priceX, y, 100, 28, true);
        Cell(g, Format.MoneyHeader("Tax"), headFont, muted, taxX, y, 100, 28, true);
        Cell(g, Format.MoneyHeader("Total"), headFont, muted, totalX, y, 112, 28, true);
        y = header.Bottom;

        var cellFont = Typography.Get(Typography.SizeSm);
        var cellMono = Typography.Numeric(Typography.SizeSm);

        foreach (var item in invoice.Items)
        {
            const int rowHeight = 30;

            Cell(g, $"{item.Sku}  {item.ProductName}", cellFont, body, left + 8, y, qtyX - left - 16, rowHeight, false);
            Cell(g, Format.Qty(item.Qty), cellMono, body, qtyX, y, 60, rowHeight, true);
            Cell(g, Format.Amount(item.UnitPrice), cellMono, body, priceX, y, 100, rowHeight, true);

            // A dash, not 0.00: an exempt line and one that rounded to nothing are
            // different things, and a customer may well ask which.
            Cell(g, item.TaxAmount == 0m ? "—" : Format.Amount(item.TaxAmount), cellMono, body, taxX, y, 100, rowHeight, true);
            Cell(g, Format.Amount(item.LineTotal), cellMono, ink, totalX, y, 112, rowHeight, true);

            y += rowHeight;

            using var pen = new Pen(Palette.BorderSubtle);
            g.DrawLine(pen, left, y, right, y);
        }

        y += Metrics.Space4;

        Total(g, "Subtotal", invoice.SubTotal, priceX, totalX, ref y, false);

        if (invoice.Discount > 0m)
        {
            Total(g, "Discount", -invoice.Discount, priceX, totalX, ref y, false);
        }

        if (invoice.TaxTotal > 0m)
        {
            Total(g, "Sales tax", invoice.TaxTotal, priceX, totalX, ref y, false);
        }

        Total(g, "Total", invoice.Total, priceX, totalX, ref y, true);
        Total(g, "Paid", invoice.PaidAmount, priceX, totalX, ref y, false);

        if (invoice.Outstanding > 0m)
        {
            Total(g, "Outstanding", invoice.Outstanding, priceX, totalX, ref y, true, Palette.DangerText);
        }

        if (!string.IsNullOrWhiteSpace(invoice.Note))
        {
            y += Metrics.Space4;
            y = Rule(g, left, right, y) + Metrics.Space2;
            y = Wrapped(g, invoice.Note, Typography.Get(Typography.SizeSm), muted, left, y, width);
        }

        return y - top;
    }

    // ------------------------------------------------------------------ primitives

    private static int Centred(Graphics g, string text, Font font, Color colour, int x, int y, int width)
    {
        var size = TextRenderer.MeasureText(g, text, font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak);

        TextRenderer.DrawText(
            g,
            text,
            font,
            new Rectangle(x, y, width, size.Height),
            colour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

        return y + size.Height;
    }

    private static int Wrapped(Graphics g, string text, Font font, Color colour, int x, int y, int width)
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

    /// <summary>A caption on the left and a value hard against the right.</summary>
    private static int Row(
        Graphics g,
        string caption,
        string value,
        Font captionFont,
        Font valueFont,
        Color captionColour,
        Color valueColour,
        int x,
        int y,
        int width)
    {
        var height = Math.Max(
            TextRenderer.MeasureText(g, caption, captionFont).Height,
            TextRenderer.MeasureText(g, value, valueFont).Height);

        var half = width / 2;

        TextRenderer.DrawText(
            g,
            caption,
            captionFont,
            new Rectangle(x, y, half, height),
            captionColour,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            value,
            valueFont,
            new Rectangle(x + half, y, width - half, height),
            valueColour,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        return y + height;
    }

    /// <summary>
    /// A dashed rule. Thermal printers render a drawn hairline unevenly at low feed
    /// speeds, so a row of hyphens is what receipts actually use.
    /// </summary>
    private static int Dashes(Graphics g, int x, int y, int width)
    {
        var font = Typography.Numeric(Typography.Size2Xs);
        var dashWidth = Math.Max(TextRenderer.MeasureText(g, "-", font).Width - 2, 1);

        return Wrapped(g, new string('-', Math.Max(8, width / dashWidth)), font, Palette.Neutral400, x, y, width);
    }

    private static void Labelled(Graphics g, string caption, string value, int x, ref int y, int width)
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
