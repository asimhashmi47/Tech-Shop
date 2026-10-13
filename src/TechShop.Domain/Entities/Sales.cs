using TechShop.Domain.Common;
using TechShop.Domain.Enums;

namespace TechShop.Domain.Entities;

public class SalesInvoice : AuditableEntity
{
    public string InvoiceNo { get; set; } = string.Empty;
    public long ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public long CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime InvoiceDate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }

    /// <summary>
    /// Sales tax, summed from the lines. Held on the invoice as well as on each line so
    /// the figure the customer was charged is stored, not recomputed from rates that may
    /// have changed since.
    /// </summary>
    public decimal TaxTotal { get; set; }

    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public DateTime? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
    public string? Note { get; set; }

    public ICollection<SalesItem> Items { get; set; } = [];
    public decimal Outstanding => Status == InvoiceStatus.Void ? 0m : Total - PaidAmount;
}

public class SalesItem
{
    public long Id { get; set; }
    public long SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;
    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Qty { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>Product cost captured at the time of sale so COGS stays stable.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>
    /// Tax charged on this line. Stored as money rather than a rate because the till
    /// lets the operator override it: a rate would stop describing what was actually
    /// charged the moment anyone adjusted it. Zero for a line that is not taxable.
    /// </summary>
    public decimal TaxAmount { get; set; }

    public decimal LineTotal => Qty * UnitPrice;

    /// <summary>What this line adds to the invoice, tax included.</summary>
    public decimal LineTotalWithTax => (Qty * UnitPrice) + TaxAmount;
}

public class CustomerPayment : AuditableEntity
{
    public long ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public long CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public long? SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }
    public DateTime ReceivedAt { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string? Note { get; set; }
}

public class ExpenseCategory : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Expense : AuditableEntity
{
    public long ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public long ExpenseCategoryId { get; set; }
    public ExpenseCategory ExpenseCategory { get; set; } = null!;
    public DateTime ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string? Note { get; set; }
}

public class PettyCashEntry : AuditableEntity
{
    public long ShopId { get; set; }
    public Shop Shop { get; set; } = null!;
    public DateTime EntryDate { get; set; }
    public PettyCashDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public long? ExpenseId { get; set; }
    public Expense? Expense { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// True when the row mirrors a cash movement already stored elsewhere (a cash sale, a
    /// cash receipt, a supplier payment or a cash expense). Reports read cash from the
    /// primary tables, so these rows are excluded there and only shown in the drawer view.
    /// </summary>
    public bool IsSystemGenerated { get; set; }
}
