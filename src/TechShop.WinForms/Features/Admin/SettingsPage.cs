using System.Drawing;
using Krypton.Toolkit;
using TechShop.Application.Features.Auth;
using TechShop.Application.Features.Settings;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Admin;

/// <summary>
/// Application settings and the signed-in operator's own password.
///
/// Currency lives here rather than on each screen because it applies to every money
/// figure in the app; changing it reformats the whole product without touching data.
/// </summary>
public sealed class SettingsPage : PageBase
{
    private readonly PageHeader _header;
    private readonly KryptonComboBox _currency;
    private readonly Label _currencyPreview;
    private readonly AppButton _saveCurrency;

    private readonly KryptonTextBox _currentPassword;
    private readonly KryptonTextBox _newPassword;
    private readonly KryptonTextBox _confirmPassword;
    private readonly Label _passwordError;

    public SettingsPage()
        : base(Module.Settings, "Settings", "Applies to every screen")
    {
        _header = new PageHeader("Settings", "Applies to every screen");

        // ---------- currency
        var currencyCard = Card(DockStyle.Top);
        currencyCard.Height = 210;

        var currencyHeading = Heading("Currency", "Chosen once and applied to every money figure in the app.");

        _currency = Theme.ComboBox();
        _currency.Width = 320;
        _currency.Location = new Point(Metrics.Space5, 92);

        foreach (var option in SettingsService.Available)
        {
            _currency.Items.Add(new CurrencyItem(option));
        }

        _currency.SelectedIndexChanged += (_, _) => UpdatePreview();

        var currencyLabel = Theme.FieldLabel("Currency");
        currencyLabel.Location = new Point(Metrics.Space5, 72);

        _currencyPreview = new Label
        {
            AutoSize = false,
            Size = new Size(420, 24),
            Location = new Point(Metrics.Space5 + 340, 96),
            ForeColor = Palette.InkMuted,
            Font = Typography.Numeric(Typography.SizeMd),
            BackColor = Color.Transparent,
        };

        _saveCurrency = new AppButton { Variant = ButtonVariant.Primary, Text = "Save currency", Width = 150 };
        _saveCurrency.Location = new Point(Metrics.Space5, 142);
        _saveCurrency.Click += async (_, _) => await SaveCurrencyAsync().ConfigureAwait(true);

        currencyCard.Controls.AddRange([currencyHeading, currencyLabel, _currency, _currencyPreview, _saveCurrency]);

        // ---------- password
        var passwordCard = Card(DockStyle.Top);
        passwordCard.Height = 300;
        passwordCard.Margin = new Padding(0, Metrics.Space4, 0, 0);

        var passwordHeading = Heading("Your password", "Changes the password for the account you are signed in as.");

        _currentPassword = Password("Current password", Metrics.Space5, 72);
        _newPassword = Password("New password", Metrics.Space5, 140);
        _confirmPassword = Password("Confirm new password", Metrics.Space5, 208);

        var hint = new Label
        {
            Text = "At least 8 characters, and different from the current one.",
            AutoSize = true,
            ForeColor = Palette.InkMuted,
            Font = Typography.Get(Typography.Size2Xs),
            Location = new Point(Metrics.Space5 + 340, 120),
            BackColor = Color.Transparent,
        };

        _passwordError = new Label
        {
            AutoSize = false,
            Size = new Size(420, 34),
            Location = new Point(Metrics.Space5 + 340, 146),
            ForeColor = Palette.DangerText,
            Font = Typography.Get(Typography.SizeSm),
            Visible = false,
            BackColor = Color.Transparent,
        };

        var savePassword = new AppButton { Variant = ButtonVariant.Primary, Text = "Change password", Width = 170 };
        savePassword.Location = new Point(Metrics.Space5 + 340, 208);
        savePassword.Click += async (_, _) => await ChangePasswordAsync().ConfigureAwait(true);

        passwordCard.Controls.AddRange(
        [
            passwordHeading,
            _currentPassword, _newPassword, _confirmPassword,
            hint, _passwordError, savePassword,
        ]);

        foreach (var (label, y) in new[] { ("Current password", 52), ("New password", 120), ("Confirm new password", 188) })
        {
            var caption = Theme.FieldLabel(label);
            caption.Location = new Point(Metrics.Space5, y);
            passwordCard.Controls.Add(caption);
        }

        var spacer = new Panel { Dock = DockStyle.Top, Height = Metrics.Space4, BackColor = Color.Transparent };

        Controls.Add(passwordCard);
        Controls.Add(spacer);
        Controls.Add(currencyCard);
        Controls.Add(_header);
    }

