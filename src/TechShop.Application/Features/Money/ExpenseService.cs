using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Money;

public sealed record ExpenseCategoryDto(long Id, string Name, bool IsActive);

public sealed record SaveExpenseCategoryRequest(string Name, bool IsActive);

public sealed record ExpenseDto
{
    public long Id { get; init; }
    public DateTime ExpenseDate { get; init; }
    public long ShopId { get; init; }
    public string ShopName { get; init; } = string.Empty;
    public long ExpenseCategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public PaymentMethod Method { get; init; }
    public string? Note { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

public sealed record SaveExpenseRequest(
    long ShopId,
    long ExpenseCategoryId,
    DateTime ExpenseDate,
    decimal Amount,
    PaymentMethod Method,
    string? Note);

public sealed class ExpenseService(
    IAppDbContext db,
    IAuditService audit,
    ICurrentUser currentUser,
    IShopScope shopScope,
    ICacheService cache,
    IExcelExportService excel,
    IDateTimeProvider clock)
{
    public static QuerySpec<ExpenseDto> Spec { get; } = new QuerySpec<ExpenseDto>()
        .Map("id", e => e.Id, "Id")
        .Map("expenseDate", e => e.ExpenseDate, "Date")
        .Text("shopName", e => e.ShopName, "Shop")
        .Text("categoryName", e => e.CategoryName, "Category")
        .Map("amount", e => e.Amount, "Amount")
        .Map("method", e => e.Method, "Method")
        .Text("note", e => e.Note, "Note")
        .Text("createdBy", e => e.CreatedBy, "By");

    public async Task<PagedResult<ExpenseDto>> QueryAsync(QueryRequest request, CancellationToken ct = default)
        => await QueryRunner
            .RunAsync(await ProjectedAsync(request.ShopId, ct).ConfigureAwait(false), request, Spec, ct)
            .ConfigureAwait(false);

    public async Task<byte[]> ExportAsync(QueryRequest request, CancellationToken ct = default)
    {
        var source = await ProjectedAsync(request.ShopId, ct).ConfigureAwait(false);
        var rows = await QueryRunner.RunForExportAsync(source, request, Spec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.Expenses, AuditAction.Export, nameof(Expense), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Expenses", rows, Spec, request.Columns);
    }

    public Task<List<ExpenseCategoryDto>> CategoriesAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync(
            CacheKeys.Lookup("expense-categories"),
            () => db.ExpenseCategories
                .AsNoTracking()
                .Where(c => c.IsActive && !c.IsDeleted)
                .OrderBy(c => c.Name)
                .Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.IsActive))
                .ToListAsync(ct));

    public async Task<ExpenseCategoryDto> CreateCategoryAsync(SaveExpenseCategoryRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw ValidationFailedException.ForField(nameof(request.Name), "Category name is required.");
        }

        if (await db.ExpenseCategories.AnyAsync(c => c.Name == name && !c.IsDeleted, ct).ConfigureAwait(false))
        {
            throw ValidationFailedException.ForField(nameof(request.Name), "That category already exists.");
        }

        var entity = new ExpenseCategory
        {
            Name = name,
            IsActive = request.IsActive,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        db.ExpenseCategories.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Lookups);

        await audit.LogAsync(Module.Expenses, AuditAction.Create, nameof(ExpenseCategory), entity.Id, name, ct: ct)
            .ConfigureAwait(false);
        return new ExpenseCategoryDto(entity.Id, entity.Name, entity.IsActive);
    }

