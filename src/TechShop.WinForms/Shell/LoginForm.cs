using System.Drawing;
using Krypton.Toolkit;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Features.Auth;
using TechShop.Infrastructure.Services;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Shell;

/// <summary>
/// Sign-in. A two-panel layout: the brand wash carries the product's identity so the form
/// beside it can stay plain, which is the right trade for a screen an operator sees every
/// morning and wants to get past quickly.
/// </summary>
public sealed class LoginForm : Form
{
    private readonly KryptonTextBox _username;
    private readonly KryptonTextBox _password;
    private readonly AppButton _submit;
    private readonly Label _error;
    private readonly Panel _card;
    private bool _busy;

    public LoginForm()
    {
        Text = "TechShop";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(840, 520);
        BackColor = Palette.Surface;
        Font = Typography.Body;

        var brand = BuildBrandPanel();

        _card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            Padding = new Padding(Metrics.Space12, Metrics.Space16, Metrics.Space12, Metrics.Space12),
        };

        var heading = new Label
        {
            Text = "Sign in",
            AutoSize = true,
            Font = Typography.H2,
            ForeColor = Palette.InkStrong,
            Location = new Point(Metrics.Space12, 92),
        };

        var subheading = new Label
        {
            Text = "Use the account your shop administrator set up.",
            AutoSize = true,
            Font = Typography.Get(Typography.SizeBase),
            ForeColor = Palette.InkMuted,
            Location = new Point(Metrics.Space12, 132),
        };

        var usernameLabel = Theme.FieldLabel("Username");
        usernameLabel.Location = new Point(Metrics.Space12, 182);

        _username = Theme.TextBox("admin");
        _username.Location = new Point(Metrics.Space12, 202);
        _username.Width = 320;

        var passwordLabel = Theme.FieldLabel("Password");
        passwordLabel.Location = new Point(Metrics.Space12, 254);

        _password = Theme.TextBox();
        _password.PasswordChar = '●';
        _password.Location = new Point(Metrics.Space12, 274);
        _password.Width = 320;

        _error = new Label
        {
            AutoSize = false,
            Size = new Size(320, 36),
            Location = new Point(Metrics.Space12, 318),
            ForeColor = Palette.DangerText,
            Font = Typography.Get(Typography.SizeSm),
            Visible = false,
        };

        _submit = new AppButton
        {
            Variant = ButtonVariant.Primary,
            Text = "Sign in",
            Width = 320,
            Height = 42,
            Location = new Point(Metrics.Space12, 362),
        };
        _submit.Click += async (_, _) => await SubmitAsync().ConfigureAwait(true);

        var hint = new Label
        {
            AutoSize = false,
            Size = new Size(360, 40),
            Location = new Point(Metrics.Space12, 416),
            ForeColor = Palette.InkMuted,
            Font = Typography.Get(Typography.SizeXs),
            Text = $"Demo data: sign in as {DemoCredentials.Username} / {DemoCredentials.Password}. Standard accounts (sara, bilal, hina, omar, zoya) use their username as the password.",
        };

        _card.Controls.AddRange([heading, subheading, usernameLabel, _username, passwordLabel, _password, _error, _submit, hint]);

        // Docking resolves from the highest index down, so the footer is added last to
        // claim the full width underneath both the brand wash and the form.
        Controls.Add(_card);
        Controls.Add(brand);
        Controls.Add(new AppFooter());

        AcceptButton = _submit;
        Shown += (_, _) => _username.Focus();
    }

    private static Panel BuildBrandPanel()
    {
        var panel = new Panel { Dock = DockStyle.Left, Width = 360 };

        panel.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Render.UseQualityRendering(g);

            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                panel.ClientRectangle,
                Palette.Indigo,
                Render.GradientEnd(Palette.Violet),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical);
            g.FillRectangle(brush, panel.ClientRectangle);

            var mark = new Rectangle(Metrics.Space12, Metrics.Space12, 52, 52);
            Render.FillRounded(g, mark, Metrics.RadiusLg, Color.FromArgb(56, 255, 255, 255));
            Icons.Draw(g, Icons.Pos, mark, Color.White, 20f);

            TextRenderer.DrawText(
                g,
                "TechShop",
                Typography.Get(Typography.Size3Xl, FontStyle.Bold),
                new Rectangle(Metrics.Space12, 150, panel.Width - Metrics.Space16, 48),
                Color.White,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(
                g,
                "Point of sale, stock and the books,\nin one place that agrees with itself.",
                Typography.Get(Typography.SizeMd),
                new Rectangle(Metrics.Space12, 204, panel.Width - (Metrics.Space12 * 2), 60),
                Color.FromArgb(220, 255, 255, 255),
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            var lines = new[]
            {
                "Till, invoices and customer accounts",
                "Stock across every branch",
                "Receivables, payables and reports",
            };

            var y = 310;
            foreach (var line in lines)
            {
                var tick = new Rectangle(Metrics.Space12, y, 18, 18);
                Icons.Draw(g, Icons.Success, tick, Color.FromArgb(230, 255, 255, 255), 9f);

                TextRenderer.DrawText(
                    g,
                    line,
                    Typography.Get(Typography.SizeSm),
                    new Rectangle(tick.Right + Metrics.Space2, y, panel.Width - 90, 20),
                    Color.FromArgb(215, 255, 255, 255),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                y += 30;
            }
        };

        return panel;
    }

    private async Task SubmitAsync()
    {
        if (_busy)
        {
            return;
        }

        var username = _username.Text.Trim();
        var password = _password.Text;

        if (username.Length == 0 || password.Length == 0)
        {
            ShowError("Enter a username and password.");
            return;
        }

        SetBusy(true);

        try
        {
            using var scope = AppHost.CreateScope();
            var auth = (AuthService)scope.ServiceProvider.GetService(typeof(AuthService))!;
            var profile = await auth.LoginAsync(new LoginRequest(username, password)).ConfigureAwait(true);

            var session = AppHost.Get<UserSession>();
            session.SignIn(
                profile.Id,
                profile.Username,
                profile.FullName,
                profile.Role,
                profile.Shops.Select(s => s.Id).ToList());

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ForbiddenException ex)
        {
            ShowError(ex.Message);
            _password.Text = string.Empty;
            _password.Focus();
        }
        catch (Exception ex)
        {
            ShowError($"Could not sign in: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _submit.Enabled = !busy;
        _submit.Text = busy ? "Signing in…" : "Sign in";
        _username.Enabled = !busy;
        _password.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = true;
    }
}

/// <summary>
/// The demo credentials, surfaced on the form so a fresh install is not a dead end.
/// Read from the seeder rather than retyped, so the two cannot drift apart.
/// </summary>
internal static class DemoCredentials
{
    public const string Username = Infrastructure.Persistence.DatabaseSeeder.AdminUsername;
    public const string Password = Infrastructure.Persistence.DatabaseSeeder.AdminPassword;
}
