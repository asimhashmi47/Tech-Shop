using System.Drawing;
using Krypton.Toolkit;

namespace TechShop.WinForms.Theming;

/// <summary>
/// Applies the token set to Krypton.
///
/// Krypton ships a palette model that can be defined wholesale, but a full custom palette
/// is several hundred properties and most of them describe chrome this app never shows
/// (ribbons, docking, nav tabs). Instead the global palette is set to the plainest stock
/// theme and each control the app actually uses is styled through its own StateCommon in
/// the factories below. That keeps the styling next to the control it affects.
/// </summary>
public static class Theme
{
    // Krypton's manager publishes the global palette through an instance, so one has to
    // exist and stay alive for the lifetime of the app.
    private static KryptonManager? _manager;

    public static void Apply()
    {
        _manager ??= new KryptonManager();
        _manager.GlobalPaletteMode = PaletteMode.Microsoft365White;
        _manager.GlobalApplyToolstrips = true;
        _manager.GlobalUseThemeFormChromeBorderWidth = false;
    }

    public static KryptonTextBox TextBox(string? cue = null)
    {
        var box = new KryptonTextBox
        {
            CueHint = { CueHintText = cue ?? string.Empty },
            Height = Metrics.ControlHeight,
        };

        box.StateCommon.Content.Color1 = Palette.Ink;
        box.StateCommon.Content.Font = Typography.Get(Typography.SizeBase);
        box.StateCommon.Back.Color1 = Palette.Surface;
        box.StateCommon.Border.Color1 = Palette.Border;
        box.StateCommon.Border.Color2 = Palette.Border;
        box.StateCommon.Border.Rounding = Metrics.RadiusMd;
        box.StateCommon.Border.Width = 1;
        box.StateActive.Border.Color1 = Palette.Primary;
        box.StateActive.Border.Color2 = Palette.Primary;
        box.CueHint.Color1 = Palette.InkMuted;
        box.CueHint.Font = Typography.Get(Typography.SizeBase);
        return box;
    }

    public static KryptonComboBox ComboBox()
    {
        var combo = new KryptonComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Height = Metrics.ControlHeight,
        };

        combo.StateCommon.ComboBox.Content.Color1 = Palette.Ink;
        combo.StateCommon.ComboBox.Content.Font = Typography.Get(Typography.SizeBase);
        combo.StateCommon.ComboBox.Back.Color1 = Palette.Surface;
        combo.StateCommon.ComboBox.Border.Color1 = Palette.Border;
        combo.StateCommon.ComboBox.Border.Color2 = Palette.Border;
        combo.StateCommon.ComboBox.Border.Rounding = Metrics.RadiusMd;
        combo.StateCommon.ComboBox.Border.Width = 1;
        combo.StateCommon.Item.Content.ShortText.Font = Typography.Get(Typography.SizeBase);
        combo.StateActive.ComboBox.Border.Color1 = Palette.Primary;
        return combo;
    }

    public static KryptonDateTimePicker DatePicker()
    {
        var picker = new KryptonDateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd MMM yyyy",
            Height = Metrics.ControlHeight,
        };

        picker.StateCommon.Content.Color1 = Palette.Ink;
        picker.StateCommon.Content.Font = Typography.Get(Typography.SizeBase);
        picker.StateCommon.Back.Color1 = Palette.Surface;
        picker.StateCommon.Border.Color1 = Palette.Border;
        picker.StateCommon.Border.Color2 = Palette.Border;
        picker.StateCommon.Border.Rounding = Metrics.RadiusMd;
        picker.StateCommon.Border.Width = 1;
        return picker;
    }

    public static KryptonNumericUpDown Numeric(int decimals = 2)
    {
        var numeric = new KryptonNumericUpDown
        {
            DecimalPlaces = decimals,
            Maximum = 9_999_999_999m,
            Minimum = 0m,
            Height = Metrics.ControlHeight,
            TextAlign = HorizontalAlignment.Right,
        };

        numeric.StateCommon.Content.Color1 = Palette.Ink;
        numeric.StateCommon.Content.Font = Typography.Numeric(Typography.SizeBase);
        numeric.StateCommon.Back.Color1 = Palette.Surface;
        numeric.StateCommon.Border.Color1 = Palette.Border;
        numeric.StateCommon.Border.Color2 = Palette.Border;
        numeric.StateCommon.Border.Rounding = Metrics.RadiusMd;
        numeric.StateCommon.Border.Width = 1;
        return numeric;
    }

    public static KryptonCheckBox CheckBox(string text)
    {
        var check = new KryptonCheckBox { Text = text };
        check.StateCommon.ShortText.Color1 = Palette.InkBody;
        check.StateCommon.ShortText.Font = Typography.Get(Typography.SizeBase);
        return check;
    }

    /// <summary>A field label: small, muted, sentence case, sitting above its input.</summary>
    public static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Palette.InkMuted,
        Font = Typography.Get(Typography.SizeXs, FontStyle.Bold),
        Margin = new Padding(0, 0, 0, Metrics.Space1),
    };
}
