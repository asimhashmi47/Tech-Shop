using System.Drawing;
using TechShop.Application.Features.Dashboard;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Dashboard;

/// <summary>
/// The opening screen: the eight figures that say how the business is doing, then the
/// rows behind two of them.
///
/// Every card states what it counts and over what window, because a number on its own is
/// not information. Cards that have rows behind them are clickable and send the operator
/// to the screen that holds those rows.
/// </summary>
public sealed class DashboardPage : PageBase
{
    private readonly PageHeader _header;
    private readonly DateRangeBar _range = new();
    private readonly FlowLayoutPanel _cards;
    private readonly DataGridPanel<Application.Features.Sales.InvoiceListDto> _recent = new();
    private readonly DataGridPanel<Application.Features.Stock.StockLevelDto> _lowStock = new();

    private DashboardDto? _data;

    public DashboardPage()
        : base(Module.Dashboard, "Dashboard")
    {
        _header = new PageHeader("Dashboard");

        var refresh = new AppButton { Variant = ButtonVariant.Secondary, Text = "Refresh" };
        refresh.FitToText();
        refresh.Click += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        _header.AddAction(refresh);

        // Height is computed from how many cards fit per row rather than left to
        // AutoSize. A docked FlowLayoutPanel measures itself before it knows its final
        // width, decides it needs an extra row or two, and keeps that height — which
        // squeezed the lists underneath down to a sliver.
        _cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, Metrics.Space4, 0, 0),
            Height = Metrics.KpiCardHeight + Metrics.Space4 + Metrics.Space4,
        };

        _cards.Resize += (_, _) => SizeCardRows();

        _range.Changed += async (_, _) => await ReloadAsync().ConfigureAwait(true);

        BuildRecentGrid();
        BuildLowStockGrid();

        // Fill, not a fixed height: the lists take whatever is left under the cards, so
        // the dashboard fits its window instead of pushing a scrollbar onto the page.
        var lists = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0, Metrics.Space4, 0, 0),
        };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        lists.Controls.Add(Section("Recent invoices", _recent), 0, 0);
        lists.Controls.Add(Section("Low stock", _lowStock), 1, 0);

        // Docked top-to-bottom, so added in reverse of the order they appear.
        Controls.Add(lists);
        Controls.Add(_cards);
        Controls.Add(_range);
        Controls.Add(_header);
    }

    /// <summary>
    /// Sizes the card strip to exactly the rows it needs at the current width, so the
    /// lists below it get everything that is left and the page never has to scroll.
    /// </summary>
    /// <summary>The lists are the point of the lower half, so they are never squeezed below this.</summary>
    private const int MinListHeight = 226;

    /// <summary>Below this a card cannot show its label, figure and caption legibly.</summary>
    private const int MinCardHeight = 100;

    private void SizeCardRows()
    {
        var count = _cards.Controls.Count;
        if (count == 0 || _cards.ClientSize.Width <= 0)
        {
            return;
        }

        var step = Metrics.KpiCardWidth + Metrics.Space4;
        var perRow = Math.Max(1, (_cards.ClientSize.Width - Metrics.Space2) / step);
        var rows = (int)Math.Ceiling(count / (double)perRow);

        // What is left for the cards once the header, the range bar and a usable list
        // area have taken theirs. On a laptop this is what stops eight cards filling
        // the screen and leaving one row of invoices underneath.
        var spare = Height - _header.Height - _range.Height - MinListHeight;
        var perCard = ((spare - Metrics.Space4) / Math.Max(rows, 1)) - Metrics.Space4;

        var cardHeight = Math.Clamp(perCard, MinCardHeight, Metrics.KpiCardHeight);

        foreach (var card in _cards.Controls.OfType<KpiCard>())
        {
            if (card.Height != cardHeight)
            {
                card.Height = cardHeight;
            }
        }

        var wanted = (rows * (cardHeight + Metrics.Space4)) + Metrics.Space4;

        if (_cards.Height != wanted)
        {
            _cards.Height = wanted;
        }
    }

    private static Panel Section(string title, Control body)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            Padding = new Padding(Metrics.Space4),
            Margin = new Padding(0, 0, Metrics.Space4, 0),
        };

        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = title,
            Font = Typography.Get(Typography.SizeMd, FontStyle.Bold),
            ForeColor = Palette.Ink,
            BackColor = Color.Transparent,
        };

        body.Dock = DockStyle.Fill;
        card.Controls.Add(body);
        card.Controls.Add(heading);

        card.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Render.UseQualityRendering(g);
            var bounds = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
            Render.FillRounded(g, bounds, Metrics.RadiusLg, Palette.Surface);
            Render.DrawRoundedBorder(g, bounds, Metrics.RadiusLg, Palette.BorderSubtle);
        };

        return card;
    }

    private void BuildRecentGrid()
    {
        var sales = Service<Application.Features.Sales.SalesService>();

        // A fixed snapshot, so no search box and no pager.
        _recent.Compact = true;

        _recent
            .WithColumns(
                new GridColumn<Application.Features.Sales.InvoiceListDto>("invoiceNo", "Invoice", r => r.InvoiceNo) { Width = 160 },
                new GridColumn<Application.Features.Sales.InvoiceListDto>("invoiceDate", "Date", r => r.InvoiceDate)
                {
                    Width = 110,
                    Format = r => Format.Date(r.InvoiceDate),
                },
                new GridColumn<Application.Features.Sales.InvoiceListDto>("customerName", "Customer", r => r.CustomerName) { Fill = true },
                new GridColumn<Application.Features.Sales.InvoiceListDto>("total", Format.MoneyHeader("Total"), r => r.Total)
                {
                    // A shop's invoices reach seven figures, and a truncated total is
                    // worse than no total: "521,60..." reads as five hundred thousand
                    // or five million depending on the reader.
                    Width = 140,
                    Align = ContentAlignment.MiddleRight,
                    Numeric = true,
                    Format = r => Format.Amount(r.Total),
                },
                new GridColumn<Application.Features.Sales.InvoiceListDto>("status", "Status", r => r.Status)
                {
                    Width = 90,
                    Chip = r =>
                    {
                        var (fill, ink) = Badge.ToneFor(r.Status);
                        return (r.Status.ToString(), fill, ink);
                    },
                })
            .SortedBy("invoiceDate", descending: true)
            .WithLoader(request => sales.QueryAsync(request));

        _recent.Scope = request =>
        {
            request.ShopId = ActiveShopId;
            request.PageSize = 25;
        };
    }

    private void BuildLowStockGrid()
    {
        var stock = Service<Application.Features.Stock.StockService>();

        _lowStock.Compact = true;

        _lowStock
            .WithColumns(
                new GridColumn<Application.Features.Stock.StockLevelDto>("sku", "SKU", r => r.Sku) { Width = 120 },
                new GridColumn<Application.Features.Stock.StockLevelDto>("productName", "Product", r => r.ProductName) { Fill = true },
                new GridColumn<Application.Features.Stock.StockLevelDto>("qty", "On hand", r => r.Qty)
                {
                    Width = 80,
                    Align = ContentAlignment.MiddleRight,
                    Numeric = true,
                    Format = r => Format.Qty(r.Qty),

                    // The figure that triggered the row is the one that should stand out.
                    Foreground = r => r.Qty <= 0 ? Palette.DangerText : Palette.WarningText,
                },
                new GridColumn<Application.Features.Stock.StockLevelDto>("reorderLevel", "Reorder at", r => r.ReorderLevel)
                {
                    Width = 86,
                    Align = ContentAlignment.MiddleRight,
                    Numeric = true,
                    Format = r => Format.Qty(r.ReorderLevel),
                })
            .SortedBy("qty")
            .WithLoader(request => stock.QueryLevelsAsync(request));

        _lowStock.Scope = request =>
        {
            request.ShopId = ActiveShopId;
            request.PageSize = 25;
            request.Filters.Add(new Application.Common.Query.FilterRule
            {
                Field = "isLow",
                Operator = Application.Common.Query.FilterOperator.Equals,
                Value = "true",
            });
        };
    }

    protected override async Task LoadAsync()
    {
        var service = Service<DashboardService>();
        _data = await service.GetAsync(ActiveShopId, _range.FromUtc, _range.ToUtc).ConfigureAwait(true);

        _header.Subtitle = _range.Describe(ScopeLabel());
        BuildCards(_data);

        await _recent.ReloadAsync().ConfigureAwait(true);
        await _lowStock.ReloadAsync().ConfigureAwait(true);
    }

    private static string ScopeLabel() => ActiveShopId is null ? "All shops" : "Selected shop";

    private void BuildCards(DashboardDto data)
    {
        _cards.SuspendLayout();
        foreach (Control existing in _cards.Controls)
        {
            existing.Dispose();
        }

        _cards.Controls.Clear();

        var kpi = data.Kpi;
        var window = $"{Format.Date(data.Range.From)} to {Format.Date(data.Range.To)}";

        Add(Icons.Reports, Palette.Indigo, "Today sales", Format.Money(kpi.TodaySales), "Invoices raised today", Module.Invoices);
        Add(Icons.Calendar, Palette.Violet, "Sales in range", Format.Money(kpi.PeriodSales), window, Module.Invoices);
        Add(Icons.Profit, Palette.Emerald, "Gross profit", Format.Money(kpi.GrossProfit), "Sales less cost of goods", Module.Reports);
        Add(Icons.Expense, Palette.Rose, "Expenses", Format.Money(kpi.Expenses), "In the selected range", Module.Expenses);
        Add(Icons.Receivable, Palette.Amber, "Receivables", Format.Money(kpi.Receivables), "Owed as at the end date", Module.Customers);
        Add(Icons.Payable, Palette.Orange, "Payables", Format.Money(kpi.Payables), "Owed as at the end date", Module.Suppliers);
        Add(Icons.Cash, Palette.Cyan, "Cash in hand", Format.Money(kpi.CashInHand), "Across the drawers", Module.PettyCash);
        Add(Icons.LowStock, Palette.Pink, "Low stock items", Format.Qty(kpi.LowStockItems), "At or below reorder level", Module.Stock);

        _cards.ResumeLayout();
        SizeCardRows();

        void Add(string glyph, Color accent, string label, string value, string caption, Module target)
        {
            var card = new KpiCard
            {
                Glyph = glyph,
                Accent = accent,
                Label = label,
                Value = value,
                Caption = caption,
                Margin = new Padding(0, 0, Metrics.Space4, Metrics.Space4),
            };

            card.Click += (_, _) => NavigationRequested?.Invoke(this, target);
            _cards.Controls.Add(card);
        }
    }

    /// <summary>Raised when a KPI card is clicked, so the shell can open the rows behind it.</summary>
    public event EventHandler<Module>? NavigationRequested;
}
