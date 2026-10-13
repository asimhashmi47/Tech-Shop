using System.Drawing;
using TechShop.Application.Features.Reports;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;
using Row = TechShop.WinForms.Controls.StatementView.Row;
using RowKind = TechShop.WinForms.Controls.StatementView.RowKind;

namespace TechShop.WinForms.Features.Reports;

/// <summary>
/// The four statements, over one date range and one shop selection: a period summary,
/// profit and loss, the balance sheet and a direct-method cash flow.
///
/// They share a range and a tab strip rather than being four screens, because the first
/// thing anyone does with one of them is check it against another.
/// </summary>
public sealed class ReportsPage : PageBase
{
    private readonly PageHeader _header;
    private readonly DateRangeBar _range = new();
    private readonly TabControl _tabs;

    private readonly DataGridPanel<SummaryRowDto> _summary = new();
    private readonly StatementView _profitAndLoss = new() { Dock = DockStyle.Fill };
    private readonly StatementView _balanceSheet = new() { Dock = DockStyle.Fill };
    private readonly StatementView _cashFlow = new() { Dock = DockStyle.Fill };
    private readonly AppButton _monthly;

    private bool _showMonthly;

    public ReportsPage()
        : base(Module.Reports, "Reports")
    {
        _header = new PageHeader("Reports");

        _monthly = new AppButton { Variant = ButtonVariant.Secondary, Text = "Monthly" };
        _monthly.FitToText();
        _monthly.Click += async (_, _) =>
        {
            _showMonthly = !_showMonthly;
            _monthly.Text = _showMonthly ? "Monthly" : "Daily";
            _monthly.Variant = _showMonthly ? ButtonVariant.Primary : ButtonVariant.Secondary;
            await ReloadAsync().ConfigureAwait(true);
        };
        _header.AddAction(_monthly);

        var export = new AppButton { Variant = ButtonVariant.Secondary, Text = "Export summary" };
        export.FitToText();
        export.Click += async (_, _) => await ExportAsync().ConfigureAwait(true);
        _header.AddAction(export);

        _range.Changed += async (_, _) => await ReloadAsync().ConfigureAwait(true);

        BuildSummaryGrid();

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Appearance = TabAppearance.Normal,
            Font = Typography.Get(Typography.SizeBase),
            ItemSize = new Size(150, 32),
            SizeMode = TabSizeMode.Fixed,
            Padding = new Point(Metrics.Space3, Metrics.Space1),
        };

        _tabs.TabPages.Add(Tab("Summary", _summary));
        _tabs.TabPages.Add(Tab("Profit and loss", _profitAndLoss));
        _tabs.TabPages.Add(Tab("Balance sheet", _balanceSheet));
        _tabs.TabPages.Add(Tab("Cash flow", _cashFlow));

        var card = Card();
        card.Controls.Add(_tabs);

        var spacer = new Panel { Dock = DockStyle.Top, Height = Metrics.Space4, BackColor = Color.Transparent };

