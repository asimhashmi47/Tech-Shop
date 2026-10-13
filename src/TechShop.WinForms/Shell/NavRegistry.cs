using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Shell;

/// <summary>
/// Everything the rail can show, in the order it shows it. Grouping follows the shape of
/// the working day rather than the data model: what you sell, what you hold, what you owe.
/// </summary>
public static class NavRegistry
{
    public static IReadOnlyList<NavEntry> All { get; } =
    [
        new(Module.Dashboard, "Dashboard", Icons.Dashboard, "Overview"),

        new(Module.Pos, "Point of sale", Icons.Pos, "Sales"),
        new(Module.Invoices, "Invoices", Icons.Invoice, "Sales"),
        new(Module.Customers, "Customers", Icons.Customer, "Sales"),

        new(Module.Products, "Products", Icons.Product, "Inventory"),
        new(Module.Stock, "Stock", Icons.Stock, "Inventory"),
        new(Module.Suppliers, "Suppliers", Icons.Supplier, "Inventory"),
        new(Module.Purchases, "Purchases", Icons.Purchase, "Inventory"),

        new(Module.Expenses, "Expenses", Icons.Expense, "Accounting"),
        new(Module.PettyCash, "Petty cash", Icons.PettyCash, "Accounting"),
        new(Module.Reports, "Reports", Icons.Reports, "Accounting"),

        new(Module.Shops, "Shops", Icons.Shops, "Admin"),
        new(Module.Users, "Users", Icons.Users, "Admin"),
        new(Module.AuditLog, "Audit log", Icons.AuditLog, "Admin"),
        new(Module.Settings, "Settings", Icons.Settings, "Admin"),
    ];
}
