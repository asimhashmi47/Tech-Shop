using System.Drawing;
using TechShop.Application.Common.Query;
using TechShop.Application.Features.Stock;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Stock;

/// <summary>
/// The movement history: every opening balance, purchase, sale, adjustment, transfer and
/// sale reversal, in one list.
///
/// This is the screen that answers "why does the system say eleven when I counted nine",
/// so it shows the reference of the document that caused each movement and who entered it.
/// </summary>
public sealed class StockMovementsDialog : Form
{
    private readonly DataGridPanel<StockMovementDto> _grid = new();

    public StockMovementsDialog(StockService stock, long? shopId, string? sku)
    {
        Text = "Stock movements";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1080, 620);
        MinimumSize = new Size(820, 460);
        BackColor = Palette.AppBackground;
        Font = Typography.Body;
        ShowIcon = false;
        MinimizeBox = false;

        var header = new PageHeader(
            "Stock movements",
            sku is null ? "Every change, newest first" : $"Filtered to {sku}")
        {
            Dock = DockStyle.Top,
        };

        _grid
            .WithColumns(
                new GridColumn<StockMovementDto>("movedAt", "When", m => m.MovedAt)
                {
                    Width = 150,
                    Format = m => Format.DateTimeShort(m.MovedAt),
                },
                new GridColumn<StockMovementDto>("shopName", "Shop", m => m.ShopName) { Width = 160 },
                new GridColumn<StockMovementDto>("sku", "SKU", m => m.Sku) { Width = 130 },
                new GridColumn<StockMovementDto>("productName", "Product", m => m.ProductName) { Fill = true },
                new GridColumn<StockMovementDto>("type", "Type", m => m.Type)
                {
                    Width = 120,
                    Chip = m => (Format.Humanise(m.Type.ToString()), Tone(m.Type).Fill, Tone(m.Type).Ink),
                },
                new GridColumn<StockMovementDto>("qty", "Qty", m => m.Qty)
                {
                    Width = 80,
                    Align = ContentAlignment.MiddleRight,
                    Numeric = true,

                    // A signed quantity is the whole point of a movement row, so the sign
                    // is shown explicitly rather than inferred from the type.
                    Format = m => m.Qty > 0 ? $"+{Format.Qty(m.Qty)}" : Format.Qty(m.Qty),
                    Foreground = m => m.Qty < 0 ? Palette.DangerText : Palette.EmeraldText,
                },
                new GridColumn<StockMovementDto>("reference", "Reference", m => m.Reference) { Width = 160 },
                new GridColumn<StockMovementDto>("reason", "Reason", m => m.Reason) { Width = 180 },
                new GridColumn<StockMovementDto>("createdBy", "By", m => m.CreatedBy) { Width = 100 })
            .SortedBy("movedAt", descending: true)
            .WithLoader(request => stock.QueryMovementsAsync(request));

        _grid.Scope = request =>
        {
            request.ShopId = shopId;

            if (sku is not null)
            {
                request.Filters.Add(new FilterRule
                {
                    Field = "sku",
                    Operator = FilterOperator.Equals,
                    Value = sku,
                });
            }
        };

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            Padding = new Padding(Metrics.Space4),
        };
        card.Controls.Add(_grid);
        card.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Render.UseQualityRendering(g);
            var bounds = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
            Render.FillRounded(g, bounds, Metrics.RadiusLg, Palette.Surface);
            Render.DrawRoundedBorder(g, bounds, Metrics.RadiusLg, Palette.BorderSubtle);
        };

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.AppBackground,
            Padding = new Padding(Metrics.Space5, Metrics.Space4, Metrics.Space5, Metrics.Space4),
        };
        body.Controls.Add(card);
        body.Controls.Add(header);

        var close = new AppButton { Variant = ButtonVariant.Secondary, Text = "Close", Width = 100 };
        close.Click += (_, _) => Close();

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Palette.SurfaceSunken };
        footer.Controls.Add(close);
        footer.Resize += (_, _) => close.Location = new Point(
            footer.Width - close.Width - Metrics.Space5,
            (footer.Height - close.Height) / 2);

        Controls.Add(body);
        Controls.Add(footer);

        CancelButton = close;
        Shown += async (_, _) => await _grid.ReloadAsync().ConfigureAwait(true);
    }

    private static (Color Fill, Color Ink) Tone(StockMovementType type) => type switch
    {
        StockMovementType.Purchase or StockMovementType.TransferIn or StockMovementType.AdjustmentIn
            => (Palette.EmeraldSoft, Palette.EmeraldText),

        StockMovementType.Sale or StockMovementType.TransferOut or StockMovementType.AdjustmentOut
            => (Palette.RoseSoft, Palette.RoseText),

        StockMovementType.SaleVoid => (Palette.AmberSoft, Palette.AmberText),

        _ => (Palette.SlateSoft, Palette.SlateText),
    };
}
