using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Exceptions;
using TechShop.Domain.Enums;
using TechShop.Infrastructure.Services;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Shell;

/// <summary>
/// Base for every screen.
///
/// A page owns one DI scope, and therefore one EF Core change tracker, for as long as it
/// is open. That is the unit of work: edits made on a screen do not leak into another
/// screen's context, and closing the screen disposes everything it loaded.
///
/// Pages are also told when the shop picker or the date range changes, so the shell can
/// re-run a query without each screen subscribing to the top bar itself.
/// </summary>
public abstract class PageBase : UserControl
{
    private IServiceScope? _scope;
    private bool _loaded;

    protected PageBase(Module module, string title, string subtitle = "")
    {
        Module = module;
        Title = title;
        Subtitle = subtitle;

        Dock = DockStyle.Fill;
        BackColor = Palette.AppBackground;
        Font = Typography.Body;

        // A page does not scroll as a whole. Each screen docks a header and filters to
        // the top and lets its grid fill what is left, and the grid scrolls internally.
        // Page-level scrolling put a scrollbar on screens that fit, and moved the header
        // off the top when it did scroll.
        AutoScroll = false;
    }

    public Module Module { get; }

    public string Title { get; }

    public string Subtitle { get; protected set; }

    /// <summary>The shop the shell currently has selected. Null means every shop the user may see.</summary>
    public static long? ActiveShopId => Session.ActiveShopId;

    protected static UserSession Session => AppHost.Get<UserSession>();

    protected IServiceProvider Scope => (_scope ??= AppHost.CreateScope()).ServiceProvider;

    protected T Service<T>() where T : notnull => Scope.GetRequiredService<T>();

    /// <summary>True when the signed-in user may change anything on this screen.</summary>
    protected bool CanWrite { get; private set; }

    /// <summary>Called once, after the page is on screen and its permissions are known.</summary>
    protected abstract Task LoadAsync();

    /// <summary>Called when the shop picker changes. Default is a full reload.</summary>
    public virtual Task ShopChangedAsync() => ReloadAsync();

    public async Task ReloadAsync()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    public async Task InitialiseAsync(bool canWrite)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        CanWrite = canWrite;
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Turns an exception from the service layer into something an operator can act on.
    /// A permission refusal and a validation failure are normal outcomes, not crashes, so
    /// they get a plain message; anything else keeps its detail for a support call.
    /// </summary>
    protected void ShowFailure(Exception ex)
    {
        switch (ex)
        {
            case ForbiddenException forbidden:
                Dialogs.Warn(this, "Not allowed", forbidden.Message);
                break;

            case ValidationFailedException validation:
                var lines = validation.Errors
                    .SelectMany(kv => kv.Value.Select(message => $"• {message}"))
                    .ToList();
                Dialogs.Warn(this, "Check the form", string.Join(Environment.NewLine, lines));
                break;

            case NotFoundException notFound:
                Dialogs.Warn(this, "Not found", notFound.Message);
                break;

            case ConflictException conflict:
                Dialogs.Warn(this, "Cannot continue", conflict.Message);
                break;

            default:
                Dialogs.Error(this, "Something went wrong", ex.Message);
                break;
        }
    }

    /// <summary>Runs a write and reloads on success, reporting any failure in place.</summary>
    protected async Task<bool> TryAsync(Func<Task> work, string? successMessage = null)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            await work().ConfigureAwait(true);

            if (successMessage is not null)
            {
                Toast.Show(FindForm(), successMessage);
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
            return false;
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>A standard content container: white card on the sunken app background.</summary>
    protected static Panel Card(DockStyle dock = DockStyle.Fill)
    {
        var card = new Panel
        {
            Dock = dock,
            BackColor = Palette.Surface,
            Padding = new Padding(Metrics.Space5),
            Margin = new Padding(0),
        };

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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scope?.Dispose();
            _scope = null;
        }

        base.Dispose(disposing);
    }
}
