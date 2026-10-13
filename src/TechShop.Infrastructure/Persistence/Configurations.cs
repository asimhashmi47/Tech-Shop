using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechShop.Domain.Entities;

namespace TechShop.Infrastructure.Persistence;

/// <summary>
/// SQLite has no decimal type. Money is mapped straight onto a REAL column as a store type
/// rather than through a value converter: a converter would stop EF Core translating
/// arithmetic and comparisons, and every grid here sorts and filters on computed money
/// columns such as Outstanding and Stock value. PostgreSQL is the upgrade path when exact
/// decimal arithmetic starts to matter.
/// </summary>
internal static class ConfigurationExtensions
{
    public static void Money<T>(this EntityTypeBuilder<T> builder, string property)
        where T : class
        => builder.Property(property).HasColumnType("REAL");
}

public sealed class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    public void Configure(EntityTypeBuilder<Shop> builder)
    {
        builder.Property(s => s.Code).HasMaxLength(16).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Address).HasMaxLength(250);
        builder.Property(s => s.Phone).HasMaxLength(40);
        builder.HasIndex(s => s.Code).IsUnique();
        builder.HasIndex(s => s.IsDeleted);
    }
}

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(u => u.Username).HasMaxLength(60).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(120).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();

        builder.HasMany(u => u.UserShops).WithOne(us => us.User).HasForeignKey(us => us.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(u => u.Permissions).WithOne(p => p.User).HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserShopConfiguration : IEntityTypeConfiguration<UserShop>
{
    public void Configure(EntityTypeBuilder<UserShop> builder)
    {
        builder.HasIndex(us => new { us.UserId, us.ShopId }).IsUnique();
        builder.HasOne(us => us.Shop).WithMany(s => s.UserShops).HasForeignKey(us => us.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
        => builder.HasIndex(p => new { p.UserId, p.Module }).IsUnique();
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.Username).HasMaxLength(60).IsRequired();
        builder.Property(a => a.Entity).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(1000);
        builder.Property(a => a.Machine).HasMaxLength(60);
        builder.Property(a => a.CorrelationId).HasMaxLength(60);
        builder.HasIndex(a => a.At);
        builder.HasIndex(a => new { a.Module, a.Action });
        builder.HasIndex(a => a.UserId);
    }
}

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.Property(s => s.Key).HasMaxLength(60).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(200).IsRequired();
        builder.HasIndex(s => s.Key).IsUnique();
    }
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
        builder.HasIndex(c => c.Name);
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Sku).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Barcode).HasMaxLength(60);
        builder.Property(p => p.Name).HasMaxLength(160).IsRequired();
        builder.Property(p => p.Brand).HasMaxLength(80);
        builder.Money(nameof(Product.CostPrice));
        builder.Money(nameof(Product.SalePrice));
        builder.Money(nameof(Product.TaxRate));
        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.Barcode);
        builder.HasIndex(p => p.Name);
        builder.HasIndex(p => p.CategoryId);

        builder.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.HasIndex(s => new { s.ShopId, s.ProductId }).IsUnique();
        builder.HasOne(s => s.Shop).WithMany().HasForeignKey(s => s.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Product).WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.Property(m => m.Reference).HasMaxLength(80);
        builder.Property(m => m.Reason).HasMaxLength(250);
        builder.Money(nameof(StockMovement.UnitCost));
        builder.HasIndex(m => m.MovedAt);
        builder.HasIndex(m => new { m.ShopId, m.ProductId });
        builder.HasOne(m => m.Shop).WithMany().HasForeignKey(m => m.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(140).IsRequired();
        builder.Property(s => s.Phone).HasMaxLength(40);
        builder.Property(s => s.Address).HasMaxLength(250);
        builder.HasIndex(s => s.Name);
    }
}

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(140).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(40);
        builder.Property(c => c.Address).HasMaxLength(250);
        builder.HasIndex(c => c.Name);
        builder.HasIndex(c => c.IsWalkIn);
    }
}

