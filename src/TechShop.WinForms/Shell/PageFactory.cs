using TechShop.Domain.Enums;
using TechShop.WinForms.Features.Admin;
using TechShop.WinForms.Features.Catalog;
using TechShop.WinForms.Features.Dashboard;
using TechShop.WinForms.Features.Money;
using TechShop.WinForms.Features.Parties;
using TechShop.WinForms.Features.Purchases;
using TechShop.WinForms.Features.Sales;
using TechShop.WinForms.Features.Stock;

namespace TechShop.WinForms.Shell;

/// <summary>
/// Maps a module to its screen. One switch rather than reflection, so an unregistered
/// module is a compile-time-visible gap instead of a runtime surprise.
/// </summary>
public static class PageFactory
{
    public static PageBase Create(Module module) => module switch
    {
        Module.Dashboard => new DashboardPage(),
        Module.Products => new ProductsPage(),
        Module.Customers => new CustomersPage(),
        Module.Suppliers => new SuppliersPage(),
        Module.Expenses => new ExpensesPage(),
        Module.PettyCash => new PettyCashPage(),
        Module.Shops => new ShopsPage(),
        Module.Invoices => new InvoicesPage(),
        Module.Purchases => new PurchasesPage(),
        Module.Stock => new StockPage(),
        Module.Pos => new Features.Pos.PosPage(),
        Module.Reports => new Features.Reports.ReportsPage(),
        Module.Users => new UsersPage(),
        Module.Settings => new SettingsPage(),
        Module.AuditLog => new AuditLogPage(),
        _ => new PlaceholderPage(module, Label(module)),
    };

    private static string Label(Module module)
        => NavRegistry.All.FirstOrDefault(e => e.Module == module)?.Label ?? module.ToString();
}
