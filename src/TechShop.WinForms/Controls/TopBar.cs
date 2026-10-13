using System.Drawing;
using Krypton.Toolkit;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

public sealed record ShopChoice(long? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The bar above the content: which shop the screens are looking at, and who is looking.
/// The shop picker is here rather than on each screen because it applies to all of them,
/// and having it in one place is what lets a user compare two branches without re-filtering.
/// </summary>
public sealed class TopBar : Panel
{
    private readonly KryptonComboBox _shopPicker;
    private readonly Panel _userChip;
    private readonly AppButton _signOut;
    private string _userName = string.Empty;
    private string _userRole = string.Empty;
    private bool _suppressShopEvent;

    public TopBar()
    {
        var shopLabel = new Label
        {
            Text = "Shop",
            AutoSize = true,
            ForeColor = Palette.InkMuted,
            Font = Typography.Get(Typography.Size2Xs, FontStyle.Bold),
            Location = new Point(Metrics.Space5, 10),
            BackColor = Color.Transparent,
        };

        _shopPicker = Theme.ComboBox();
        _shopPicker.Location = new Point(Metrics.Space5, 26);
        _shopPicker.Width = 200;
        _shopPicker.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppressShopEvent && _shopPicker.SelectedItem is ShopChoice choice)
            {
                ShopChanged?.Invoke(this, choice.Id);
            }
        };

        _signOut = new AppButton
        {
            Variant = ButtonVariant.Ghost,
            Text = "Sign out",
            Height = Metrics.ControlHeightSmall,
        };
        _signOut.FitToText(Metrics.Space3);
        _signOut.Click += (_, _) => SignOutRequested?.Invoke(this, EventArgs.Empty);

        _userChip = new Panel { Height = 40, Width = 190, BackColor = Color.Transparent };
        _userChip.Paint += PaintUserChip;

        Controls.Add(shopLabel);
        Controls.Add(_shopPicker);
        Controls.Add(_userChip);
        Controls.Add(_signOut);

        // Set last: assigning Height raises a resize, and OnResize positions the fields
        // above, so they have to exist by then.
        BackColor = Palette.Surface;
        Height = Metrics.TopBarHeight;
        Dock = DockStyle.Top;
    }

    public event EventHandler<long?>? ShopChanged;

    public event EventHandler? SignOutRequested;

    public void SetUser(string fullName, string role)
    {
        _userName = fullName;
        _userRole = role;
        _userChip.Invalidate();
    }

    /// <summary>
    /// Loads the picker. "All shops" is only offered when the user actually has more than
    /// one, so a single-branch operator is never asked a question with one answer.
    /// </summary>
    public void SetShops(IReadOnlyList<ShopChoice> shops, long? selected)
    {
        _suppressShopEvent = true;
        _shopPicker.Items.Clear();

        foreach (var shop in shops)
        {
            _shopPicker.Items.Add(shop);
        }

        var match = shops.FirstOrDefault(s => s.Id == selected) ?? (shops.Count > 0 ? shops[0] : null);
        if (match is not null)
        {
            _shopPicker.SelectedItem = match;
        }

        _shopPicker.Enabled = shops.Count > 1;
        _suppressShopEvent = false;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        _signOut.Location = new Point(Width - _signOut.Width - Metrics.Space5, (Height - _signOut.Height) / 2);
        _userChip.Location = new Point(_signOut.Left - _userChip.Width - Metrics.Space2, (Height - _userChip.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Palette.BorderSubtle);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }

    private void PaintUserChip(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        Render.UseQualityRendering(g);

        var bounds = new Rectangle(0, 0, _userChip.Width - 1, _userChip.Height - 1);
        Render.FillRounded(g, bounds, Metrics.RadiusMd, Palette.SurfaceSunken);
        Render.DrawRoundedBorder(g, bounds, Metrics.RadiusMd, Palette.BorderSubtle);

        var avatar = new Rectangle(Metrics.Space2, (bounds.Height - 26) / 2, 26, 26);
        Render.FillRounded(g, avatar, Metrics.RadiusSm, Palette.Primary);

        TextRenderer.DrawText(
            g,
            Initials(_userName),
            Typography.Get(Typography.Size2Xs, FontStyle.Bold),
            avatar,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var nameRect = Rectangle.FromLTRB(avatar.Right + Metrics.Space2, 5, bounds.Right - Metrics.Space2, 22);
        TextRenderer.DrawText(
            g,
            _userName,
            Typography.Get(Typography.SizeSm, FontStyle.Bold),
            nameRect,
            Palette.Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var roleRect = Rectangle.FromLTRB(avatar.Right + Metrics.Space2, 21, bounds.Right - Metrics.Space2, 36);
        TextRenderer.DrawText(
            g,
            _userRole,
            Typography.Get(Typography.Size2Xs),
            roleRect,
            Palette.InkMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => string.Concat(parts[0][..1], parts[^1][..1]).ToUpperInvariant(),
        };
    }
}
