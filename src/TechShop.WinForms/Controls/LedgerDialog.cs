using System.Drawing;
using TechShop.Application.Features.Ledgers;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// A party's account history: opening balance, every document that moved it, closing
/// balance.
///
/// The closing figure is shown twice on purpose — once as the running balance on the last
/// row and once in the summary — because the whole point of a ledger is that those two
/// agree. If they ever disagree, this screen is where it shows.
/// </summary>
public sealed class LedgerDialog : Form
{
    public LedgerDialog(string title, LedgerDto ledger)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 560);
        MinimumSize = new Size(700, 420);
        BackColor = Palette.AppBackground;
        Font = Typography.Body;
        ShowIcon = false;
        MinimizeBox = false;

        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Palette.Surface };
        header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Render.UseQualityRendering(g);

            TextRenderer.DrawText(
                g,
                ledger.PartyName,
                Typography.H3,
                new Rectangle(Metrics.Space5, Metrics.Space3, header.Width - 320, 30),
                Palette.InkStrong,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(
                g,
                $"{ledger.Rows.Count:N0} entries",
                Typography.Get(Typography.SizeSm),
                new Rectangle(Metrics.Space5 + 2, 44, header.Width - 320, 20),
                Palette.InkMuted,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            Figure(g, header.Width - 300, "Opening", ledger.Opening);
            Figure(g, header.Width - 150, "Closing", ledger.Closing);

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

        grid.Columns.Add(TextColumn("date", "Date", 110));
        grid.Columns.Add(TextColumn("reference", "Reference", 150));
        grid.Columns.Add(Fill("description", "Description"));
        grid.Columns.Add(Money("debit", Format.MoneyHeader("Debit")));
        grid.Columns.Add(Money("credit", Format.MoneyHeader("Credit")));
        grid.Columns.Add(Money("balance", Format.MoneyHeader("Balance")));

        foreach (var row in ledger.Rows)
        {
            grid.Rows.Add(
                Format.Date(row.Date),
                row.Reference,
                row.Description,
                row.Debit == 0 ? string.Empty : Format.Amount(row.Debit),
                row.Credit == 0 ? string.Empty : Format.Amount(row.Credit),
                Format.Amount(row.Balance));
        }

        if (ledger.Rows.Count == 0)
        {
            var empty = new Label
            {
                Dock = DockStyle.Fill,
                Text = "No entries on this account yet.",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Palette.InkMuted,
                Font = Typography.Get(Typography.SizeMd),
                BackColor = Palette.Surface,
            };
            Controls.Add(empty);
        }
        else
        {
            Controls.Add(grid);
        }

        Controls.Add(header);

        var close = new AppButton { Variant = ButtonVariant.Secondary, Text = "Close", Width = 100 };
        close.Click += (_, _) => Close();

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Palette.SurfaceSunken };
        footer.Controls.Add(close);
        footer.Resize += (_, _) => close.Location = new Point(
            footer.Width - close.Width - Metrics.Space5,
            (footer.Height - close.Height) / 2);
        Controls.Add(footer);

        CancelButton = close;

        // Never taller or wider than the screen it opens on.
        this.KeepOnScreen();
    }

    private static void Figure(Graphics g, int x, string label, decimal value)
    {
        TextRenderer.DrawText(
            g,
            label,
            Typography.Get(Typography.Size2Xs, FontStyle.Bold),
            new Rectangle(x, Metrics.Space4, 140, 16),
            Palette.InkMuted,
            TextFormatFlags.Right | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            Format.Money(value),
            Typography.Numeric(Typography.SizeMd, FontStyle.Bold),
            new Rectangle(x, Metrics.Space4 + 18, 140, 24),
            value > 0 ? Palette.DangerText : Palette.InkStrong,
            TextFormatFlags.Right | TextFormatFlags.NoPrefix);
    }

    private static DataGridViewTextBoxColumn TextColumn(string name, string header, int width) => new()
    {
        Name = name,
        HeaderText = header,
        Width = width,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private static DataGridViewTextBoxColumn Fill(string name, string header) => new()
    {
        Name = name,
        HeaderText = header,
        AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private static DataGridViewTextBoxColumn Money(string name, string header)
    {
        var column = TextColumn(name, header, 120);
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.DefaultCellStyle.Font = Typography.Numeric(Typography.SizeSm);
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        return column;
    }
}
