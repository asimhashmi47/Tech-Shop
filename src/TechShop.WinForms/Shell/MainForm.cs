using System.Drawing;
using TechShop.Application.Features.Auth;
using TechShop.Domain.Enums;
using TechShop.Infrastructure.Services;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Shell;

/// <summary>
/// The shell: rail on the left, shop picker and identity along the top, one page in the
/// middle, footer underneath.
///
/// Pages are created on first visit and then kept, so going back to a screen does not
/// re-query everything. They are disposed with the shell, which is also when their DI
/// scopes and change trackers go.
/// </summary>
public sealed class MainForm : Form, IMessageFilter
{
    private readonly SideNav _nav = new();
    private readonly TopBar _topBar = new();
    private readonly Panel _content;
    private readonly Dictionary<Module, PageBase> _pages = [];
    private readonly UserSession _session;

    /// <summary>
    /// Watches for the session ageing out. It ticks far more often than the timeout so
    /// the lock arrives promptly rather than up to a timeout late.
    /// </summary>
    private readonly System.Windows.Forms.Timer _idleCheck = new() { Interval = 15_000 };

    private UserProfile? _profile;
    private PageBase? _current;

    public MainForm()
    {
        _session = AppHost.Get<UserSession>();

        Text = "TechShop";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 720);
        ClientSize = new Size(1440, 880);
        WindowState = FormWindowState.Maximized;
        BackColor = Palette.AppBackground;
        Font = Typography.Body;
        KeyPreview = true;

