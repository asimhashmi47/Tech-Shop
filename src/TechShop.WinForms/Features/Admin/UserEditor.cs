using System.Drawing;
using Krypton.Toolkit;
using TechShop.Application.Features.Shops;
using TechShop.Application.Features.Users;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Admin;

/// <summary>
/// Adds or changes an account, including which shops it can reach and what it may do in
/// each module.
///
/// The permission grid is the point of this dialog. It is a matrix rather than a list of
/// roles because real shops do not divide neatly into roles — one person does the till
/// and the customers, another does stock and purchasing — and an admin needs to see the
/// whole picture at once before granting anything.
/// </summary>
public sealed class UserEditor : Form
{
    /// <summary>
    /// Users and the audit log are admin-only whatever the rows say, so they are not
    /// offered here. Showing a switch the service ignores is worse than not showing it.
    /// </summary>
    private static readonly Module[] Grantable = Enum.GetValues<Module>()
        .Where(m => m is not (Module.Users or Module.AuditLog))
        .ToArray();

    private readonly UserService _users;
    private readonly long? _id;
    private readonly bool _isSelfOrOnlyAdmin;

    private readonly KryptonTextBox _username;
    private readonly KryptonTextBox _fullName;
    private readonly KryptonComboBox _role;
    private readonly KryptonTextBox _password;
    private readonly KryptonCheckBox _active;
    private readonly CheckedListBox _shops;
    private readonly DataGridView _permissions;
    private readonly Label _error;
    private readonly AppButton _save;
    private bool _busy;

    public UserEditor(UserService users, IReadOnlyList<ShopDto> shops, UserDetailDto? existing)
    {
        _users = users;
        _id = existing?.Id;
        _isSelfOrOnlyAdmin = existing?.Role == UserRole.Admin;

        Text = existing is null ? "New user" : $"Edit {existing.Username}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 680);
        MinimumSize = new Size(820, 600);
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Palette.AppBackground;
        Font = Typography.Body;
        ShowIcon = false;
        MinimizeBox = false;

        // ---------- identity
        var identity = Card();
        identity.Dock = DockStyle.Top;
        identity.Height = 188;

        _username = Theme.TextBox("sara");
        _fullName = Theme.TextBox();
        _role = Pickers.ForEnum<UserRole>(existing?.Role ?? UserRole.Standard);
        _password = Theme.TextBox(existing is null ? "Required" : "Leave blank to keep the current one");
        _password.PasswordChar = '●';
        _active = Theme.CheckBox("Active and able to sign in");

        Place(identity, "Username", _username, Metrics.Space5, 240, 0);
        Place(identity, "Full name", _fullName, Metrics.Space5 + 256, 300, 0);
        Place(identity, "Role", _role, Metrics.Space5 + 572, 200, 0);
        Place(identity, "Password", _password, Metrics.Space5, 240, 68);
        Place(identity, "Status", _active, Metrics.Space5 + 256, 300, 68);

        // ---------- shops
        var shopsCard = Card();
        shopsCard.Dock = DockStyle.Left;
        shopsCard.Width = 280;
        shopsCard.Margin = new Padding(0, Metrics.Space3, Metrics.Space3, 0);

        _shops = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Palette.Surface,
            ForeColor = Palette.InkBody,
            Font = Typography.Get(Typography.SizeBase),
            CheckOnClick = true,
            IntegralHeight = false,
        };

        foreach (var shop in shops)
        {
            var index = _shops.Items.Add(new ShopItem(shop.Id, shop.Name));
            if (existing?.ShopIds.Contains(shop.Id) == true)
            {
                _shops.SetItemChecked(index, true);
            }
        }

        var shopsHeading = SectionLabel("Shops", "Which branches this account can see.");
        shopsCard.Controls.Add(_shops);
        shopsCard.Controls.Add(shopsHeading);

        // ---------- permissions
        var permissionsCard = Card();
        permissionsCard.Dock = DockStyle.Fill;
        permissionsCard.Margin = new Padding(0, Metrics.Space3, 0, 0);

        _permissions = BuildPermissionGrid();
        var permissionsHeading = SectionLabel("Permissions", "Read lets them see a screen. Write lets them change it.");
        permissionsCard.Controls.Add(_permissions);
        permissionsCard.Controls.Add(permissionsHeading);

        LoadPermissions(existing);

        // ---------- footer
        _error = new Label
        {
            AutoSize = false,
            Height = 20,
            ForeColor = Palette.DangerText,
            Font = Typography.Get(Typography.SizeSm),
            Visible = false,
            BackColor = Color.Transparent,
        };

