using System.Drawing;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Shell;

/// <summary>
/// Stands in for a screen that has not been built yet. It exists so the rail is never
/// wired to a null page during the build; every module it covers is replaced before the
/// app ships.
/// </summary>
public sealed class PlaceholderPage : PageBase
{
    public PlaceholderPage(Module module, string title)
        : base(module, title)
    {
        var header = new PageHeader(title, "This screen is still being built.");

        var card = Card();
        var message = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"{title} is not available yet.",
            Font = Typography.Get(Typography.SizeMd),
            ForeColor = Palette.InkMuted,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        card.Controls.Add(message);

        Controls.Add(card);
        Controls.Add(header);
    }

    protected override Task LoadAsync() => Task.CompletedTask;
}
