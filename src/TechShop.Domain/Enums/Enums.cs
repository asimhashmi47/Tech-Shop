namespace TechShop.Domain.Enums;

public enum UserRole
{
    Admin = 1,
    Standard = 2
}

public enum Module
{
    Dashboard = 1,
    Shops = 2,
    Products = 3,
    Stock = 4,
    Pos = 5,
    Invoices = 6,
    Customers = 7,
    Suppliers = 8,
    Purchases = 9,
    Expenses = 10,
    PettyCash = 11,
    Reports = 12,
    Users = 13,
    AuditLog = 14,
    Settings = 15
}

public enum Access
{
    Read = 1,
    Write = 2
}

public enum PaymentMethod
{
    Cash = 1,
    Card = 2,
    Bank = 3,
    Credit = 4
}

public enum InvoiceStatus
{
    Unpaid = 1,
    Partial = 2,
    Paid = 3,
    Void = 4
}

public enum StockMovementType
{
    Opening = 1,
    Purchase = 2,
    Sale = 3,
    AdjustmentIn = 4,
    AdjustmentOut = 5,
    TransferIn = 6,
    TransferOut = 7,
    SaleVoid = 8
}

public enum PettyCashDirection
{
    In = 1,
    Out = 2
}

public enum AuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Login = 4,
    LoginFailed = 5,
    Logout = 6,
    Void = 7,
    Export = 8,
    PermissionChange = 9,
    PasswordChange = 10
}
