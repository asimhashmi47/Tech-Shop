using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Interfaces;

namespace TechShop.Application.Features.Common;

/// <summary>
/// Builds per shop document numbers in the form SHOPCODE-YYYYMM-0001. The sequence restarts
/// each month and is derived from the highest existing number so no counter table is needed.
/// </summary>
public sealed class DocumentNumberService(IAppDbContext db, IDateTimeProvider clock)
{
    public async Task<string> NextInvoiceNoAsync(long shopId, CancellationToken ct = default)
    {
        var prefix = await PrefixAsync(shopId, ct).ConfigureAwait(false);
        var used = await db.SalesInvoices
            .Where(i => i.ShopId == shopId && i.InvoiceNo.StartsWith(prefix))
            .Select(i => i.InvoiceNo)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return $"{prefix}{NextSequence(used, prefix)}";
    }

    public async Task<string> NextBillNoAsync(long shopId, CancellationToken ct = default)
    {
        var prefix = await PrefixAsync(shopId, ct, "P");
        var used = await db.PurchaseBills
            .Where(b => b.ShopId == shopId && b.BillNo.StartsWith(prefix))
            .Select(b => b.BillNo)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return $"{prefix}{NextSequence(used, prefix)}";
    }

    private async Task<string> PrefixAsync(long shopId, CancellationToken ct, string? kind = null)
    {
        var code = await db.Shops
            .Where(s => s.Id == shopId)
            .Select(s => s.Code)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? "SHOP";

        var month = clock.UtcNow.ToString("yyyyMM", CultureInfo.InvariantCulture);
        return kind is null ? $"{code}-{month}-" : $"{code}-{kind}{month}-";
    }

    private static string NextSequence(IEnumerable<string> existing, string prefix)
    {
        var highest = 0;

        foreach (var value in existing)
        {
            var tail = value[prefix.Length..];
            if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > highest)
            {
                highest = number;
            }
        }

        return (highest + 1).ToString("D4", CultureInfo.InvariantCulture);
    }
}
