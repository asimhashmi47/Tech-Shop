namespace TechShop.WinForms.Theming;

/// <summary>Spacing, radius and layout tokens. A 4px base, same scale as the web app.</summary>
public static class Metrics
{
    public const int Space1 = 4;
    public const int Space2 = 8;
    public const int Space3 = 12;
    public const int Space4 = 16;
    public const int Space5 = 20;
    public const int Space6 = 24;
    public const int Space8 = 32;
    public const int Space10 = 40;
    public const int Space12 = 48;
    public const int Space16 = 64;

    public const int RadiusXs = 4;
    public const int RadiusSm = 6;
    public const int RadiusMd = 8;
    public const int RadiusLg = 12;
    public const int RadiusXl = 16;
    public const int Radius2Xl = 20;

    public const int SidebarWidth = 256;
    public const int SidebarWidthCollapsed = 68;
    public const int TopBarHeight = 60;

    public const int ControlHeight = 38;
    public const int ControlHeightSmall = 32;
    public const int GridRowHeight = 36;
    public const int GridHeaderHeight = 40;

    public const int KpiCardWidth = 184;

    // Short enough that eight cards in two rows still leave the dashboard's lists room
    // on a 1080p screen. The figure auto-fits rather than wrapping, so this can be tight.
    public const int KpiCardHeight = 148;
}
