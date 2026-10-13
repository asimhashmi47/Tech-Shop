using TechShop.Domain.Common;
using TechShop.Domain.Enums;

namespace TechShop.Domain.Entities;

public class Category : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = [];
}

public class Product : AuditableEntity
{
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public long CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string? Brand { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SalePrice { get; set; }
    public int ReorderLevel { get; set; }

    /// <summary>
    /// Whether this line attracts sales tax. A real catalogue is mixed: hardware is
    /// taxed, a service or an exempt item is not, and the till has to know which is
    /// which per line rather than taxing a whole invoice.
    /// </summary>
    public bool IsTaxable { get; set; }

    /// <summary>Percentage used to prefill the tax on a sale line. Ignored when not taxable.</summary>
    public decimal TaxRate { get; set; }

    public bool IsActive { get; set; } = true;
}

public class StockLevel
{
    public long Id { get; set; }
    public long ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Qty { get; set; }
}

public class StockMovement : AuditableEntity
{
    public long ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public StockMovementType Type { get; set; }

    /// <summary>Signed quantity. Positive increases stock, negative decreases it.</summary>
    public int Qty { get; set; }
    public decimal UnitCost { get; set; }
    public DateTime MovedAt { get; set; }
    public string? Reference { get; set; }
    public string? Reason { get; set; }
}

public class Supplier : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Customer : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsWalkIn { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PurchaseBill : AuditableEntity
{
    public string BillNo { get; set; } = string.Empty;
    public long ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public long SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public DateTime BillDate { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? Note { get; set; }

    public ICollection<PurchaseItem> Items { get; set; } = [];
    public decimal Outstanding => Total - PaidAmount;
}

public class PurchaseItem
{
    public long Id { get; set; }
    public long PurchaseBillId { get; set; }
    public PurchaseBill PurchaseBill { get; set; } = null!;
    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Qty { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal => Qty * UnitCost;
}

public class SupplierPayment : AuditableEntity
{
    public long ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public long SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public long? PurchaseBillId { get; set; }
    public PurchaseBill? PurchaseBill { get; set; }
    public DateTime PaidAt { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string? Note { get; set; }
}