public sealed class PurchaseBillConfiguration : IEntityTypeConfiguration<PurchaseBill>
{
    public void Configure(EntityTypeBuilder<PurchaseBill> builder)
    {
        builder.Property(b => b.BillNo).HasMaxLength(40).IsRequired();
        builder.Property(b => b.Note).HasMaxLength(500);
        builder.Money(nameof(PurchaseBill.Total));
        builder.Money(nameof(PurchaseBill.PaidAmount));
        builder.Ignore(b => b.Outstanding);
        builder.HasIndex(b => new { b.ShopId, b.BillNo }).IsUnique();
        builder.HasIndex(b => b.BillDate);
        builder.HasIndex(b => b.SupplierId);

        builder.HasOne(b => b.Shop).WithMany().HasForeignKey(b => b.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Supplier).WithMany().HasForeignKey(b => b.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(b => b.Items).WithOne(i => i.PurchaseBill).HasForeignKey(i => i.PurchaseBillId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PurchaseItemConfiguration : IEntityTypeConfiguration<PurchaseItem>
{
    public void Configure(EntityTypeBuilder<PurchaseItem> builder)
    {
        builder.Money(nameof(PurchaseItem.UnitCost));
        builder.Ignore(i => i.LineTotal);
        builder.HasIndex(i => i.ProductId);
        builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.Property(p => p.Note).HasMaxLength(500);
        builder.Money(nameof(SupplierPayment.Amount));
        builder.HasIndex(p => p.PaidAt);
        builder.HasIndex(p => new { p.SupplierId, p.ShopId });

        builder.HasOne(p => p.Shop).WithMany().HasForeignKey(p => p.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Supplier).WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.PurchaseBill).WithMany().HasForeignKey(p => p.PurchaseBillId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> builder)
    {
        builder.Property(i => i.InvoiceNo).HasMaxLength(40).IsRequired();
        builder.Property(i => i.Note).HasMaxLength(500);
        builder.Property(i => i.VoidReason).HasMaxLength(500);
        builder.Property(i => i.VoidedBy).HasMaxLength(60);
        builder.Money(nameof(SalesInvoice.SubTotal));
        builder.Money(nameof(SalesInvoice.Discount));
        builder.Money(nameof(SalesInvoice.TaxTotal));
        builder.Money(nameof(SalesInvoice.Total));
        builder.Money(nameof(SalesInvoice.PaidAmount));
        builder.Ignore(i => i.Outstanding);
        builder.HasIndex(i => new { i.ShopId, i.InvoiceNo }).IsUnique();
        builder.HasIndex(i => i.InvoiceDate);
        builder.HasIndex(i => i.CustomerId);
        builder.HasIndex(i => i.Status);

        builder.HasOne(i => i.Shop).WithMany().HasForeignKey(i => i.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Customer).WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(i => i.Items).WithOne(x => x.SalesInvoice).HasForeignKey(x => x.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SalesItemConfiguration : IEntityTypeConfiguration<SalesItem>
{
    public void Configure(EntityTypeBuilder<SalesItem> builder)
    {
        builder.Money(nameof(SalesItem.UnitPrice));
        builder.Money(nameof(SalesItem.UnitCost));
        builder.Money(nameof(SalesItem.TaxAmount));
        builder.Ignore(x => x.LineTotal);
        builder.Ignore(x => x.LineTotalWithTax);
        builder.HasIndex(x => x.ProductId);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerPaymentConfiguration : IEntityTypeConfiguration<CustomerPayment>
{
    public void Configure(EntityTypeBuilder<CustomerPayment> builder)
    {
        builder.Property(p => p.Note).HasMaxLength(500);
        builder.Money(nameof(CustomerPayment.Amount));
        builder.HasIndex(p => p.ReceivedAt);
        builder.HasIndex(p => new { p.CustomerId, p.ShopId });

        builder.HasOne(p => p.Shop).WithMany().HasForeignKey(p => p.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Customer).WithMany().HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.SalesInvoice).WithMany().HasForeignKey(p => p.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
        builder.HasIndex(c => c.Name);
    }
}

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Money(nameof(Expense.Amount));
        builder.HasIndex(e => e.ExpenseDate);
        builder.HasIndex(e => new { e.ShopId, e.ExpenseCategoryId });

        builder.HasOne(e => e.Shop).WithMany().HasForeignKey(e => e.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ExpenseCategory).WithMany().HasForeignKey(e => e.ExpenseCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PettyCashEntryConfiguration : IEntityTypeConfiguration<PettyCashEntry>
{
    public void Configure(EntityTypeBuilder<PettyCashEntry> builder)
    {
        builder.Property(p => p.Note).HasMaxLength(500);
        builder.Money(nameof(PettyCashEntry.Amount));
        builder.HasIndex(p => p.EntryDate);
        builder.HasIndex(p => p.ShopId);

        builder.HasOne(p => p.Shop).WithMany().HasForeignKey(p => p.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Expense).WithMany().HasForeignKey(p => p.ExpenseId).OnDelete(DeleteBehavior.Restrict);
    }
}

