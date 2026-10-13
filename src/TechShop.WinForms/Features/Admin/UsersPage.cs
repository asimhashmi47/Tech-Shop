using TechShop.Application.Common.Query;
using TechShop.Application.Features.Shops;
using TechShop.Application.Features.Users;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Admin;

/// <summary>
/// The accounts, and what each one may reach.
///
/// A user is deactivated rather than deleted: their name is on invoices, audit rows and
/// stock movements, and those have to keep resolving.
/// </summary>
public sealed class UsersPage : ListPage<UserListDto>
{
    public UsersPage()
        : base(Module.Users, "Users", "Accounts, shops and permissions")
    {
    }

    protected override string DefaultSortField => "username";

    protected override bool DefaultSortDescending => false;

    protected override string ItemNoun => "user";

    protected override bool SupportsCreate => true;

    protected override bool SupportsEdit => true;

    protected override bool SupportsDelete => true;

    protected override Func<QueryRequest, Task<byte[]>>? Exporter
        => request => Service<UserService>().ExportAsync(request);

    protected override IReadOnlyList<GridColumn<UserListDto>> Columns() =>
    [
        new("username", "Username", u => u.Username) { Width = 140 },
        new("fullName", "Name", u => u.FullName) { Fill = true },

        new("role", "Role", u => u.Role)
        {
            Width = 110,
            Chip = u => u.Role == UserRole.Admin
                ? ("Admin", Palette.VioletSoft, Palette.VioletText)
                : ("Standard", Palette.SlateSoft, Palette.SlateText),
        },

        new("shops", "Shops", u => u.Shops) { Width = 220 },

        new("lastLoginAt", "Last signed in", u => u.LastLoginAt)
        {
            Width = 150,
            Format = u => u.LastLoginAt is { } at ? Format.DateTimeShort(at) : "Never",
        },

        new("isActive", "Status", u => u.IsActive)
        {
            Width = 100,
            Chip = u => u.IsActive
                ? ("Active", Palette.EmeraldSoft, Palette.EmeraldText)
                : ("Disabled", Palette.RoseSoft, Palette.RoseText),
        },
    ];

    protected override Task<PagedResult<UserListDto>> QueryAsync(QueryRequest request)
        => Service<UserService>().QueryAsync(request);

    protected override string Describe(UserListDto row) => $"{row.Username} ({row.FullName})";

    protected override async Task OnNewAsync()
    {
        var shops = await Service<ShopService>().LookupAsync().ConfigureAwait(true);

        using var editor = new UserEditor(Service<UserService>(), shops, null);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), "User added");
        }
    }

    protected override async Task OnEditAsync(UserListDto row)
    {
        var shops = await Service<ShopService>().LookupAsync().ConfigureAwait(true);
        var detail = await Service<UserService>().GetAsync(row.Id).ConfigureAwait(true);

        using var editor = new UserEditor(Service<UserService>(), shops, detail);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Toast.Show(FindForm(), $"{row.Username} saved");
        }
    }

    /// <summary>Deactivates rather than deletes, which is what the service does too.</summary>
    protected override Task OnDeleteAsync(UserListDto row)
        => Service<UserService>().DeactivateAsync(row.Id);
}
