using System.Drawing;
using System.Drawing.Text;

namespace TechShop.WinForms.Theming;

/// <summary>
/// The type scale, at a 1.25 ratio between steps so hierarchy reads without relying on
/// colour. The web app ships Inter; on Windows the native UI face is the right call, so
/// this resolves Segoe UI Variable where it exists and falls back cleanly where it does not.
///
/// Sizes are points. The web tokens are in px, converted at the WinForms 96dpi baseline
/// (px * 0.75); per-monitor DPI scaling is handled by the framework, not by picking
/// different point sizes.
/// </summary>
public static class Typography
{
    private static readonly string Family = ResolveFamily();
    private static readonly string MonoFamily = ResolveMono();
    private static readonly Dictionary<(float Size, FontStyle Style, bool Mono), Font> Cache = [];
    private static readonly Lock Gate = new();

    // 11px — table micro labels, section captions in the sidebar
    public const float Size2Xs = 8.25f;

    // 12px — captions and helper text
    public const float SizeXs = 9f;

    // 13px — grid body and dense forms
    public const float SizeSm = 9.75f;

    // 14px — app body
    public const float SizeBase = 10.5f;

    // 16px — form inputs
    public const float SizeMd = 12f;

    // 18px — card titles
    public const float SizeLg = 13.5f;

    // 22px — h3
    public const float SizeXl = 16.5f;

    // 28px — h2 and KPI values
    public const float Size2Xl = 21f;

    // 34px — h1
    public const float Size3Xl = 25.5f;

    // 42px — the POS total
    public const float Size4Xl = 31.5f;

    public static Font Get(float size, FontStyle style = FontStyle.Regular)
        => Resolve(size, style, mono: false);

    public static Font Mono(float size, FontStyle style = FontStyle.Regular)
        => Resolve(size, style, mono: true);

    public static Font Body => Get(SizeBase);
    public static Font BodyStrong => Get(SizeBase, FontStyle.Bold);
    public static Font Caption => Get(SizeXs);
    public static Font GridBody => Get(SizeSm);
    public static Font Input => Get(SizeMd);
    public static Font CardTitle => Get(SizeLg, FontStyle.Bold);
    public static Font H1 => Get(Size3Xl, FontStyle.Bold);
    public static Font H2 => Get(Size2Xl, FontStyle.Bold);
    public static Font H3 => Get(SizeXl, FontStyle.Bold);

    /// <summary>Money and quantities, so columns of figures line up on the digit.</summary>
    public static Font Numeric(float size, FontStyle style = FontStyle.Regular) => Mono(size, style);

    private static Font Resolve(float size, FontStyle style, bool mono)
    {
        lock (Gate)
        {
            var key = (size, style, mono);
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var font = new Font(mono ? MonoFamily : Family, size, style, GraphicsUnit.Point);
            Cache[key] = font;
            return font;
        }
    }

    private static string ResolveFamily()
        => FirstInstalled(["Segoe UI Variable Text", "Segoe UI", "Tahoma"]) ?? "Microsoft Sans Serif";

    private static string ResolveMono()
        => FirstInstalled(["Cascadia Mono", "Consolas", "Lucida Console"]) ?? "Courier New";

    private static string? FirstInstalled(IReadOnlyList<string> candidates)
    {
        using var installed = new InstalledFontCollection();
        var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.FirstOrDefault(names.Contains);
    }
}
