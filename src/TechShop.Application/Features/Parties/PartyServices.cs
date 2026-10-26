using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Common.Query;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Parties;

public sealed record SupplierDto
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public bool IsActive { get; init; }
    public decimal Payable { get; init; }
}

public sealed record SaveSupplierRequest(string Name, string? Phone, string? Address, bool IsActive);

public sealed record CustomerDto
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public bool IsWalkIn { get; init; }
    public bool IsActive { get; init; }
    public decimal Receivable { get; init; }
}

public sealed record SaveCustomerRequest(string Name, string? Phone, string? Address, bool IsActive);

public sealed class SupplierService(
    IAppDbContext db,
    IAuditService audit,
    ICurrentUser currentUser,
    IShopScope shopScope,
    IExcelExportService excel,
    IAccessGuard access,
    IDateTimeProvider clock)
{
    public static QuerySpec<SupplierDto> Spec { get; } = new QuerySpec<SupplierDto>()
        .Map("id", s => s.Id, "Id")
        .Text("name", s => s.Name, "Supplier")
        .Text("phone", s => s.Phone, "Phone")
        .Text("address", s => s.Address, "Address")
        .Map("payable", s => s.Payable, "Payable")
        .Map("isActive", s => s.IsActive, "Active");

    public async Task<PagedResult<SupplierDto>> QueryAsync(QueryRequest request, CancellationToken ct = default)
        => await QueryRunner
            .RunAsync(await ProjectedAsync(request.ShopId, ct).ConfigureAwait(false), request, Spec, ct)
            .ConfigureAwait(false);

    public async Task<byte[]> ExportAsync(QueryRequest request, CancellationToken ct = default)
    {
        var source = await ProjectedAsync(request.ShopId, ct).ConfigureAwait(false);
        var rows = await QueryRunner.RunForExportAsync(source, request, Spec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.Suppliers, AuditAction.Export, nameof(Supplier), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Suppliers", rows, Spec, request.Columns);
    }

    public Task<List<SupplierDto>> LookupAsync(CancellationToken ct = default)
        => db.Suppliers
            .AsNoTracking()
            .Where(s => s.IsActive && !s.IsDeleted)
            .OrderBy(s => s.Name)
            .Select(s => new SupplierDto
    {
        Id = s.Id,
        Name = s.Name,
        Phone = s.Phone,
        Address = s.Address,
        IsActive = s.IsActive,
        Payable = 0m
    })
            .ToListAsync(ct);

    public async Task<SupplierDto> GetAsync(long id, CancellationToken ct = default)
    {
        var source = await ProjectedAsync(null, ct).ConfigureAwait(false);
        return await source.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Supplier), id);
    }

    public async Task<SupplierDto> CreateAsync(SaveSupplierRequest request, CancellationToken ct = default)
    {
        await access.EnsureAsync(Module.Suppliers, Access.Write, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw ValidationFailedException.ForField(nameof(request.Name), "Supplier name is required.");
        }

        var entity = new Supplier
        {
            Name = request.Name.Trim(),
            Phone = request.Phone?.Trim(),
            Address = request.Address?.Trim(),
            IsActive = request.IsActive,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        db.Suppliers.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.LogAsync(Module.Suppliers, AuditAction.Create, nameof(Supplier), entity.Id, entity.Name, ct: ct)
            .ConfigureAwait(false);
        return await GetAsync(entity.Id, ct).ConfigureAwait(false);
    }

    public async Task<SupplierDto> UpdateAsync(long id, SaveSupplierRequest request, CancellationToken ct = default)
    {
        await access.EnsureAsync(Module.Suppliers, Access.Write, ct).ConfigureAwait(false);

        var entity = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Supplier), id);

        var before = entity.Name;
        entity.Name = request.Name.Trim();
        entity.Phone = request.Phone?.Trim();
        entity.Address = request.Address?.Trim();
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedBy = currentUser.Username;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync(
            Module.Suppliers, AuditAction.Update, nameof(Supplier), id, $"{before} -> {entity.Name}", ct: ct)
            .ConfigureAwait(false);

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Supplier), id);

        if (await db.PurchaseBills.AnyAsync(b => b.SupplierId == id && !b.IsDeleted, ct).ConfigureAwait(false))
        {
            entity.IsActive = false;
        }
        else
        {
            entity.IsDeleted = true;
        }

        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedBy = currentUser.Username;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.LogAsync(Module.Suppliers, AuditAction.Delete, nameof(Supplier), id, entity.Name, ct: ct)
            .ConfigureAwait(false);
    }

    private async Task<IQueryable<SupplierDto>> ProjectedAsync(long? shopId, CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        return db.Suppliers
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .Select(s => new SupplierDto
    {
        Id = s.Id,
        Name = s.Name,
        Phone = s.Phone,
        Address = s.Address,
        IsActive = s.IsActive,
        Payable = db.PurchaseBills
                    .Where(b => b.SupplierId == s.Id && !b.IsDeleted && shopIds.Contains(b.ShopId))
                    .Sum(b => b.Total)
                - db.SupplierPayments
                    .Where(p => p.SupplierId == s.Id && !p.IsDeleted && shopIds.Contains(p.ShopId))
                    .Sum(p => p.Amount)
    });
    }
}

