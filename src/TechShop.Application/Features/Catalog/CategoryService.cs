using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Catalog;

public sealed record CategoryDto
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int ProductCount { get; init; }
}

public sealed record SaveCategoryRequest(string Name, bool IsActive);

public sealed class CategoryService(
    IAppDbContext db,
    IAuditService audit,
    ICurrentUser currentUser,
    ICacheService cache,
    IExcelExportService excel,
    IDateTimeProvider clock)
{
    public static QuerySpec<CategoryDto> Spec { get; } = new QuerySpec<CategoryDto>()
        .Map("id", c => c.Id, "Id")
        .Text("name", c => c.Name, "Category")
        .Map("isActive", c => c.IsActive, "Active")
        .Map("productCount", c => c.ProductCount, "Products");

    public Task<PagedResult<CategoryDto>> QueryAsync(QueryRequest request, CancellationToken ct = default)
        => QueryRunner.RunAsync(Projected(), request, Spec, ct);

    public async Task<byte[]> ExportAsync(QueryRequest request, CancellationToken ct = default)
    {
        var rows = await QueryRunner.RunForExportAsync(Projected(), request, Spec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.Products, AuditAction.Export, nameof(Category), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Categories", rows, Spec, request.Columns);
    }

    public Task<List<CategoryDto>> LookupAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync(
            CacheKeys.Lookup("categories"),
            () => Projected().Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(ct));

    public async Task<CategoryDto> CreateAsync(SaveCategoryRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (await db.Categories.AnyAsync(c => c.Name == name && !c.IsDeleted, ct).ConfigureAwait(false))
        {
            throw ValidationFailedException.ForField(nameof(request.Name), "That category already exists.");
        }

        var entity = new Category
        {
            Name = name,
            IsActive = request.IsActive,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        db.Categories.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Lookups);

        await audit.LogAsync(Module.Products, AuditAction.Create, nameof(Category), entity.Id, name, ct: ct)
            .ConfigureAwait(false);
        return new CategoryDto
    {
        Id = entity.Id,
        Name = entity.Name,
        IsActive = entity.IsActive,
        ProductCount = 0
    };
    }

    public async Task<CategoryDto> UpdateAsync(long id, SaveCategoryRequest request, CancellationToken ct = default)
    {
        var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Category), id);

        var name = request.Name.Trim();
        if (await db.Categories.AnyAsync(c => c.Name == name && c.Id != id && !c.IsDeleted, ct).ConfigureAwait(false))
        {
            throw ValidationFailedException.ForField(nameof(request.Name), "That category already exists.");
        }

        var before = $"{entity.Name}/active={entity.IsActive}";
        entity.Name = name;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedBy = currentUser.Username;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Lookups);

        await audit.LogAsync(
            Module.Products,
            AuditAction.Update,
            nameof(Category),
            id,
            $"{before} -> {name}/active={entity.IsActive}",
            ct: ct).ConfigureAwait(false);

        return await Projected().FirstAsync(c => c.Id == id, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Category), id);

        if (await db.Products.AnyAsync(p => p.CategoryId == id && !p.IsDeleted, ct).ConfigureAwait(false))
        {
            throw new ConflictException("Products still use this category. Deactivate it instead.");
        }

        entity.IsDeleted = true;
        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedBy = currentUser.Username;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.RemoveGroup(CacheKeys.Lookups);

        await audit.LogAsync(Module.Products, AuditAction.Delete, nameof(Category), id, entity.Name, ct: ct)
            .ConfigureAwait(false);
    }

    private IQueryable<CategoryDto> Projected()
        => db.Categories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .Select(c => new CategoryDto
    {
        Id = c.Id,
        Name = c.Name,
        IsActive = c.IsActive,
        ProductCount = c.Products.Count(p => !p.IsDeleted)
    });
}
