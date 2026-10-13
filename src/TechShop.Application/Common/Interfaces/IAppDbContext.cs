using Microsoft.EntityFrameworkCore;
using TechShop.Domain.Entities;

namespace TechShop.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Shop> Shops { get; }
    DbSet<AppUser> Users { get; }
    DbSet<UserShop> UserShops { get; }
    DbSet<UserPermission> UserPermissions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<AppSetting> AppSettings { get; }

    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<StockLevel> StockLevels { get; }
    DbSet<StockMovement> StockMovements { get; }

    DbSet<Supplier> Suppliers { get; }
    DbSet<PurchaseBill> PurchaseBills { get; }
    DbSet<PurchaseItem> PurchaseItems { get; }
    DbSet<SupplierPayment> SupplierPayments { get; }

    DbSet<Customer> Customers { get; }
    DbSet<SalesInvoice> SalesInvoices { get; }
    DbSet<SalesItem> SalesItems { get; }
    DbSet<CustomerPayment> CustomerPayments { get; }

    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<PettyCashEntry> PettyCashEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Used by POS and purchase flows so stock, invoice and ledger move together.</summary>
    Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default);
}

public interface IAppTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}
