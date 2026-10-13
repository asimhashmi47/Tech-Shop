using System.Drawing;
using Krypton.Toolkit;
using TechShop.Application.Common.Query;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// The list surface every screen shares: a toolbar with quick search and screen actions,
/// a grid, and a pager.
///
/// Paging, sorting and filtering happen in the database, not in the control. The panel
/// only ever holds one page of rows, so a shop with sixty thousand stock movements opens
/// as fast as one with sixty — and the same <see cref="QueryRequest"/> that drew the page
/// is what the export sends, so an export always mirrors what is on screen.
/// </summary>
public sealed class DataGridPanel<T> : Panel
    where T : class
{
    private const int DefaultPageSize = 25;

    private readonly DataGridView _grid;
    private readonly KryptonTextBox _search;
    private readonly FlowLayoutPanel _actions;
    private readonly Label _summary;
    private readonly AppButton _previous;
    private readonly AppButton _next;
    private readonly KryptonComboBox _pageSize;
    private readonly System.Windows.Forms.Timer _searchDebounce;
    private readonly Panel _toolbar;
    private readonly Panel _pager;
    private readonly List<GridColumn<T>> _columns = [];
    private readonly List<SortRule> _sort = [];

    private Func<QueryRequest, Task<PagedResult<T>>>? _loader;
    private IReadOnlyList<T> _rows = [];
    private int _page = 1;
    private int _totalCount;
    private bool _loading;

    public DataGridPanel()
    {
        _grid = BuildGrid();

        _search = Theme.TextBox("Search…");
        _search.Width = 280;
        _search.Location = new Point(0, Metrics.Space2);

        _searchDebounce = new System.Windows.Forms.Timer { Interval = 280 };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            _page = 1;
            await ReloadAsync().ConfigureAwait(true);
        };
        _search.TextChanged += (_, _) =>
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        };

        // Wraps onto a second row when a screen has more actions than fit beside the
        // search box. Without wrapping the panel simply ran off the right edge, and the
        // buttons added first — Export and Refresh — were invisible on every list screen.
        _actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
        };

        _toolbar = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Color.Transparent };
        _toolbar.Controls.Add(_search);
        _toolbar.Controls.Add(_actions);
        _toolbar.Resize += (_, _) => LayOutToolbar();

        _summary = new Label
        {
            AutoSize = true,
            ForeColor = Palette.InkMuted,
            Font = Typography.Get(Typography.SizeSm),
            Location = new Point(0, 14),
            BackColor = Color.Transparent,
        };

        _previous = new AppButton { Variant = ButtonVariant.Secondary, Text = Icons.ChevronLeft, Font = Icons.Font(9f), Width = 36, Height = Metrics.ControlHeightSmall };
        _next = new AppButton { Variant = ButtonVariant.Secondary, Text = Icons.ChevronRight, Font = Icons.Font(9f), Width = 36, Height = Metrics.ControlHeightSmall };
        _previous.Click += async (_, _) => await GoToPageAsync(_page - 1).ConfigureAwait(true);
        _next.Click += async (_, _) => await GoToPageAsync(_page + 1).ConfigureAwait(true);

        _pageSize = Theme.ComboBox();
        _pageSize.Width = 76;
        _pageSize.Height = Metrics.ControlHeightSmall;
        foreach (var size in new[] { 25, 50, 100, 200 })
        {
            _pageSize.Items.Add(size);
        }

        _pageSize.SelectedItem = DefaultPageSize;
        _pageSize.SelectedIndexChanged += async (_, _) =>
        {
            _page = 1;
            await ReloadAsync().ConfigureAwait(true);
        };

        _pager = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.Transparent };
        var pager = _pager;
        pager.Controls.Add(_summary);
        pager.Controls.Add(_pageSize);
        pager.Controls.Add(_previous);
        pager.Controls.Add(_next);
        pager.Resize += (_, _) =>
        {
            var y = (pager.Height - Metrics.ControlHeightSmall) / 2;
            _next.Location = new Point(pager.Width - _next.Width, y);
            _previous.Location = new Point(_next.Left - _previous.Width - Metrics.Space2, y);
            _pageSize.Location = new Point(_previous.Left - _pageSize.Width - Metrics.Space4, y);
        };

        // This panel is always the body of a card, so it fills it. Without this it falls
        // back to the Panel default of 200x100 and every list screen renders its grid as
        // a small box in the corner.
        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;

        Controls.Add(_grid);
        Controls.Add(pager);
        Controls.Add(_toolbar);
    }

    /// <summary>Raised when a row is activated, by double click or Enter.</summary>
    public event EventHandler<T>? RowActivated;

    public T? Selected => _grid.CurrentRow is { Index: >= 0 } row && row.Index < _rows.Count
        ? _rows[row.Index]
        : null;

    public int TotalCount => _totalCount;

    /// <summary>Extra per-request scoping the screen applies, such as a date range.</summary>
    public Action<QueryRequest>? Scope { get; set; }

    /// <summary>
    /// Drops the search toolbar and the pager, for a fixed snapshot embedded in another
    /// screen — the dashboard's recent invoices and low stock lists.
    ///
    /// Those show one page of a deliberately short query. A pager that can only ever say
    /// "1-10 of 10" and a search box that filters a ten-row list are chrome, and on the
    /// dashboard they were costing a hundred pixels of the rows themselves.
    /// </summary>
    public bool Compact
    {
        get => !_toolbar.Visible;
        set
        {
            _toolbar.Visible = !value;
            _pager.Visible = !value;
        }
    }

    public DataGridPanel<T> WithColumns(params GridColumn<T>[] columns)
    {
        _columns.Clear();
        _columns.AddRange(columns);
        BuildGridColumns();
        return this;
    }

    public DataGridPanel<T> WithLoader(Func<QueryRequest, Task<PagedResult<T>>> loader)
    {
        _loader = loader;
        return this;
    }

    /// <summary>Sets the initial sort. The user can change it by clicking a header.</summary>
    public DataGridPanel<T> SortedBy(string field, bool descending = false)
    {
        _sort.Clear();
        _sort.Add(new SortRule { Field = field, Desc = descending });
        return this;
    }

    public DataGridPanel<T> AddAction(AppButton button)
    {
        button.Margin = new Padding(Metrics.Space2, 0, 0, Metrics.Space2);
        button.Height = Metrics.ControlHeightSmall;
        _actions.Controls.Add(button);
        LayOutToolbar();
        return this;
    }

    /// <summary>
    /// Gives the actions the width left beside the search box and lets them wrap, then
    /// grows the toolbar to whatever height that needed.
    /// </summary>
    private void LayOutToolbar()
    {
        if (_toolbar.Width <= 0)
        {
            return;
        }

        var available = Math.Max(160, _toolbar.Width - _search.Width - Metrics.Space4);
        _actions.MaximumSize = new Size(available, 0);

        var size = _actions.PreferredSize;
        _actions.Location = new Point(Math.Max(0, _toolbar.Width - size.Width), Metrics.Space2);

        // The search box stays on the first row; the toolbar only grows if the actions
        // needed more than one.
        var wanted = Math.Max(54, size.Height + (Metrics.Space2 * 2));

        if (_toolbar.Height != wanted)
        {
            _toolbar.Height = wanted;
        }
    }

    public QueryRequest BuildRequest()
    {
        var request = new QueryRequest
        {
            Page = _page,
            PageSize = _pageSize.SelectedItem is int size ? size : DefaultPageSize,
            Search = string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text.Trim(),
            Sort = [.. _sort],
            Columns = _columns.Select(c => c.Field).ToList(),
        };

        Scope?.Invoke(request);
        return request;
    }

    public async Task ReloadAsync()
    {
        if (_loader is null || _loading)
        {
            return;
        }

        _loading = true;
        SetPagerEnabled(false);

        try
        {
            var result = await _loader(BuildRequest()).ConfigureAwait(true);
            _rows = result.Items;
            _totalCount = result.TotalCount;
            _page = result.Page;
            Render(result);
        }
        finally
        {
            _loading = false;
            SetPagerEnabled(true);
        }
    }

    private async Task GoToPageAsync(int page)
    {
        var last = LastPage();
        var target = Math.Clamp(page, 1, Math.Max(last, 1));

        if (target == _page)
        {
            return;
        }

        _page = target;
        await ReloadAsync().ConfigureAwait(true);
    }

    private int LastPage()
    {
        var size = _pageSize.SelectedItem is int s ? s : DefaultPageSize;
        return size <= 0 ? 1 : (int)Math.Ceiling(_totalCount / (double)size);
    }

    private void SetPagerEnabled(bool enabled)
    {
        var last = LastPage();
        _previous.Enabled = enabled && _page > 1;
        _next.Enabled = enabled && _page < last;
    }

    private void Render(PagedResult<T> result)
    {
        _grid.SuspendLayout();
        _grid.Rows.Clear();

        foreach (var row in result.Items)
        {
            var cells = _columns
                .Select(column => column.Chip is not null
                    ? column.Chip(row).Text
                    : column.Format?.Invoke(row) ?? column.Value(row)?.ToString() ?? string.Empty)
                .Cast<object>()
                .ToArray();

            _grid.Rows.Add(cells);
        }

        _grid.ResumeLayout();

        var first = result.TotalCount == 0 ? 0 : ((result.Page - 1) * result.PageSize) + 1;
        var lastRow = Math.Min(result.Page * result.PageSize, result.TotalCount);

        _summary.Text = result.TotalCount == 0
            ? "No rows"
            : $"{first:N0}–{lastRow:N0} of {result.TotalCount:N0}";

        _grid.ClearSelection();
    }

    private DataGridView BuildGrid()
    {
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
            AllowUserToOrderColumns = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = Metrics.GridHeaderHeight,
            ScrollBars = ScrollBars.Both,
            Font = Typography.GridBody,
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = Palette.SurfaceSunken;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.InkMuted;
        grid.ColumnHeadersDefaultCellStyle.Font = Typography.Get(Typography.Size2Xs, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(Metrics.Space3, 0, Metrics.Space3, 0);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Palette.SurfaceSunken;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Palette.InkMuted;

        grid.DefaultCellStyle.BackColor = Palette.Surface;
        grid.DefaultCellStyle.ForeColor = Palette.InkBody;
        grid.DefaultCellStyle.SelectionBackColor = Palette.PrimarySoft;
        grid.DefaultCellStyle.SelectionForeColor = Palette.InkStrong;
        grid.DefaultCellStyle.Padding = new Padding(Metrics.Space3, 0, Metrics.Space3, 0);
        grid.RowTemplate.Height = Metrics.GridRowHeight;

        // No alternating stripe. With a hairline between rows a stripe is redundant, and
        // it fights the soft tint used for the selected row.
        grid.AlternatingRowsDefaultCellStyle.BackColor = Palette.Surface;

        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _rows.Count)
            {
                RowActivated?.Invoke(this, _rows[e.RowIndex]);
            }
        };

        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && Selected is { } selected)
            {
                RowActivated?.Invoke(this, selected);
                e.Handled = true;
            }
        };

        grid.ColumnHeaderMouseClick += async (_, e) => await SortByAsync(e.ColumnIndex).ConfigureAwait(true);
        grid.CellFormatting += OnCellFormatting;

        return grid;
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count || e.ColumnIndex < 0 || e.ColumnIndex >= _columns.Count)
        {
            return;
        }

        var column = _columns[e.ColumnIndex];
        var row = _rows[e.RowIndex];

        if (column.Chip is not null)
        {
            var (_, _, ink) = column.Chip(row);
            e.CellStyle!.ForeColor = ink;
            e.CellStyle.Font = Typography.Get(Typography.Size2Xs, FontStyle.Bold);
            return;
        }

        if (column.Foreground?.Invoke(row) is { } colour)
        {
            e.CellStyle!.ForeColor = colour;
        }
    }

    private void BuildGridColumns()
    {
        _grid.Columns.Clear();

        foreach (var column in _columns)
        {
            var view = new DataGridViewTextBoxColumn
            {
                Name = column.Field,
                HeaderText = column.Header,
                SortMode = DataGridViewColumnSortMode.Programmatic,
                Width = column.Width,
                AutoSizeMode = column.Fill
                    ? DataGridViewAutoSizeColumnMode.Fill
                    : DataGridViewAutoSizeColumnMode.None,
            };

            view.DefaultCellStyle.Alignment = column.Align switch
            {
                ContentAlignment.MiddleRight => DataGridViewContentAlignment.MiddleRight,
                ContentAlignment.MiddleCenter => DataGridViewContentAlignment.MiddleCenter,
                _ => DataGridViewContentAlignment.MiddleLeft,
            };

            view.HeaderCell.Style.Alignment = view.DefaultCellStyle.Alignment;

            if (column.Numeric)
            {
                view.DefaultCellStyle.Font = Typography.Numeric(Typography.SizeSm);

                // Tighter side padding than a text column: the mono face is wide, and
                // every pixel spent on padding is one a long total cannot use.
                view.DefaultCellStyle.Padding = new Padding(Metrics.Space2, 0, Metrics.Space2, 0);
            }

            _grid.Columns.Add(view);
        }

        UpdateSortIndicators();
    }

    private async Task SortByAsync(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Count)
        {
            return;
        }

        var column = _columns[columnIndex];
        if (!column.Sortable)
        {
            return;
        }

        // One click sorts ascending, a second flips it. Multi-column sort is not offered:
        // the whitelist supports it, but a grid of fifteen columns makes it a trap.
        var existing = _sort.FirstOrDefault(s =>
            string.Equals(s.Field, column.Field, StringComparison.OrdinalIgnoreCase));

        _sort.Clear();
        _sort.Add(new SortRule { Field = column.Field, Desc = existing is { Desc: false } });

        _page = 1;
        UpdateSortIndicators();
        await ReloadAsync().ConfigureAwait(true);
    }

    private void UpdateSortIndicators()
    {
        var active = _sort.FirstOrDefault();

        foreach (DataGridViewColumn view in _grid.Columns)
        {
            var match = active is not null
                && string.Equals(view.Name, active.Field, StringComparison.OrdinalIgnoreCase);

            var source = _columns.FirstOrDefault(c =>
                string.Equals(c.Field, view.Name, StringComparison.Ordinal));

            if (source is null)
            {
                continue;
            }

            // A caret in the header text, rather than the platform glyph, which does not
            // honour the header colours this grid sets.
            view.HeaderText = match
                ? $"{source.Header}  {(active!.Desc ? "▾" : "▴")}"
                : source.Header;

            view.HeaderCell.Style.ForeColor = match ? Palette.PrimaryText : Palette.InkMuted;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchDebounce.Stop();
            _searchDebounce.Dispose();
        }

        base.Dispose(disposing);
    }
}
