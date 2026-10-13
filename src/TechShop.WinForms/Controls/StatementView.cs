using System.Drawing;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// Renders a financial statement: a column of labelled figures with subtotals, totals and
/// the occasional rule.
///
/// Statements are built from a list of rows rather than laid out with controls because
/// the shape is the same for a profit and loss, a balance sheet and a cash flow — only
/// the rows differ — and because a total has to be able to sit visually apart from the
/// lines it sums.
/// </summary>
public sealed class StatementView : Panel
{
    public enum RowKind
    {
        /// <summary>An ordinary line.</summary>
        Line,

        /// <summary>A subtotal: rule above, bolder text.</summary>
        Subtotal,

        /// <summary>The statement's answer: heavier, with a rule above and below.</summary>
        Total,

        /// <summary>A section caption with no figure.</summary>
        Heading,

        /// <summary>Vertical breathing room.</summary>
        Gap,
    }

    public sealed record Row(RowKind Kind, string Label, decimal? Amount = null, string? Note = null)
    {
        /// <summary>Forces a colour, for a figure that is good or bad rather than neutral.</summary>
        public Color? Colour { get; init; }
    }

    private readonly List<Row> _rows = [];
    private string _title = string.Empty;
    private string _subtitle = string.Empty;

    public StatementView()
    {
        BackColor = Palette.Surface;
        AutoScroll = true;
        DoubleBuffered = true;
        Padding = new Padding(Metrics.Space6);
    }

    public void Show(string title, string subtitle, IEnumerable<Row> rows)
    {
        _title = title;
        _subtitle = subtitle;
        _rows.Clear();
        _rows.AddRange(rows);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        Render.UseQualityRendering(g);

        var left = Padding.Left;
        var right = Math.Max(left + 320, Width - Padding.Right - SystemInformation.VerticalScrollBarWidth);
        var amountWidth = 170;
        var labelRight = right - amountWidth - Metrics.Space4;

        var y = Padding.Top + AutoScrollPosition.Y;

        TextRenderer.DrawText(
            g,
            _title,
            Typography.H3,
            new Rectangle(left, y, right - left, 30),
            Palette.InkStrong,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        y += 32;

        TextRenderer.DrawText(
            g,
            _subtitle,
            Typography.Get(Typography.SizeSm),
            new Rectangle(left, y, right - left, 20),
            Palette.InkMuted,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        y += 30;

        foreach (var row in _rows)
        {
            switch (row.Kind)
            {
                case RowKind.Gap:
                    y += Metrics.Space4;
                    continue;

                case RowKind.Heading:
                    y += Metrics.Space3;
                    TextRenderer.DrawText(
                        g,
                        row.Label.ToUpperInvariant(),
                        Typography.Get(Typography.Size2Xs, FontStyle.Bold),
                        new Rectangle(left, y, right - left, 18),
                        Palette.InkMuted,
                        TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                    y += 22;
                    continue;
            }

            if (row.Kind is RowKind.Subtotal or RowKind.Total)
            {
                using var pen = new Pen(row.Kind == RowKind.Total ? Palette.Border : Palette.BorderSubtle);
                g.DrawLine(pen, left, y, right, y);
                y += Metrics.Space2;
            }

            var strong = row.Kind is RowKind.Subtotal or RowKind.Total;
            var labelFont = row.Kind switch
            {
                RowKind.Total => Typography.Get(Typography.SizeMd, FontStyle.Bold),
                RowKind.Subtotal => Typography.Get(Typography.SizeBase, FontStyle.Bold),
                _ => Typography.Get(Typography.SizeBase),
            };

            var amountFont = row.Kind switch
            {
                RowKind.Total => Typography.Numeric(Typography.SizeXl, FontStyle.Bold),
                RowKind.Subtotal => Typography.Numeric(Typography.SizeBase, FontStyle.Bold),
                _ => Typography.Numeric(Typography.SizeBase),
            };

            var height = row.Kind == RowKind.Total ? 36 : 26;

            TextRenderer.DrawText(
                g,
                row.Label,
                labelFont,
                new Rectangle(left, y, labelRight - left, height),
                strong ? Palette.InkStrong : Palette.InkBody,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (row.Amount is { } amount)
            {
                TextRenderer.DrawText(
                    g,
                    Format.Amount(amount),
                    amountFont,
                    new Rectangle(labelRight, y, amountWidth, height),
                    row.Colour ?? (amount < 0 ? Palette.DangerText : strong ? Palette.InkStrong : Palette.InkBody),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            if (!string.IsNullOrWhiteSpace(row.Note))
            {
                y += height - 4;
                TextRenderer.DrawText(
                    g,
                    row.Note,
                    Typography.Get(Typography.Size2Xs),
                    new Rectangle(left, y, right - left, 16),
                    Palette.InkMuted,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                y += 18;
            }
            else
            {
                y += height;
            }

            if (row.Kind == RowKind.Total)
            {
                using var pen = new Pen(Palette.Border);
                g.DrawLine(pen, left, y, right, y);
                y += Metrics.Space2;
            }
        }

        // Tell the panel how tall its content is, so it scrolls when a statement is long.
        AutoScrollMinSize = new Size(0, y - AutoScrollPosition.Y + Padding.Bottom);
    }
}