    private static Label Heading(string title, string subtitle)
    {
        var label = new Label
        {
            AutoSize = false,
            Size = new Size(640, 48),
            Location = new Point(Metrics.Space5, Metrics.Space4),
            BackColor = Color.Transparent,
        };

        label.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Render.UseQualityRendering(g);

            TextRenderer.DrawText(
                g,
                title,
                Typography.Get(Typography.SizeLg, FontStyle.Bold),
                new Rectangle(0, 0, label.Width, 24),
                Palette.InkStrong,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(
                g,
                subtitle,
                Typography.Get(Typography.SizeSm),
                new Rectangle(0, 24, label.Width, 20),
                Palette.InkMuted,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        };

        return label;
    }

    private static KryptonTextBox Password(string cue, int x, int y)
    {
        var box = Theme.TextBox(cue);
        box.PasswordChar = '●';
        box.Width = 300;
        box.Location = new Point(x, y);
        return box;
    }

    private void UpdatePreview()
    {
        if (_currency.SelectedItem is CurrencyItem item)
        {
            // Shows the choice applied to a real figure, so the decimals and the symbol
            // placement are visible before it is saved.
            var sample = 1234567.5m;
            _currencyPreview.Text = item.Option.SymbolFirst
                ? $"{item.Option.Symbol} {sample.ToString("N" + item.Option.Decimals, System.Globalization.CultureInfo.CurrentCulture)}"
                : $"{sample.ToString("N" + item.Option.Decimals, System.Globalization.CultureInfo.CurrentCulture)} {item.Option.Symbol}";
        }
    }

    protected override async Task LoadAsync()
    {
        var settings = await Service<SettingsService>().GetAsync().ConfigureAwait(true);

        foreach (var item in _currency.Items.OfType<CurrencyItem>())
        {
            if (item.Option.Code == settings.Currency.Code)
            {
                _currency.SelectedItem = item;
                break;
            }
        }

        UpdatePreview();

        // Only an admin may change application-wide settings; the service refuses it
        // either way, so the control is disabled rather than left to fail on click.
        _currency.Enabled = Session.IsAdmin;
        _saveCurrency.Visible = Session.IsAdmin;

        _header.Subtitle = Session.IsAdmin
            ? "Applies to every screen"
            : "Applies to every screen. Only an administrator can change the currency.";
    }

    private async Task SaveCurrencyAsync()
    {
        if (_currency.SelectedItem is not CurrencyItem item)
        {
            return;
        }

        await TryAsync(
            async () =>
            {
                var saved = await Service<SettingsService>()
                    .UpdateAsync(new SaveSettingsRequest(item.Option.Code))
                    .ConfigureAwait(true);

                // Every open screen formats money through this, so it has to change the
                // moment the setting does.
                Format.UseCurrency(saved.Currency.Code);
            },
            $"Currency set to {item.Option.Code}").ConfigureAwait(true);
    }

    private async Task ChangePasswordAsync()
    {
        _passwordError.Visible = false;

        if (_newPassword.Text != _confirmPassword.Text)
        {
            ShowPasswordError("The two new passwords do not match.");
            return;
        }

        try
        {
            await Service<AuthService>()
                .ChangePasswordAsync(new ChangePasswordRequest(_currentPassword.Text, _newPassword.Text))
                .ConfigureAwait(true);

            _currentPassword.Text = string.Empty;
            _newPassword.Text = string.Empty;
            _confirmPassword.Text = string.Empty;

            Toast.Show(FindForm(), "Password changed");
        }
        catch (Application.Common.Exceptions.ValidationFailedException validation)
        {
            ShowPasswordError(string.Join(" ", validation.Errors.SelectMany(e => e.Value)));
        }
        catch (Exception ex)
        {
            ShowPasswordError(ex.Message);
        }
    }

    private void ShowPasswordError(string message)
    {
        _passwordError.Text = message;
        _passwordError.Visible = true;
    }

    private sealed record CurrencyItem(CurrencyOptionDto Option)
    {
        public override string ToString() => $"{Option.Code}  —  {Option.Name}";
    }
}
