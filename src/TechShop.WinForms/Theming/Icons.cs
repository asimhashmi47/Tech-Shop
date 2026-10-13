using System.Drawing;
using System.Drawing.Text;

namespace TechShop.WinForms.Theming;

/// <summary>
/// Icons come from the system icon font — Segoe Fluent Icons on Windows 11, falling back
/// to Segoe MDL2 Assets on Windows 10. Both ship with the OS, so the app carries no image
/// assets, scales to any DPI without a second bitmap, and recolours by setting a brush.
///
/// The code points below are the stable ones shared by both fonts.
/// </summary>
public static class Icons
{
    public const string Dashboard = "";   // Home
    public const string Pos = "";         // Shop / cart
    public const string Invoice = "";     // Document
    public const string Customer = "";    // Contact
    public const string Receipt = "";     // Payment
    public const string Product = "";     // Package
    public const string Stock = "";       // Layers
    public const string Supplier = "";    // Truck / delivery
    public const string Purchase = "";    // Cart
    public const string Expense = "";     // Money
    public const string PettyCash = "";   // Wallet
    public const string Receivable = "";  // Chart ascending
    public const string Payable = "";     // Chart descending
    public const string Reports = "";     // Chart
    public const string Shops = "";       // Location
    public const string Users = "";       // People
    public const string Settings = "";    // Gear
    public const string AuditLog = "";    // History

    public const string Search = "";
    public const string Refresh = "";
    public const string Add = "";
    public const string Edit = "";
    public const string Delete = "";
    public const string Print = "";
    public const string Close = "";
    public const string ChevronLeft = "";
    public const string ChevronRight = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string Menu = "";
    public const string Calendar = "";
    public const string Warning = "";
    public const string Success = "";
    public const string Error = "";
    public const string Info = "";
    public const string SignOut = "";
    public const string Filter = "";
    public const string Profit = "";
    public const string Cash = "";
    public const string LowStock = "";

    private static readonly string Family = Resolve();
    private static readonly Dictionary<float, Font> Cache = [];
    private static readonly Lock Gate = new();

    public static Font Font(float size)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(size, out var cached))
            {
                return cached;
            }

            var font = new Font(Family, size, FontStyle.Regular, GraphicsUnit.Point);
            Cache[size] = font;
            return font;
        }
    }

    public static void Draw(Graphics g, string glyph, Rectangle bounds, Color colour, float size)
        => TextRenderer.DrawText(
            g,
            glyph,
            Font(size),
            bounds,
            colour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

    private static string Resolve()
    {
        using var installed = new InstalledFontCollection();
        var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            if (names.Contains(candidate))
            {
                return candidate;
            }
        }

        // Nothing to fall back to that carries these code points; the glyph boxes that
        // result are ugly but harmless, and every icon in the app has a text label beside it.
        return "Segoe UI Symbol";
    }
}