        _save = new AppButton { Variant = ButtonVariant.Primary, Text = "Save", Width = 120, Height = 40 };
        _save.Click += async (_, _) => await SubmitAsync().ConfigureAwait(true);

        var cancel = new AppButton { Variant = ButtonVariant.Secondary, Text = "Cancel", Width = 100, Height = 40 };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Palette.Surface };
        footer.Controls.Add(_error);
        footer.Controls.Add(_save);
        footer.Controls.Add(cancel);
        footer.Resize += (_, _) =>
        {
            var y = (footer.Height - _save.Height) / 2;
            _save.Location = new Point(footer.Width - _save.Width - Metrics.Space5, y);
            cancel.Location = new Point(_save.Left - cancel.Width - Metrics.Space2, y);
            _error.Location = new Point(Metrics.Space5, y + 10);
            _error.Width = cancel.Left - Metrics.Space6;
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Palette.BorderSubtle);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.AppBackground,
            Padding = new Padding(Metrics.Space5),
        };
        body.Controls.Add(permissionsCard);
        body.Controls.Add(shopsCard);
        body.Controls.Add(identity);

        Controls.Add(body);
        Controls.Add(footer);

        if (existing is not null)
        {
            _username.Text = existing.Username;
            _fullName.Text = existing.FullName;
            _active.Checked = existing.IsActive;

            // The username appears on every audit row this account has written, so it
            // stays fixed once created.
            _username.Enabled = false;
        }
        else
        {
            _active.Checked = true;
        }

        _role.SelectedIndexChanged += (_, _) => ApplyRole();
        ApplyRole();

        CancelButton = cancel;
    }

    /// <summary>
    /// An admin has everything implicitly, so the matrix and the shop list are switched
    /// off rather than left showing values that will not be stored.
    /// </summary>
    private void ApplyRole()
    {
        var isAdmin = Pickers.Selected(_role, UserRole.Standard) == UserRole.Admin;

        _permissions.Enabled = !isAdmin;
        _shops.Enabled = !isAdmin;

        _permissions.DefaultCellStyle.BackColor = isAdmin ? Palette.SurfaceSunken : Palette.Surface;
        _permissions.DefaultCellStyle.ForeColor = isAdmin ? Palette.InkMuted : Palette.InkBody;
    }

    private static DataGridView BuildPermissionGrid()
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
            RowHeadersVisible = false,
            EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            ColumnHeadersHeight = Metrics.GridHeaderHeight,
            Font = Typography.GridBody,
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = Palette.SurfaceSunken;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.InkMuted;
        grid.ColumnHeadersDefaultCellStyle.Font = Typography.Get(Typography.Size2Xs, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(Metrics.Space3, 0, Metrics.Space3, 0);
        // A header is not selectable, so it must not pick up the system selection
        // colour when the grid takes focus — that showed as one header cell going blue.
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Palette.SurfaceSunken;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Palette.InkMuted;
        grid.DefaultCellStyle.BackColor = Palette.Surface;
        grid.DefaultCellStyle.ForeColor = Palette.InkBody;
        grid.DefaultCellStyle.SelectionBackColor = Palette.PrimarySoft;
        grid.DefaultCellStyle.SelectionForeColor = Palette.InkStrong;
        grid.DefaultCellStyle.Padding = new Padding(Metrics.Space3, 0, Metrics.Space3, 0);
        grid.RowTemplate.Height = Metrics.GridRowHeight;

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "module",
            HeaderText = "Module",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });

        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "read",
            HeaderText = "Read",
            Width = 80,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });

        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "write",
            HeaderText = "Write",
            Width = 80,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });

        // Ticking Write implies Read, and clearing Read clears Write. Letting someone
        // grant write without read produces an account that can change a screen it
        // cannot open.
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            var row = grid.Rows[e.RowIndex];

            if (e.ColumnIndex == 2 && row.Cells[2].Value is true)
            {
                row.Cells[1].Value = true;
            }
            else if (e.ColumnIndex == 1 && row.Cells[1].Value is not true)
            {
                row.Cells[2].Value = false;
            }
        };

        // Without this a tick is not committed until the cell loses focus, so the last
        // box an admin clicks before pressing Save would be lost.
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        return grid;
    }

    private void LoadPermissions(UserDetailDto? existing)
    {
        foreach (var module in Grantable)
        {
            var current = existing?.Permissions.FirstOrDefault(p => p.Module == module);
            _permissions.Rows.Add(Format.Humanise(module.ToString()), current?.CanRead ?? false, current?.CanWrite ?? false);
            _permissions.Rows[^1].Tag = module;
        }
    }

    private async Task SubmitAsync()
    {
        if (_busy)
        {
            return;
        }

        _error.Visible = false;

        var role = Pickers.Selected(_role, UserRole.Standard);

        var shopIds = _shops.CheckedItems
            .OfType<ShopItem>()
            .Select(s => s.Id)
            .ToList();

        var permissions = _permissions.Rows
            .Cast<DataGridViewRow>()
            .Where(r => r.Tag is Module)
            .Select(r => new PermissionInput(
                (Module)r.Tag!,
                r.Cells[1].Value is true,
                r.Cells[2].Value is true))
            .Where(p => p.CanRead || p.CanWrite)
            .ToList();

        if (role == UserRole.Standard && shopIds.Count == 0)
        {
            ShowError("A standard account needs at least one shop, or it can see nothing.");
            return;
        }

        if (role == UserRole.Standard && permissions.Count == 0)
        {
            ShowError("Grant at least one permission, or this account can sign in and do nothing.");
            return;
        }

        SetBusy(true);

        try
        {
            var request = new SaveUserRequest(
                _username.Text,
                _fullName.Text,
                role,
                _active.Checked,
                string.IsNullOrWhiteSpace(_password.Text) ? null : _password.Text,
                shopIds,
                permissions);

            if (_id is { } id)
            {
                await _users.UpdateAsync(id, request).ConfigureAwait(true);
            }
            else
            {
                await _users.CreateAsync(request).ConfigureAwait(true);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Application.Common.Exceptions.ValidationFailedException validation)
        {
            ShowError(string.Join("  ", validation.Errors.SelectMany(e => e.Value)));
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _save.Enabled = !busy;
        _save.Text = busy ? "Saving…" : "Save";
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = true;
    }

    // ---------------------------------------------------------------- test seams

    internal string? VisibleError => string.IsNullOrEmpty(_error.Text) ? null : _error.Text;

    internal Task SubmitForTestAsync() => SubmitAsync();

    internal void FillForTest(string username, string fullName, UserRole role, string? password)
    {
        _username.Text = username;
        _fullName.Text = fullName;
        _password.Text = password ?? string.Empty;

        foreach (var item in _role.Items)
        {
            if (item.GetType().GetProperty("Value")?.GetValue(item) is UserRole value && value == role)
            {
                _role.SelectedItem = item;
                break;
            }
        }

        ApplyRole();
    }

    internal void GrantForTest(Module module, bool read, bool write)
    {
        foreach (DataGridViewRow row in _permissions.Rows)
        {
            if (row.Tag is Module m && m == module)
            {
                row.Cells[1].Value = read || write;
                row.Cells[2].Value = write;
                return;
            }
        }
    }

    internal void AssignShopForTest(long shopId)
    {
        for (var i = 0; i < _shops.Items.Count; i++)
        {
            if (_shops.Items[i] is ShopItem item && item.Id == shopId)
            {
                _shops.SetItemChecked(i, true);
                return;
            }
        }
    }

    // ---------------------------------------------------------------- plumbing

    private static Panel Card()
    {
        var card = new Panel { BackColor = Palette.Surface, Padding = new Padding(Metrics.Space4, 52, Metrics.Space4, Metrics.Space4) };
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

    private static Label SectionLabel(string title, string subtitle)
    {
        var label = new Label { Dock = DockStyle.Top, Height = 44, BackColor = Color.Transparent };

        label.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Render.UseQualityRendering(g);

            TextRenderer.DrawText(
                g,
                title,
                Typography.Get(Typography.SizeMd, FontStyle.Bold),
                new Rectangle(0, 0, label.Width, 22),
                Palette.InkStrong,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(
                g,
                subtitle,
                Typography.Get(Typography.Size2Xs),
                new Rectangle(0, 22, label.Width, 18),
                Palette.InkMuted,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        };

        return label;
    }

    private static void Place(Control parent, string label, Control input, int x, int width, int yOffset)
    {
        var caption = Theme.FieldLabel(label);
        caption.Location = new Point(x, 52 + yOffset);
        input.Location = new Point(x, 72 + yOffset);
        input.Width = width;
        parent.Controls.Add(caption);
        parent.Controls.Add(input);
    }

    private sealed record ShopItem(long Id, string Name)
    {
        public override string ToString() => Name;
    }
}