    public async Task<ExpenseDto> GetAsync(long id, CancellationToken ct = default)
    {
        var source = await ProjectedAsync(null, ct).ConfigureAwait(false);
        return await source.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Expense), id);
    }

    public async Task<ExpenseDto> CreateAsync(SaveExpenseRequest request, CancellationToken ct = default)
    {
        await shopScope.EnsureWritableAsync(request.ShopId, ct).ConfigureAwait(false);
        Validate(request);

        await using var tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);

        var entity = new Expense
        {
            ShopId = request.ShopId,
            ExpenseCategoryId = request.ExpenseCategoryId,
            ExpenseDate = DateTime.SpecifyKind(request.ExpenseDate, DateTimeKind.Utc),
            Amount = request.Amount,
            Method = request.Method,
            Note = request.Note,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        db.Expenses.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // A cash expense leaves the drawer, so it also lands in petty cash.
        if (request.Method == PaymentMethod.Cash)
        {
            db.PettyCashEntries.Add(new PettyCashEntry
            {
                ShopId = request.ShopId,
                EntryDate = entity.ExpenseDate,
                Direction = PettyCashDirection.Out,
                Amount = entity.Amount,
                ExpenseId = entity.Id,
                Note = entity.Note ?? "Cash expense",
                IsSystemGenerated = true,
                CreatedAt = clock.UtcNow,
                CreatedBy = currentUser.Username
            });

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.Expenses,
            AuditAction.Create,
            nameof(Expense),
            entity.Id,
            $"{entity.Amount:0.00} via {entity.Method}",
            request.ShopId,
            ct: ct).ConfigureAwait(false);

        return await GetAsync(entity.Id, ct).ConfigureAwait(false);
    }

    public async Task<ExpenseDto> UpdateAsync(long id, SaveExpenseRequest request, CancellationToken ct = default)
    {
        Validate(request);

        var entity = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Expense), id);

        await shopScope.EnsureWritableAsync(entity.ShopId, ct).ConfigureAwait(false);
        await shopScope.EnsureWritableAsync(request.ShopId, ct).ConfigureAwait(false);

        var before = $"{entity.Amount:0.00}/{entity.Method}";

        await using var tx = await db.BeginTransactionAsync(ct).ConfigureAwait(false);

        entity.ShopId = request.ShopId;
        entity.ExpenseCategoryId = request.ExpenseCategoryId;
        entity.ExpenseDate = DateTime.SpecifyKind(request.ExpenseDate, DateTimeKind.Utc);
        entity.Amount = request.Amount;
        entity.Method = request.Method;
        entity.Note = request.Note;
        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedBy = currentUser.Username;

        // The linked drawer entry is rebuilt so cash never drifts from the expense.
        var linked = await db.PettyCashEntries
            .Where(p => p.ExpenseId == id && !p.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var row in linked)
        {
            row.IsDeleted = true;
            row.UpdatedAt = clock.UtcNow;
            row.UpdatedBy = currentUser.Username;
        }

        if (request.Method == PaymentMethod.Cash)
        {
            db.PettyCashEntries.Add(new PettyCashEntry
            {
                ShopId = request.ShopId,
                EntryDate = entity.ExpenseDate,
                Direction = PettyCashDirection.Out,
                Amount = entity.Amount,
                ExpenseId = entity.Id,
                Note = entity.Note ?? "Cash expense",
                IsSystemGenerated = true,
                CreatedAt = clock.UtcNow,
                CreatedBy = currentUser.Username
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.Expenses,
            AuditAction.Update,
            nameof(Expense),
            id,
            $"{before} -> {entity.Amount:0.00}/{entity.Method}",
            entity.ShopId,
            ct: ct).ConfigureAwait(false);

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Expense), id);

        await shopScope.EnsureWritableAsync(entity.ShopId, ct).ConfigureAwait(false);

        entity.IsDeleted = true;
        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedBy = currentUser.Username;

        var linked = await db.PettyCashEntries
            .Where(p => p.ExpenseId == id && !p.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var row in linked)
        {
            row.IsDeleted = true;
            row.UpdatedAt = clock.UtcNow;
            row.UpdatedBy = currentUser.Username;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Dashboard);

        await audit.LogAsync(
            Module.Expenses, AuditAction.Delete, nameof(Expense), id, $"{entity.Amount:0.00}", entity.ShopId, ct: ct)
            .ConfigureAwait(false);
    }

    private static void Validate(SaveExpenseRequest request)
    {
        if (request.Amount <= 0)
        {
            throw ValidationFailedException.ForField(nameof(request.Amount), "Enter an amount above zero.");
        }
    }

    private async Task<IQueryable<ExpenseDto>> ProjectedAsync(long? shopId, CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        return db.Expenses
            .AsNoTracking()
            .Where(e => !e.IsDeleted && shopIds.Contains(e.ShopId))
            .Select(e => new ExpenseDto
    {
        Id = e.Id,
        ExpenseDate = e.ExpenseDate,
        ShopId = e.ShopId,
        ShopName = e.Shop.Name,
        ExpenseCategoryId = e.ExpenseCategoryId,
        CategoryName = e.ExpenseCategory.Name,
        Amount = e.Amount,
        Method = e.Method,
        Note = e.Note,
        CreatedBy = e.CreatedBy
    });
    }
}
