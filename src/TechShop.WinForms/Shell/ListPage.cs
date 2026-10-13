using System.Drawing;
using TechShop.Application.Common.Query;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Shell;

/// <summary>
/// The shape almost every screen in this app takes: a title, an optional date range, a
/// server-side grid, and the actions that apply to it.
///
/// Subclasses declare their columns and the service call that fills them, and opt into
/// New / Edit / Delete by overriding. Everything else — permission gating on the write
/// buttons, the export file dialog, reloading when the shop picker changes, turning a
/// service exception into a message an operator can act on — happens once, here.
/// </summary>
public abstract class ListPage<T> : PageBase
    where T : class
{
    private readonly bool _withDateRange;
    private AppButton? _new;
    private AppButton? _edit;
    private AppButton? _delete;

    protected ListPage(Module module, string title, string subtitle = "", bool withDateRange = false)
        : base(module, title, subtitle)
    {
        _withDateRange = withDateRange;
        Header = new PageHeader(title, subtitle);
        Grid = new DataGridPanel<T>();
    }

    protected PageHeader Header { get; }

    protected DataGridPanel<T> Grid { get; }

    protected DateRangeBar? Range { get; private set; }

    /// <summary>The field the grid sorts by before the operator touches a header.</summary>
    protected abstract string DefaultSortField { get; }

    protected virtual bool DefaultSortDescending => true;

    /// <summary>The noun used in buttons and confirmations, for example "product".</summary>
    protected virtual string ItemNoun => Title.TrimEnd('s').ToLowerInvariant();

    protected abstract IReadOnlyList<GridColumn<T>> Columns();

    protected abstract Task<PagedResult<T>> QueryAsync(QueryRequest request);

    /// <summary>Return null for a screen whose service offers no export.</summary>
    protected virtual Func<QueryRequest, Task<byte[]>>? Exporter => null;

    /// <summary>Override to offer New. The base returns false, so the button is hidden.</summary>
    protected virtual bool SupportsCreate => false;

    protected virtual bool SupportsEdit => false;

    protected virtual bool SupportsDelete => false;

    protected virtual Task OnNewAsync() => Task.CompletedTask;

    protected virtual Task OnEditAsync(T row) => Task.CompletedTask;

    protected virtual Task OnDeleteAsync(T row) => Task.CompletedTask;

    /// <summary>How a row is named in a delete confirmation.</summary>
    protected virtual string Describe(T row) => row.ToString() ?? ItemNoun;

    /// <summary>Extra per-request scoping, on top of the shop and the date range.</summary>
    protected virtual void ScopeRequest(QueryRequest request)
    {
    }

    /// <summary>Called after each successful load, for a screen with a summary strip.</summary>
    protected virtual Task AfterLoadAsync() => Task.CompletedTask;

    protected sealed override async Task LoadAsync()
    {
        if (!Built)
        {
            Build();
        }

        await Grid.ReloadAsync().ConfigureAwait(true);
        await AfterLoadAsync().ConfigureAwait(true);
        UpdateSubtitle();
    }

    protected bool Built { get; private set; }

    private void Build()
    {
        Grid.WithColumns([.. Columns()])
            .SortedBy(DefaultSortField, DefaultSortDescending)
            .WithLoader(QueryAsync);

        Grid.Scope = request =>
        {
            request.ShopId = ActiveShopId;

            if (Range is not null)
            {
                request.FromDate = Range.FromUtc;
                request.ToDate = Range.ToUtc;
            }

            ScopeRequest(request);
        };

        if (SupportsEdit)
        {
            Grid.RowActivated += async (_, row) =>
            {
                if (CanWrite)
                {
                    await TryAsync(() => OnEditAsync(row)).ConfigureAwait(true);
                    await Grid.ReloadAsync().ConfigureAwait(true);
                }
            };
        }

        BuildActions();

        var card = Card();
        card.Controls.Add(Grid);

        if (_withDateRange)
        {
            Range = new DateRangeBar { Margin = new Padding(0, 0, 0, Metrics.Space4) };
            Range.Changed += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        }

        // Docked top-first, so these are added in reverse of how they appear.
        Controls.Add(card);

        if (Range is not null)
        {
            var spacer = new Panel { Dock = DockStyle.Top, Height = Metrics.Space4, BackColor = Color.Transparent };
            Controls.Add(spacer);
            Controls.Add(Range);
        }

        Controls.Add(Header);
        Built = true;
    }

    private void BuildActions()
    {
        // Export sits furthest from the primary action, because it is the one button here
        // that cannot change anything.
        if (Exporter is not null)
        {
            var export = Ghost("Export");
            export.Click += async (_, _) => await ExportAsync().ConfigureAwait(true);
            Grid.AddAction(export);
        }

        var refresh = Ghost("Refresh");
        refresh.Click += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        Grid.AddAction(refresh);

        if (SupportsDelete)
        {
            _delete = Ghost("Delete");
            _delete.Click += async (_, _) => await DeleteSelectedAsync().ConfigureAwait(true);
            Grid.AddAction(_delete);
        }

        if (SupportsEdit)
        {
            _edit = Ghost("Edit");
            _edit.Click += async (_, _) => await EditSelectedAsync().ConfigureAwait(true);
            Grid.AddAction(_edit);
        }

        if (SupportsCreate)
        {
            _new = new AppButton { Variant = ButtonVariant.Primary, Text = $"New {ItemNoun}" };
            _new.FitToText();
            _new.Click += async (_, _) =>
            {
                await TryAsync(OnNewAsync).ConfigureAwait(true);
                await Grid.ReloadAsync().ConfigureAwait(true);
            };
            Grid.AddAction(_new);
        }

        // Read-only accounts see the screen; they do not see buttons that would be
        // refused by the service layer the moment they were pressed.
        foreach (var button in new[] { _new, _edit, _delete })
        {
            if (button is not null)
            {
                button.Visible = CanWrite;
            }
        }
    }

    private static AppButton Ghost(string text)
    {
        var button = new AppButton { Variant = ButtonVariant.Secondary, Text = text };
        button.FitToText(Metrics.Space3);
        return button;
    }

    private async Task EditSelectedAsync()
    {
        if (Grid.Selected is not { } row)
        {
            Dialogs.Info(FindForm(), "Nothing selected", $"Choose a {ItemNoun} to edit.");
            return;
        }

        await TryAsync(() => OnEditAsync(row)).ConfigureAwait(true);
        await Grid.ReloadAsync().ConfigureAwait(true);
    }

    private async Task DeleteSelectedAsync()
    {
        if (Grid.Selected is not { } row)
        {
            Dialogs.Info(FindForm(), "Nothing selected", $"Choose a {ItemNoun} to remove.");
            return;
        }

        if (!Dialogs.ConfirmDestructive(
            FindForm(),
            $"Remove {ItemNoun}",
            $"Remove {Describe(row)}?\n\nThis cannot be undone."))
        {
            return;
        }

        if (await TryAsync(() => OnDeleteAsync(row), $"{Describe(row)} removed").ConfigureAwait(true))
        {
            await Grid.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task ExportAsync()
    {
        if (Exporter is not { } exporter)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = $"Export {Title}",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"{Title.Replace(' ', '-').ToLowerInvariant()}-{DateTime.Now:yyyyMMdd}.xlsx",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        await TryAsync(
            async () =>
            {
                // The export sends the request that drew the page, so the workbook has
                // the same columns, filters and sort the operator is looking at.
                var bytes = await exporter(Grid.BuildRequest()).ConfigureAwait(true);
                await File.WriteAllBytesAsync(dialog.FileName, bytes).ConfigureAwait(true);
            },
            $"Exported {Grid.TotalCount:N0} rows").ConfigureAwait(true);
    }

    private void UpdateSubtitle()
    {
        var scope = ActiveShopId is null ? "All shops" : "Selected shop";
        var count = $"{Grid.TotalCount:N0} {(Grid.TotalCount == 1 ? ItemNoun : ItemNoun + "s")}";

        Header.Subtitle = Range is not null
            ? $"{Range.Describe(scope)} · {count}"
            : $"{scope} · {count}";
    }
}
