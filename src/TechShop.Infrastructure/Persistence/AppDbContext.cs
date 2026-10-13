using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TechShop.Application.Common.Interfaces;
using TechShop.Domain.Entities;

namespace TechShop.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserShop> UserShops => Set<UserShop>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseBill> PurchaseBills => Set<PurchaseBill>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesItem> SalesItems => Set<SalesItem>();
    public DbSet<CustomerPayment> CustomerPayments => Set<CustomerPayment>();

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<PettyCashEntry> PettyCashEntries => Set<PettyCashEntry>();

    public new Task<int> SaveChangesAsync(CancellationToken ct = default)
        => base.SaveChangesAsync(ct);

    public async Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        // A nested call reuses the ambient transaction so services can compose freely.
        if (Database.CurrentTransaction is not null)
        {
            return new NestedTransaction();
        }

        return new EfTransaction(await Database.BeginTransactionAsync(ct).ConfigureAwait(false));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    private sealed class EfTransaction(IDbContextTransaction inner) : IAppTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class NestedTransaction : IAppTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
