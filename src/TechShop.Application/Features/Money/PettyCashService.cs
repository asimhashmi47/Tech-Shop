using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Money;

public sealed record PettyCashEntryDto
{
    public long Id { get; init; }
    public DateTime EntryDate { get; init; }
    public long ShopId { get; init; }
    public string ShopName { get; init; } = string.Empty;
    public PettyCashDirection Direction { get; init; }
    public decimal CashIn { get; init; }
    public decimal CashOut { get; init; }
    public string? Note { get; init; }
    public bool FromExpense { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

public sealed record SavePettyCashRequest(
    long ShopId,
    DateTime EntryDate,
    PettyCashDirection Direction,
    decimal Amount,
    string? Note);

public sealed record PettyCashDayDto(
    DateTime Date,
    long ShopId,
    string ShopName,
    decimal Opening,
    decimal CashIn,
    decimal CashOut,
    decimal Closing);

public sealed class PettyCashService(
    IAppDbContext db,
    IAuditService audit,
    ICurrentUser currentUser,
    IShopScope shopScope,
    ICacheService cache,
    IExcelExportService excel,
    IDateTimeProvider clock)
{
    public static QuerySpec<PettyCashEntryDto> Spec { get; } = new QuerySpec<PettyCashEntryDto>()
        .Map("id", p => p.Id, "Id")
        .Map("entryDate", p => p.EntryDate, "Date")
        .Text("shopName", p => p.ShopName, "Shop")
        .Map("direction", p => p.Direction, "Direction")
        .Map("cashIn", p => p.CashIn, "Cash in")
        .Map("cashOut", p => p.CashOut, "Cash out")
        .Text("note", p => p.Note, "Note")
        .Map("fromExpense", p => p.FromExpense, "From expense")
        .Text("createdBy", p => p.CreatedBy, "By");

    public async Task<PagedResult<PettyCashEntryDto>> QueryAsync(QueryRequest request, CancellationToken ct = default)
        => await QueryRunner
            .RunAsync(await ProjectedAsync(request.ShopId, ct).ConfigureAwait(false), request, Spec, ct)
            .ConfigureAwait(false);

    public async Task<byte[]> ExportAsync(QueryRequest request, CancellationToken ct = default)
    {
        var source = await ProjectedAsync(request.ShopId, ct).ConfigureAwait(false);
        var rows = await QueryRunner.RunForExportAsync(source, request, Spec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.PettyCash, AuditAction.Export, nameof(PettyCashEntry), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Petty cash", rows, Spec, request.Columns);
    }

    /// <summary>Opening and closing drawer balance per shop for one day.</summary>
    public async Task<List<PettyCashDayDto>> DayAsync(DateTime date, long? shopId, CancellationToken ct = default)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);
        var dayStart = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        var shops = await db.Shops
            .AsNoTracking()
            .Where(s => shopIds.Contains(s.Id) && !s.IsDeleted)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = new List<PettyCashDayDto>(shops.Count);

        foreach (var shop in shops)
        {
            var opening = await db.PettyCashEntries
                .Where(p => p.ShopId == shop.Id && !p.IsDeleted && p.EntryDate < dayStart)
                .SumAsync(p => p.Direction == PettyCashDirection.In ? p.Amount : -p.Amount, ct)
                .ConfigureAwait(false);

            var cashIn = await db.PettyCashEntries
                .Where(p => p.ShopId == shop.Id && !p.IsDeleted
                    && p.EntryDate >= dayStart && p.EntryDate < dayEnd
                    && p.Direction == PettyCashDirection.In)
                .SumAsync(p => p.Amount, ct)
                .ConfigureAwait(false);

            var cashOut = await db.PettyCashEntries
                .Where(p => p.ShopId == shop.Id && !p.IsDeleted
                    && p.EntryDate >= dayStart && p.EntryDate < dayEnd
                    && p.Direction == PettyCashDirection.Out)
                .SumAsync(p => p.Amount, ct)
                .ConfigureAwait(false);

            result.Add(new PettyCashDayDto(
                dayStart, shop.Id, shop.Name, opening, cashIn, cashOut, opening + cashIn - cashOut));
        }

        return result;
    }

    public async Task<PettyCashEntryDto> CreateAsync(SavePettyCashRequest request, CancellationToken ct = default)
    {
        await shopScope.EnsureWritableAsync(request.ShopId, ct).ConfigureAwait(false);

        if (request.Amount <= 0)
        {
            throw ValidationFailedException.ForField(nameof(request.Amount), "Enter an amount above zero.");
        }

        var entryDate = DateTime.SpecifyKind(request.EntryDate, DateTimeKind.Utc);

        if (request.Direction == PettyCashDirection.Out)
        {
            var balance = await BalanceAsync(request.ShopId, ct).ConfigureAwait(false);
            if (request.Amount > balance)
            {
                throw ValidationFailedException.ForField(
                    nameof(request.Amount),
                    $"The drawer holds {balance:0.00}, so that cash out is too large.");
            }
        }

        var entity = new PettyCashEntry
        {
            ShopId = request.ShopId,
            EntryDate = entryDate,
            Direction = request.Direction,
            Amount = request.Amount,
            Note = request.Note,
            IsSystemGenerated = false,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        db.PettyCashEntries.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.PettyCash,
            AuditAction.Create,
            nameof(PettyCashEntry),
            entity.Id,
            $"{request.Direction} {request.Amount:0.00}",
            request.ShopId,
            ct: ct).ConfigureAwait(false);

        var source = await ProjectedAsync(null, ct).ConfigureAwait(false);
        return await source.FirstAsync(p => p.Id == entity.Id, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await db.PettyCashEntries.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct)
            .ConfigureAwait(false) ?? throw new NotFoundException(nameof(PettyCashEntry), id);

        await shopScope.EnsureWritableAsync(entity.ShopId, ct).ConfigureAwait(false);

        if (entity.ExpenseId is not null)
        {
            throw new ConflictException("This entry belongs to an expense. Remove the expense instead.");
        }

        entity.IsDeleted = true;
        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedBy = currentUser.Username;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.PettyCash, AuditAction.Delete, nameof(PettyCashEntry), id, $"{entity.Amount:0.00}", entity.ShopId, ct: ct)
            .ConfigureAwait(false);
    }

    public Task<decimal> BalanceAsync(long shopId, CancellationToken ct = default)
        => db.PettyCashEntries
            .Where(p => p.ShopId == shopId && !p.IsDeleted)
            .SumAsync(p => p.Direction == PettyCashDirection.In ? p.Amount : -p.Amount, ct);

    private async Task<IQueryable<PettyCashEntryDto>> ProjectedAsync(long? shopId, CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        return db.PettyCashEntries
            .AsNoTracking()
            .Where(p => !p.IsDeleted && shopIds.Contains(p.ShopId))
            .Select(p => new PettyCashEntryDto
    {
        Id = p.Id,
        EntryDate = p.EntryDate,
        ShopId = p.ShopId,
        ShopName = p.Shop.Name,
        Direction = p.Direction,
        CashIn = p.Direction == PettyCashDirection.In ? p.Amount : 0m,
        CashOut = p.Direction == PettyCashDirection.Out ? p.Amount : 0m,
        Note = p.Note,
        FromExpense = p.ExpenseId != null,
        CreatedBy = p.CreatedBy
    });
    }
}