        _content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.AppBackground,
            Padding = new Padding(Metrics.Space6, Metrics.Space5, Metrics.Space6, Metrics.Space5),
            AutoScroll = true,
        };

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Palette.AppBackground };
        body.Controls.Add(_content);
        body.Controls.Add(_topBar);

        // Added last so the footer spans the full width beneath the rail as well.
        Controls.Add(body);
        Controls.Add(_nav);
        Controls.Add(new AppFooter());

        _nav.Navigated += (_, module) => Navigate(module);
        _topBar.ShopChanged += async (_, shopId) => await OnShopChangedAsync(shopId).ConfigureAwait(true);
        _topBar.SignOutRequested += async (_, _) => await SignOutAsync().ConfigureAwait(true);

        _idleCheck.Tick += (_, _) => LockIfIdle();

        Load += async (_, _) => await StartAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Every keypress and mouse click in the process counts as the operator being
    /// present. Filtering messages is the only way to see input aimed at any control,
    /// rather than wiring an event onto each one.
    /// </summary>
    public bool PreFilterMessage(ref Message m)
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_MOUSEWHEEL = 0x020A;

        if (m.Msg is WM_KEYDOWN or WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MOUSEWHEEL)
        {
            _session.Touch();
        }

        // Never swallow the message: this only observes.
        return false;
    }

    /// <summary>
    /// Closes the shell back to the login screen when the till has been left alone.
    /// The services refuse an idle session anyway; this is what makes that visible
    /// instead of surfacing as a baffling refusal twenty minutes later.
    /// </summary>
    private void LockIfIdle()
    {
        if (!_session.IsIdle)
        {
            return;
        }

        _idleCheck.Stop();
        _session.SignOut();
        SignOutRequested = true;
        Close();
    }

    /// <summary>True when the shell closed because the operator signed out, not quit.</summary>
    public bool SignOutRequested { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        System.Windows.Forms.Application.AddMessageFilter(this);
        _session.Touch();
        _idleCheck.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _idleCheck.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task StartAsync()
    {
        try
        {
            _profile = await AppHost.InScopeAsync<AuthService, UserProfile>(auth => auth.MeAsync())
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Dialogs.Error(this, "Could not load your profile", ex.Message);
            Close();
            return;
        }

        _topBar.SetUser(_profile.FullName, _profile.Role == UserRole.Admin ? "Administrator" : "Standard user");

        var choices = new List<ShopChoice>();
        if (_profile.Shops.Count > 1)
        {
            choices.Add(new ShopChoice(null, "All shops"));
        }

        choices.AddRange(_profile.Shops.Select(s => new ShopChoice(s.Id, s.Name)));
        _topBar.SetShops(choices, _session.ActiveShopId);

        BuildNav();

        var landing = Readable().FirstOrDefault()?.Module;
        if (landing is null)
        {
            Dialogs.Warn(
                this,
                "No access",
                "This account has not been granted access to any screen yet. Ask an administrator to set its permissions.");
            Close();
            return;
        }

        Navigate(landing.Value);
    }

    private void BuildNav() => _nav.Build(Readable().ToList());

    /// <summary>
    /// The rail shows only what this account may read. The service layer refuses the call
    /// either way, so this is courtesy rather than security — but a menu of rows that all
    /// error on click is a worse product than a short menu.
    /// </summary>
    private IEnumerable<NavEntry> Readable()
    {
        if (_profile is null)
        {
            return [];
        }

        if (_profile.Role == UserRole.Admin)
        {
            return NavRegistry.All;
        }

        var allowed = _profile.Permissions
            .Where(p => p.CanRead)
            .Select(p => p.Module)
            .ToHashSet();

        return NavRegistry.All.Where(entry => allowed.Contains(entry.Module));
    }

    private bool CanWrite(Module module)
        => _profile is not null
            && (_profile.Role == UserRole.Admin
                || _profile.Permissions.Any(p => p.Module == module && p.CanWrite));

    private void Navigate(Module module)
    {
        if (_current?.Module == module)
        {
            return;
        }

        if (!_pages.TryGetValue(module, out var page))
        {
            page = PageFactory.Create(module);

            // A KPI card is a link to the rows behind the figure, but only to screens
            // this account may actually read.
            if (page is Features.Dashboard.DashboardPage dashboard)
            {
                dashboard.NavigationRequested += (_, target) =>
                {
                    if (Readable().Any(entry => entry.Module == target))
                    {
                        Navigate(target);
                    }
                };
            }

            _pages[module] = page;
        }

        _content.SuspendLayout();

        if (_current is not null)
        {
            _current.Visible = false;
        }

        if (!_content.Controls.Contains(page))
        {
            _content.Controls.Add(page);
        }

        page.Visible = true;
        page.BringToFront();
        _current = page;

        _content.ResumeLayout();
        _nav.SetActive(module);

        _ = page.InitialiseAsync(CanWrite(module));
    }

    private async Task OnShopChangedAsync(long? shopId)
    {
        _session.ActiveShopId = shopId;

        // Pages already built are stale now; the visible one is refreshed immediately and
        // the rest are dropped so they reload with the new scope when next opened.
        foreach (var (module, page) in _pages.ToList())
        {
            if (page == _current)
            {
                continue;
            }

            _content.Controls.Remove(page);
            page.Dispose();
            _pages.Remove(module);
        }

        if (_current is not null)
        {
            await _current.ShopChangedAsync().ConfigureAwait(true);
        }
    }

    private async Task SignOutAsync()
    {
        if (!Dialogs.Confirm(this, "Sign out", $"Sign {_profile?.FullName ?? "out"} out of TechShop?"))
        {
            return;
        }

        try
        {
            await AppHost.InScopeAsync<AuthService>(auth => auth.LogoutAsync()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Signing out must not be blocked by a logging failure.
            Toast.Show(this, $"Signed out, but the audit entry failed: {ex.Message}", ToastTone.Warning);
        }

        _session.SignOut();
        SignOutRequested = true;
        Close();
    }

    // ---------------------------------------------------------------- test seams

    /// <summary>
    /// True once the signed-in profile has loaded and the rail has been built. The
    /// shell fills itself from an async Load handler, so a screenshot taken before
    /// this is of an empty window.
    /// </summary>
    internal bool ReadyForTest => _profile is not null;

    internal PageBase? CurrentPageForTest => _current;

    internal IReadOnlyList<NavEntry> NavigableForTest => Readable().ToList();

    internal void NavigateForTest(Module module) => Navigate(module);

    /// <summary>Picks a shop the way the top bar's picker does.</summary>
    internal async Task UseShopForTest(long? shopId)
    {
        _session.ActiveShopId = shopId;
        _topBar.SelectShop(shopId);

        if (_current is not null)
        {
            await _current.ShopChangedAsync().ConfigureAwait(true);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _idleCheck.Stop();
        System.Windows.Forms.Application.RemoveMessageFilter(this);

        foreach (var page in _pages.Values)
        {
            page.Dispose();
        }

        _pages.Clear();
        base.OnFormClosed(e);
    }
}
