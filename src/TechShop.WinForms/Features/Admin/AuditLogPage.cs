using TechShop.Application.Common.Query;
using TechShop.Application.Features.Audit;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Admin;

/// <summary>
/// Who did what, when, and from which till.
///
/// Read only by design. An audit log that can be edited is not an audit log, so this
/// screen offers no New, Edit or Delete at all — not even to an admin.
/// </summary>
public sealed class AuditLogPage : ListPage<AuditLogDto>
{
    public AuditLogPage()
        : base(Module.AuditLog, "Audit log", withDateRange: true)
    {
    }

    protected override string DefaultSortField => "at";

    protected override string ItemNoun => "entry";

    protected override Func<QueryRequest, Task<byte[]>>? Exporter
        => request => Service<AuditLogService>().ExportAsync(request);

    protected override IReadOnlyList<GridColumn<AuditLogDto>> Columns() =>
    [
        new("at", "When", a => a.At)
        {
            Width = 150,
            Format = a => Format.DateTimeShort(a.At),
        },

        new("username", "User", a => a.Username) { Width = 110 },
        new("shopName", "Shop", a => a.ShopName) { Width = 160 },
        new("module", "Module", a => a.Module) { Width = 110 },

        new("action", "Action", a => a.Action)
        {
            Width = 120,
            Chip = a => (a.Action.ToString(), Tone(a.Action).Fill, Tone(a.Action).Ink),
        },

        new("entity", "Entity", a => a.Entity) { Width = 120 },
        new("summary", "Detail", a => a.Summary) { Fill = true },
        new("machine", "Machine", a => a.Machine) { Width = 120 },
    ];

    /// <summary>
    /// A failed sign-in and a void are the two rows someone scanning this screen is
    /// usually looking for, so they are the ones that stand out.
    /// </summary>
    private static (System.Drawing.Color Fill, System.Drawing.Color Ink) Tone(AuditAction action) => action switch
    {
        AuditAction.LoginFailed => (Palette.RoseSoft, Palette.RoseText),
        AuditAction.Void or AuditAction.Delete => (Palette.AmberSoft, Palette.AmberText),
        AuditAction.PermissionChange or AuditAction.PasswordChange => (Palette.VioletSoft, Palette.VioletText),
        AuditAction.Login or AuditAction.Logout => (Palette.SlateSoft, Palette.SlateText),
        AuditAction.Create => (Palette.EmeraldSoft, Palette.EmeraldText),
        _ => (Palette.IndigoSoft, Palette.IndigoText),
    };

    protected override Task<PagedResult<AuditLogDto>> QueryAsync(QueryRequest request)
        => Service<AuditLogService>().QueryAsync(request);
}
