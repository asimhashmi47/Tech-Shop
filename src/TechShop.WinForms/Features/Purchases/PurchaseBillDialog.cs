using System.Drawing;
using TechShop.Application.Features.Purchases;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Purchases;

/// <summary>One supplier bill, read only. What arrived, at what cost, and what is still owed.</summary>
public sealed class PurchaseBillDialog : Form
{
    public PurchaseBillDialog(PurchaseBillDetailDto bill)
    {
        Text = bill.BillNo;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 600);
        MinimumSize = new Size(640, 480);
        BackColor = Palette.AppBackground;
        Font = Typography.Body;
        ShowIcon = false;
        MinimizeBox = false;

        var header = new Panel { Dock = DockStyle.Top, Height = 116, BackColor = Palette.Surface };
        header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Render.UseQualityRendering(g);

            TextRenderer.DrawText(
                g,
                bill.SupplierName,
                Typography.H3,
                new Rectangle(Metrics.Space5, Metrics.Space4, header.Width - 280, 30),
                Palette.InkStrong,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(
                g,
                $"{bill.BillNo}  ·  {Format.Date(bill.BillDate)}  ·  {bill.ShopName}",
                Typography.Get(Typography.SizeSm),
                new Rectangle(Metrics.Space5 + 2, 48, header.Width - 280, 20),
                Palette.InkMuted,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            if (!string.IsNullOrWhiteSpace(bill.Note))
            {
                TextRenderer.DrawText(
                    g,
                    bill.Note,
                    Typography.Get(Typography.SizeSm),
                    new Rectangle(Metrics.Space5 + 2, 72, header.Width - 280, 20),
                    Palette.InkBody,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            Figure(g, header.Width - 250, Metrics.Space4, "Total", bill.Total, Palette.InkStrong);
            Figure(g, header.Width - 250, 60, "Paid", bill.PaidAmount, Palette.EmeraldText);
            Figure(g, header.Width - 120, Metrics.Space4, "Owed", bill.Outstanding, Palette.WarningText);
            Figure(
                g,
                header.Width - 120,
                60,
                "Method",
                null,
                Palette.InkBody,
                Format.Humanise(bill.PaymentMethod.ToString()));

            using var pen = new Pen(Palette.BorderSubtle);
            g.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Palette.Surface,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Palette.BorderSubtle,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeight = Metrics.GridHeaderHeight,
            Font = Typography.GridBody,
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = Palette.SurfaceSunken;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.InkMuted;
        grid.ColumnHeadersDefaultCellStyle.Font = Typography.Get(Typography.Size2Xs, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(Metrics.Space3, 0, Metrics.Space3, 0);
        // A header is not selectable, so it must not pick up the system selection
        // colour when the grid takes focus — that showed as one header cell going blue.
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Palette.SurfaceSunken;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Palette.InkMuted;
        grid.DefaultCellStyle.BackColor = Palette.Surface;
        grid.DefaultCellStyle.ForeColor = Palette.InkBody;
        grid.DefaultCellStyle.SelectionBackColor = Palette.PrimarySoft;
        grid.DefaultCellStyle.SelectionForeColor = Palette.InkStrong;
        grid.DefaultCellStyle.Padding = new Padding(Metrics.Space3, 0, Metrics.Space3, 0);
        grid.RowTemplate.Height = Metrics.GridRowHeight;

        grid.Columns.Add(Col("sku", "SKU", 130, false));
        grid.Columns.Add(FillCol("name", "Product"));
        grid.Columns.Add(Col("qty", "Qty", 80, true));
        grid.Columns.Add(Col("cost", Format.MoneyHeader("Unit cost"), 130, true));
        grid.Columns.Add(Col("total", Format.MoneyHeader("Line total"), 140, true));

        foreach (var item in bill.Items)
        {
            grid.Rows.Add(
                item.Sku,
                item.ProductName,
                Format.Qty(item.Qty),
                Format.Amount(item.UnitCost),
                Format.Amount(item.LineTotal));
        }

        var close = new AppButton { Variant = ButtonVariant.Secondary, Text = "Close", Width = 100 };
        close.Click += (_, _) => Close();

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Palette.SurfaceSunken };
        footer.Controls.Add(close);
        footer.Resize += (_, _) => close.Location = new Point(
            footer.Width - close.Width - Metrics.Space5,
            (footer.Height - close.Height) / 2);

        Controls.Add(grid);
        Controls.Add(header);
        Controls.Add(footer);
        CancelButton = close;

        // Never taller or wider than the screen it opens on.
        this.KeepOnScreen();
    }

    private static void Figure(
        Graphics g,
        int x,
        int y,
        string label,
        decimal? value,
        Color colour,
        string? text = null)
    {
        TextRenderer.DrawText(
            g,
            label,
            Typography.Get(Typography.Size2Xs, FontStyle.Bold),
            new Rectangle(x, y, 110, 14),
            Palette.InkMuted,
            TextFormatFlags.Right | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            text ?? Format.Amount(value ?? 0m),
            text is null
                ? Typography.Numeric(Typography.SizeBase, FontStyle.Bold)
                : Typography.Get(Typography.SizeSm),
            new Rectangle(x, y + 15, 110, 20),
            colour,
            TextFormatFlags.Right | TextFormatFlags.NoPrefix);
    }

    private static DataGridViewTextBoxColumn Col(string name, string header, int width, bool numeric)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };

        if (numeric)
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Font = Typography.Numeric(Typography.SizeSm);
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }

        return column;
    }

    private static DataGridViewTextBoxColumn FillCol(string name, string header) => new()
    {
        Name = name,
        HeaderText = header,
        AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };
}