public sealed class CustomerService(
    IAppDbContext db,
    IAuditService audit,
    ICurrentUser currentUser,
    IShopScope shopScope,
    IExcelExportService excel,
    IAccessGuard access,
    IDateTimeProvider clock)
{
    public static QuerySpec<CustomerDto> Spec { get; } = new QuerySpec<CustomerDto>()
        .Map("id", c => c.Id, "Id")
        .Text("name", c => c.Name, "Customer")
        .Text("phone", c => c.Phone, "Phone")
        .Text("address", c => c.Address, "Address")
        .Map("receivable", c => c.Receivable, "Receivable")
        .Map("isWalkIn", c => c.IsWalkIn, "Walk-in")
        .Map("isActive", c => c.IsActive, "Active");

    public async Task<PagedResult<CustomerDto>> QueryAsync(QueryRequest request, CancellationToken ct = default)
        => await QueryRunner
            .RunAsync(await ProjectedAsync(request.ShopId, ct).ConfigureAwait(false), request, Spec, ct)
            .ConfigureAwait(false);

    public async Task<byte[]> ExportAsync(QueryRequest request, CancellationToken ct = default)
    {
        var source = await ProjectedAsync(request.ShopId, ct).ConfigureAwait(false);
        var rows = await QueryRunner.RunForExportAsync(source, request, Spec, ct).ConfigureAwait(false);
        await audit.LogAsync(Module.Customers, AuditAction.Export, nameof(Customer), summary: $"{rows.Count} rows", ct: ct)
            .ConfigureAwait(false);
        return excel.Build("Customers", rows, Spec, request.Columns);
    }

    public Task<List<CustomerDto>> LookupAsync(CancellationToken ct = default)
        => db.Customers
            .AsNoTracking()
            .Where(c => c.IsActive && !c.IsDeleted)
            .OrderByDescending(c => c.IsWalkIn)
            .ThenBy(c => c.Name)
            .Select(c => new CustomerDto
    {
        Id = c.Id,
        Name = c.Name,
        Phone = c.Phone,
        Address = c.Address,
        IsWalkIn = c.IsWalkIn,
        IsActive = c.IsActive,
        Receivable = 0m
    })
            .ToListAsync(ct);

    public async Task<CustomerDto> GetAsync(long id, CancellationToken ct = default)
    {
        var source = await ProjectedAsync(null, ct).ConfigureAwait(false);
        return await source.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Customer), id);
    }

    public async Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default)
    {
        await access.EnsureAsync(Module.Customers, Access.Write, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw ValidationFailedException.ForField(nameof(request.Name), "Customer name is required.");
        }

        var entity = new Customer
        {
            Name = request.Name.Trim(),
            Phone = request.Phone?.Trim(),
            Address = request.Address?.Trim(),
            IsActive = request.IsActive,
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.Username
        };

        db.Customers.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.LogAsync(Module.Customers, AuditAction.Create, nameof(Customer), entity.Id, entity.Name, ct: ct)
            .ConfigureAwait(false);
        return await GetAsync(entity.Id, ct).ConfigureAwait(false);
    }

    public async Task<CustomerDto> UpdateAsync(long id, SaveCustomerRequest request, CancellationToken ct = default)
    {
        await access.EnsureAsync(Module.Customers, Access.Write, ct).ConfigureAwait(false);

        var entity = await db.Customers.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Customer), id);

        var before = entity.Name;
        entity.Name = request.Name.Trim();
        entity.Phone = request.Phone?.Trim();
        entity.Address = request.Address?.Trim();
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedBy = currentUser.Username;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync(
            Module.Customers, AuditAction.Update, nameof(Customer), id, $"{before} -> {entity.Name}", ct: ct)
            .ConfigureAwait(false);

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await db.Customers.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Customer), id);

        if (entity.IsWalkIn)
        {
            throw new ConflictException("The walk-in customer is required by the POS and cannot be removed.");
        }

        if (await db.SalesInvoices.AnyAsync(i => i.CustomerId == id && !i.IsDeleted, ct).ConfigureAwait(false))
        {
            entity.IsActive = false;
        }
        else
        {
            entity.IsDeleted = true;
        }

        entity.UpdatedAt = clock.UtcNow;
        entity.UpdatedBy = currentUser.Username;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.LogAsync(Module.Customers, AuditAction.Delete, nameof(Customer), id, entity.Name, ct: ct)
            .ConfigureAwait(false);
    }

    private async Task<IQueryable<CustomerDto>> ProjectedAsync(long? shopId, CancellationToken ct)
    {
        var shopIds = await shopScope.ResolveAsync(shopId, ct).ConfigureAwait(false);

        return db.Customers
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .Select(c => new CustomerDto
    {
        Id = c.Id,
        Name = c.Name,
        Phone = c.Phone,
        Address = c.Address,
        IsWalkIn = c.IsWalkIn,
        IsActive = c.IsActive,
        Receivable = db.SalesInvoices
                    .Where(i => i.CustomerId == c.Id && !i.IsDeleted
                        && i.Status != InvoiceStatus.Void && shopIds.Contains(i.ShopId))
                    .Sum(i => i.Total)
                - db.CustomerPayments
                    .Where(p => p.CustomerId == c.Id && !p.IsDeleted && shopIds.Contains(p.ShopId))
                    .Sum(p => p.Amount)
    });
    }
}
