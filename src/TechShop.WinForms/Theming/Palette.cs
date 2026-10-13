using System.Drawing;

namespace TechShop.WinForms.Theming;

/// <summary>
/// The colour tokens, converted from the OKLCH source of truth the web app uses to sRGB.
/// The neutral ramp carries a little chroma toward the indigo brand hue (264) so white
/// cards sit against the app background without looking like two unrelated greys.
///
/// Nothing in the UI names a colour literal. If a screen needs a colour it is because a
/// token is missing, and the token belongs here.
/// </summary>
public static class Palette
{
    private static Color Hex(string rgb) => ColorTranslator.FromHtml("#" + rgb);

    // ---------- Neutrals ----------
    public static readonly Color Neutral0 = Hex("FFFFFF");
    public static readonly Color Neutral25 = Hex("F8FAFD");
    public static readonly Color Neutral50 = Hex("F2F4F8");
    public static readonly Color Neutral100 = Hex("E6E9EF");
    public static readonly Color Neutral200 = Hex("D6D9E0");
    public static readonly Color Neutral300 = Hex("BBBFC7");
    public static readonly Color Neutral400 = Hex("9499A2");
    public static readonly Color Neutral500 = Hex("6D727B");
    public static readonly Color Neutral600 = Hex("585D68");
    public static readonly Color Neutral700 = Hex("3E444E");
    public static readonly Color Neutral800 = Hex("252A36");
    public static readonly Color Neutral900 = Hex("141924");

    // ---------- Text ----------
    // Body sits at neutral-700, not a light grey. Light grey body copy is the fastest way
    // to make a dashboard unreadable under shop lighting.
    public static readonly Color Ink = Neutral800;
    public static readonly Color InkStrong = Neutral900;
    public static readonly Color InkBody = Neutral700;
    public static readonly Color InkMuted = Neutral500;
    public static readonly Color InkInverse = Neutral0;

    // ---------- Surfaces ----------
    public static readonly Color Surface = Neutral0;
    public static readonly Color SurfaceSunken = Neutral25;
    public static readonly Color SurfaceHover = Neutral50;
    public static readonly Color AppBackground = Neutral25;
    public static readonly Color Border = Neutral200;
    public static readonly Color BorderSubtle = Neutral100;
    public static readonly Color BorderStrong = Neutral300;

    // ---------- Accents ----------
    // Each accent carries a solid for fills, a text shade that clears 4.5:1 on its own
    // soft background, and a soft tint for chips and card washes.
    public static readonly Color Indigo = Hex("2A60E8");
    public static readonly Color IndigoText = Hex("1640AD");
    public static readonly Color IndigoSoft = Hex("E8F2FF");
    public static readonly Color IndigoHover = Hex("1A4CD3");
    public static readonly Color IndigoActive = Hex("113FBB");

    public static readonly Color Violet = Hex("7C41E2");
    public static readonly Color VioletText = Hex("5826A7");
    public static readonly Color VioletSoft = Hex("F2EEFF");

    public static readonly Color Emerald = Hex("008D59");
    public static readonly Color EmeraldText = Hex("005F39");
    public static readonly Color EmeraldSoft = Hex("DBF9E9");

    public static readonly Color Amber = Hex("E58E00");
    public static readonly Color AmberText = Hex("854500");
    public static readonly Color AmberSoft = Hex("FFF0CB");

    public static readonly Color Rose = Hex("DC214C");
    public static readonly Color RoseText = Hex("A10031");
    public static readonly Color RoseSoft = Hex("FFEBEB");
    public static readonly Color RoseHover = Hex("C6003C");

    public static readonly Color Cyan = Hex("0098B4");
    public static readonly Color CyanText = Hex("005D79");
    public static readonly Color CyanSoft = Hex("D9F8FF");

    public static readonly Color Orange = Hex("E35C01");
    public static readonly Color OrangeText = Hex("973200");
    public static readonly Color OrangeSoft = Hex("FFEADA");

    public static readonly Color Pink = Hex("D13D91");
    public static readonly Color PinkText = Hex("931A62");
    public static readonly Color PinkSoft = Hex("FFEAF5");

    public static readonly Color Slate = Neutral500;
    public static readonly Color SlateText = Neutral700;
    public static readonly Color SlateSoft = Neutral50;

    // ---------- Semantic roles ----------
    public static readonly Color Primary = Indigo;
    public static readonly Color PrimaryText = IndigoText;
    public static readonly Color PrimarySoft = IndigoSoft;
    public static readonly Color PrimaryHover = IndigoHover;
    public static readonly Color PrimaryActive = IndigoActive;

    public static readonly Color Success = Emerald;
    public static readonly Color SuccessText = EmeraldText;
    public static readonly Color SuccessSoft = EmeraldSoft;

    public static readonly Color Warning = Amber;
    public static readonly Color WarningText = AmberText;
    public static readonly Color WarningSoft = AmberSoft;

    public static readonly Color Danger = Rose;
    public static readonly Color DangerText = RoseText;
    public static readonly Color DangerSoft = RoseSoft;
    public static readonly Color DangerHover = RoseHover;

    public static readonly Color Info = Cyan;
    public static readonly Color InfoText = CyanText;
    public static readonly Color InfoSoft = CyanSoft;

    // ---------- Sidebar ----------
    public static readonly Color SidebarBackground = Neutral0;
    public static readonly Color SidebarInk = Neutral700;
    public static readonly Color SidebarInkMuted = Neutral500;
    public static readonly Color SidebarActiveFill = IndigoSoft;
    public static readonly Color SidebarActiveInk = IndigoText;
    public static readonly Color SidebarHoverFill = Neutral50;

    /// <summary>Blends two colours, used for hover and pressed states on flat fills.</summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            (int)Math.Round(from.R + ((to.R - from.R) * amount)),
            (int)Math.Round(from.G + ((to.G - from.G) * amount)),
            (int)Math.Round(from.B + ((to.B - from.B) * amount)));
    }
}
