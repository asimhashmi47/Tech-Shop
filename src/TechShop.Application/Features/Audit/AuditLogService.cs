using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Audit;

public sealed record AuditLogDto
{
    public long Id { get; init; }
    public DateTime At { get; init; }
    public string Username { get; init; } = string.Empty;
    public string? ShopName { get; init; }
    public Module Module { get; init; }
    public AuditAction Action { get; init; }
    public string Entity { get; init; } = string.Empty;
    public long? EntityId { get; init; }
    public string? Summary { get; init; }
    /// <summary>The workstation the action came from, in place of the web app's IP.</summary>
    public string? Machine { get; init; }
    public string? CorrelationId { get; init; }
}

/// <summary>Admin only read model over the audit trail. It uses the same grid as every list.</summary>
public sealed class AuditLogService(IAppDbContext db, IExcelExportService excel, IAuditService audit)
{
    public static QuerySpec<AuditLogDto> Spec { get; } = new QuerySpec<AuditLogDto>()
        .Map("id", a => a.Id, "Id")
        .Map("at", a => a.At, "When")
        .Text("username", a => a.Username, "User")
        .Text("shopName", a => a.ShopName, "Shop")
        .Map("module", a => a.Module, "Module")
        .Map("action", a => a.Action, "Action")
        .Text("entity", a => a.Entity, "Entity")
        .Map("entityId", a => a.EntityId, "Entity id")
        .Text("summary", a => a.Summary, "Summary")
        .Text("machine", a => a.Machine, "Machine")
        .Text("correlationId", a => a.CorrelationId, "Correlation id");

    public Task<PagedResult<AuditLogDto>> QueryAsync(QueryRequest request, CancellationToken ct = default)
        => QueryRunner.RunAsync(Projected(), request, Spec, ct);

    public async Task<byte[]> ExportAsync(QueryRequest request, CancellationToken ct = default)
    {
        var rows = await QueryRunner.RunForExportAsync(Projected(), request, Spec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.AuditLog, AuditAction.Export, "AuditLog", summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Audit log", rows, Spec, request.Columns);
    }

    private IQueryable<AuditLogDto> Projected()
        => from log in db.AuditLogs.AsNoTracking()
           join shop in db.Shops.AsNoTracking() on log.ShopId equals shop.Id into shops
           from shop in shops.DefaultIfEmpty()
           select new AuditLogDto
    {
        Id = log.Id,
        At = log.At,
        Username = log.Username,
        ShopName = shop != null ? shop.Name : null,
        Module = log.Module,
        Action = log.Action,
        Entity = log.Entity,
        EntityId = log.EntityId,
        Summary = log.Summary,
        Machine = log.Machine,
        CorrelationId = log.CorrelationId
    };
}
