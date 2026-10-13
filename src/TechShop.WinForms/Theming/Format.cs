using System.Globalization;
using TechShop.Application.Features.Settings;

namespace TechShop.WinForms.Theming;

/// <summary>
/// How numbers and dates are written across the app.
///
/// The currency is chosen once in Settings and applied everywhere, so it is cached here
/// after sign-in rather than fetched per cell: a grid of 50 rows with 4 money columns
/// would otherwise ask the database 200 times to render one page.
/// </summary>
public static class Format
{
    private static CurrencyOptionDto _currency = SettingsService.Available
        .First(c => c.Code == SettingsService.DefaultCurrency);

    public static string CurrencyCode => _currency.Code;

    public static string CurrencySymbol => _currency.Symbol;

    /// <summary>Called at sign-in, and again when Settings changes the currency.</summary>
    public static void UseCurrency(string code)
        => _currency = SettingsService.Available.FirstOrDefault(c => c.Code == code) ?? _currency;

    /// <summary>Money with the currency symbol, for totals and KPI figures.</summary>
    public static string Money(decimal value)
        => _currency.SymbolFirst
            ? $"{_currency.Symbol} {Amount(value)}"
            : $"{Amount(value)} {_currency.Symbol}";

    /// <summary>
    /// Money without the symbol, for grid columns. Repeating the symbol down a column
    /// adds width and no information; the header carries it instead.
    /// </summary>
    public static string Amount(decimal value)
        => value.ToString("N" + _currency.Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);

    /// <summary>A column header that names the unit once, for example "Total (Rs)".</summary>
    public static string MoneyHeader(string label) => $"{label} ({_currency.Symbol})";

    public static string Qty(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// Dates are stored in UTC and shown in the operator's local time, because "today's
    /// sales" has to mean the day the shop actually had.
    /// </summary>
    public static string Date(DateTime utc) => ToLocal(utc).ToString("dd MMM yyyy", CultureInfo.CurrentCulture);

    public static string DateTimeShort(DateTime utc)
        => ToLocal(utc).ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture);

    public static DateTime ToLocal(DateTime utc)
        => utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    /// <summary>Turns a local date chosen in a picker back into the UTC the services expect.</summary>
    public static DateTime ToUtc(DateTime local)
        => local.Kind == DateTimeKind.Utc ? local : DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();

    /// <summary>Splits a PascalCase enum or field name into words for a header.</summary>
    public static string Humanise(string name)
    {
        var chars = new List<char>(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(i == 0 ? char.ToUpperInvariant(name[i]) : name[i]);
        }

        return new string([.. chars]);
    }
}
