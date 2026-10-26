using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Common.Interfaces;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Settings;

public sealed record CurrencyOptionDto(
    string Code,
    string Symbol,
    string Name,
    int Decimals,
    bool SymbolFirst);

public sealed record AppSettingsDto(CurrencyOptionDto Currency, IReadOnlyList<CurrencyOptionDto> Available);

public sealed record SaveSettingsRequest(string CurrencyCode);

/// <summary>
/// Application wide options. Currency is the only one in the MVP: it changes how money is
/// shown everywhere, so it lives on the server rather than in each browser, and only the
/// owner account can change it.
/// </summary>
public sealed class SettingsService(
    IAppDbContext db,
    IAuditService audit,
    ICurrentUser currentUser,
    ICacheService cache,
    IAccessGuard access,
    IDateTimeProvider clock)
{
    public const string CurrencyKey = "Currency";
    public const string DefaultCurrency = "PKR";

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The currencies the dropdown offers. Decimals and symbol placement differ by
    /// currency, so both travel with the code rather than being assumed.
    /// </summary>
    public static IReadOnlyList<CurrencyOptionDto> Available { get; } =
    [
        new("PKR", "Rs", "Pakistani rupee", 2, true),
        new("USD", "$", "US dollar", 2, true),
        new("EUR", "€", "Euro", 2, true),
        new("GBP", "£", "Pound sterling", 2, true),
        new("AED", "AED", "UAE dirham", 2, true),
        new("SAR", "SAR", "Saudi riyal", 2, true),
        new("INR", "₹", "Indian rupee", 2, true),
        new("BDT", "৳", "Bangladeshi taka", 2, true),
        new("TRY", "₺", "Turkish lira", 2, true),
        new("CNY", "¥", "Chinese yuan", 2, true),
        new("JPY", "¥", "Japanese yen", 0, true),
        new("KES", "KSh", "Kenyan shilling", 2, true),
        new("NGN", "₦", "Nigerian naira", 2, true),
        new("ZAR", "R", "South African rand", 2, true),
        new("CAD", "C$", "Canadian dollar", 2, true),
        new("AUD", "A$", "Australian dollar", 2, true),
    ];

    public async Task<AppSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var code = await CurrencyCodeAsync(ct).ConfigureAwait(false);
        return new AppSettingsDto(Resolve(code), Available);
    }

    public Task<string> CurrencyCodeAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync(
            CacheKeys.Lookup("currency"),
            async () =>
            {
                var stored = await db.AppSettings
                    .AsNoTracking()
                    .Where(s => s.Key == CurrencyKey)
                    .Select(s => s.Value)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                return stored ?? DefaultCurrency;
            },
            Ttl);

    public async Task<AppSettingsDto> UpdateAsync(SaveSettingsRequest request, CancellationToken ct = default)
    {
        await access.EnsureAsync(Module.Settings, Access.Write, ct).ConfigureAwait(false);

        if (!currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only the owner account can change application settings.");
        }

        var code = (request.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();

        if (!Available.Any(c => c.Code == code))
        {
            throw ValidationFailedException.ForField(
                nameof(request.CurrencyCode),
                "Choose a currency from the list.");
        }

        var setting = await db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == CurrencyKey, ct)
            .ConfigureAwait(false);

        var before = setting?.Value ?? DefaultCurrency;

        if (setting is null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = CurrencyKey,
                Value = code,
                CreatedAt = clock.UtcNow,
                CreatedBy = currentUser.Username,
            });
        }
        else
        {
            setting.Value = code;
            setting.UpdatedAt = clock.UtcNow;
            setting.UpdatedBy = currentUser.Username;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Everyone reads the cached value, so it has to go the moment it changes.
        cache.RemoveGroup(CacheKeys.Lookups);

        await audit.LogAsync(
            Module.Settings,
            AuditAction.Update,
            nameof(AppSetting),
            setting?.Id,
            $"Currency {before} -> {code}",
            ct: ct).ConfigureAwait(false);

        return new AppSettingsDto(Resolve(code), Available);
    }

    private static CurrencyOptionDto Resolve(string code)
        => Available.FirstOrDefault(c => c.Code == code)
            ?? Available.First(c => c.Code == DefaultCurrency);
}