        Controls.Add(card);
        Controls.Add(spacer);
        Controls.Add(_range);
        Controls.Add(_header);
    }

    private static TabPage Tab(string title, Control body)
    {
        var page = new TabPage(title)
        {
            BackColor = Palette.Surface,
            Padding = new Padding(Metrics.Space2),
        };

        body.Dock = DockStyle.Fill;
        page.Controls.Add(body);
        return page;
    }

    private void BuildSummaryGrid()
    {
        _summary
            .WithColumns(
                new GridColumn<SummaryRowDto>("period", "Period", r => r.Period) { Width = 140, Sortable = false },
                Money("sales", "Sales", r => r.Sales),
                Money("discount", "Discount", r => r.Discount),
                Money("cogs", "Cost of goods", r => r.Cogs),
                Money("grossProfit", "Gross profit", r => r.GrossProfit),
                Money("expenses", "Expenses", r => r.Expenses),
                new GridColumn<SummaryRowDto>("netProfit", Format.MoneyHeader("Net profit"), r => r.NetProfit)
                {
                    Width = 140,
                    Align = ContentAlignment.MiddleRight,
                    Numeric = true,
                    Sortable = false,
                    Format = r => Format.Amount(r.NetProfit),

                    // The one figure on this grid someone is actually looking for.
                    Foreground = r => r.NetProfit < 0 ? Palette.DangerText : Palette.EmeraldText,
                },
                new GridColumn<SummaryRowDto>("invoiceCount", "Invoices", r => r.InvoiceCount)
                {
                    Width = 90,
                    Align = ContentAlignment.MiddleRight,
                    Numeric = true,
                    Sortable = false,
                    Format = r => Format.Qty(r.InvoiceCount),
                })
            .WithLoader(request =>
            {
                // The report services return a whole period at once rather than a page,
                // so the rows are wrapped to fit the grid's contract.
                var filter = Filter();
                var rows = _showMonthly
                    ? Service<ReportService>().MonthlyAsync(filter)
                    : Service<ReportService>().DailyAsync(filter);

                return Wrap(rows);
            });
    }

    private static GridColumn<SummaryRowDto> Money(
        string field,
        string header,
        Func<SummaryRowDto, decimal> value) => new(field, Format.MoneyHeader(header), r => value(r))
        {
            Width = 130,
            Align = ContentAlignment.MiddleRight,
            Numeric = true,
            Sortable = false,
            Format = r => Format.Amount(value(r)),
        };

    private static async Task<Application.Common.Query.PagedResult<SummaryRowDto>> Wrap(
        Task<List<SummaryRowDto>> rows)
    {
        var list = await rows.ConfigureAwait(true);

        return new Application.Common.Query.PagedResult<SummaryRowDto>
        {
            Items = list,
            Page = 1,
            PageSize = Math.Max(list.Count, 1),
            TotalCount = list.Count,
        };
    }

    private ReportFilter Filter() => new(ActiveShopId, _range.FromUtc, _range.ToUtc);

    protected override async Task LoadAsync()
    {
        var scope = ActiveShopId is null ? "All shops" : "Selected shop";
        _header.Subtitle = _range.Describe(scope);

        var reports = Service<ReportService>();
        var filter = Filter();

        await _summary.ReloadAsync().ConfigureAwait(true);

        ShowProfitAndLoss(await reports.ProfitAndLossAsync(filter).ConfigureAwait(true));
        ShowBalanceSheet(await reports.BalanceSheetAsync(filter).ConfigureAwait(true));
        ShowCashFlow(await reports.CashFlowAsync(filter).ConfigureAwait(true));
    }

    private void ShowProfitAndLoss(ProfitAndLossDto report)
    {
        var rows = new List<Row>
        {
            new(RowKind.Heading, "Trading"),
            new(RowKind.Line, "Sales, net of tax", report.Revenue),
            new(RowKind.Line, "Less discount given", -report.Discount),
            new(RowKind.Line, "Less cost of goods sold", -report.Cogs),
            new(RowKind.Subtotal, "Gross profit", report.GrossProfit),
            new(RowKind.Gap, string.Empty),
            new(RowKind.Heading, "Expenses"),
        };

        if (report.ExpenseLines.Count == 0)
        {
            rows.Add(new Row(RowKind.Line, "No expenses in this period", 0m));
        }
        else
        {
            rows.AddRange(report.ExpenseLines.Select(line => new Row(RowKind.Line, line.Name, -line.Amount)));
        }

        rows.Add(new Row(RowKind.Subtotal, "Total expenses", -report.Expenses));
        rows.Add(new Row(RowKind.Gap, string.Empty));
        rows.Add(new Row(RowKind.Total, "Net profit", report.NetProfit)
        {
            Colour = report.NetProfit < 0 ? Palette.DangerText : Palette.EmeraldText,
        });

        _profitAndLoss.Show(
            "Profit and loss",
            $"{Format.Date(report.From)} to {Format.Date(report.To)}",
            rows);
    }

    private void ShowBalanceSheet(BalanceSheetDto report)
    {
        // Assets equal liabilities plus equity by construction here, and the statement
        // says so explicitly rather than leaving the reader to add it up.
        var balances = report.TotalAssets - (report.TotalLiabilities + report.OwnerEquity);

        _balanceSheet.Show(
            "Balance sheet",
            $"As at {Format.Date(report.AsAt)}",
            [
                new Row(RowKind.Heading, "Assets"),
                new Row(RowKind.Line, "Cash and bank", report.CashAndBank),
                new Row(RowKind.Line, "Receivables", report.Receivables),
                new Row(RowKind.Line, "Inventory at cost", report.Inventory),
                new Row(RowKind.Subtotal, "Total assets", report.TotalAssets),
                new Row(RowKind.Gap, string.Empty),
                new Row(RowKind.Heading, "Liabilities"),
                new Row(RowKind.Line, "Payables", report.Payables),
                new Row(RowKind.Line, "Sales tax collected, not yet paid over", report.TaxPayable),
                new Row(RowKind.Subtotal, "Total liabilities", report.TotalLiabilities),
                new Row(RowKind.Gap, string.Empty),
                new Row(RowKind.Heading, "Equity"),
                new Row(RowKind.Line, "Owner equity", report.OwnerEquity),
                new Row(RowKind.Gap, string.Empty),
                new Row(RowKind.Total, "Liabilities and equity", report.TotalLiabilities + report.OwnerEquity),
                new Row(
                    RowKind.Line,
                    Math.Abs(balances) < 0.01m ? "Balanced" : "OUT OF BALANCE",
                    Math.Abs(balances) < 0.01m ? null : balances)
                {
                    Colour = Math.Abs(balances) < 0.01m ? Palette.EmeraldText : Palette.DangerText,
                },
            ]);
    }

    private void ShowCashFlow(CashFlowDto report)
    {
        _cashFlow.Show(
            "Cash flow",
            $"{Format.Date(report.From)} to {Format.Date(report.To)}, direct method",
            [
                new Row(RowKind.Line, "Opening cash", report.OpeningCash),
                new Row(RowKind.Gap, string.Empty),
                new Row(RowKind.Heading, "Cash in"),
                new Row(RowKind.Line, "Received from customers", report.CustomerReceipts),
                new Row(RowKind.Line, "Other cash in", report.OtherCashIn),
                new Row(RowKind.Subtotal, "Total in", report.TotalInflow),
                new Row(RowKind.Gap, string.Empty),
                new Row(RowKind.Heading, "Cash out"),
                new Row(RowKind.Line, "Paid to suppliers", -report.SupplierPayments),
                new Row(RowKind.Line, "Expenses paid", -report.ExpensePayments),
                new Row(RowKind.Line, "Other cash out", -report.OtherCashOut),
                new Row(RowKind.Subtotal, "Total out", -report.TotalOutflow),
                new Row(RowKind.Gap, string.Empty),
                new Row(RowKind.Subtotal, "Net movement", report.NetCashFlow)
                {
                    Colour = report.NetCashFlow < 0 ? Palette.DangerText : Palette.EmeraldText,
                },
                new Row(RowKind.Total, "Closing cash", report.ClosingCash),
            ]);
    }

    private async Task ExportAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export report summary",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"techshop-summary-{DateTime.Now:yyyyMMdd}.xlsx",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        await TryAsync(
            async () =>
            {
                var bytes = await Service<ReportService>()
                    .ExportSummaryAsync(Filter(), _showMonthly)
                    .ConfigureAwait(true);

                await File.WriteAllBytesAsync(dialog.FileName, bytes).ConfigureAwait(true);
            },
            "Summary exported").ConfigureAwait(true);
    }
}
