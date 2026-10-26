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

    /// <summary>
    /// Decrements stock only if there is enough, in a single statement.
    ///
    /// Read-modify-write through the change tracker is what let a hundred tills each
    /// read the same last unit and each sell it. Checking and writing in one UPDATE
    /// makes the shelf the arbiter: whoever's statement lands second sees the already
    /// reduced quantity and is refused.
    /// </summary>
    public async Task<bool> TryTakeStockAsync(long shopId, long productId, int qty, CancellationToken ct = default)
    {
        if (qty <= 0)
        {
            return true;
        }

        await EnsureStockRowAsync(shopId, productId, ct).ConfigureAwait(false);

        var affected = await Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE StockLevels SET Qty = Qty - {qty}
               WHERE ShopId = {shopId} AND ProductId = {productId} AND Qty >= {qty}",
            ct).ConfigureAwait(false);

        if (affected > 0)
        {
            // The row was changed behind the change tracker's back, so any copy it is
            // holding is now stale and must not be written back over this.
            Detach(shopId, productId);
        }

        return affected > 0;
    }

    public async Task AddStockAsync(long shopId, long productId, int qty, CancellationToken ct = default)
    {
        if (qty == 0)
        {
            return;
        }

        await EnsureStockRowAsync(shopId, productId, ct).ConfigureAwait(false);

        await Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE StockLevels SET Qty = Qty + {qty}
               WHERE ShopId = {shopId} AND ProductId = {productId}",
            ct).ConfigureAwait(false);

        Detach(shopId, productId);
    }

    private Task<int> EnsureStockRowAsync(long shopId, long productId, CancellationToken ct)
        => Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO StockLevels (ShopId, ProductId, Qty)
               SELECT {shopId}, {productId}, 0
               WHERE NOT EXISTS (
                   SELECT 1 FROM StockLevels WHERE ShopId = {shopId} AND ProductId = {productId})",
            ct);

    private void Detach(long shopId, long productId)
    {
        var tracked = ChangeTracker.Entries<StockLevel>()
            .FirstOrDefault(e => e.Entity.ShopId == shopId && e.Entity.ProductId == productId);

        if (tracked is not null)
        {
            tracked.State = EntityState.Detached;
        }
    }

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
