using System.Drawing;

namespace TechShop.WinForms.Controls;

/// <summary>
/// One column of a <see cref="DataGridPanel{T}"/>.
///
/// <paramref name="Field"/> is the name the server-side query whitelist knows the column
/// by. Sorting and filtering send that name, which is why a column cannot be sorted on
/// something the service never offered.
/// </summary>
public sealed record GridColumn<T>(
    string Field,
    string Header,
    Func<T, object?> Value)
{
    public int Width { get; init; } = 140;

    /// <summary>Takes the remaining width. At most one column per grid should set it.</summary>
    public bool Fill { get; init; }

    public bool Sortable { get; init; } = true;

    public ContentAlignment Align { get; init; } = ContentAlignment.MiddleLeft;

    /// <summary>Formats the cell. Money and dates pass through the app's formatter.</summary>
    public Func<T, string>? Format { get; init; }

    /// <summary>Per-row foreground, for figures that should read as negative or due.</summary>
    public Func<T, Color?>? Foreground { get; init; }

    /// <summary>Renders the cell as a status chip instead of text.</summary>
    public Func<T, (string Text, Color Fill, Color Ink)>? Chip { get; init; }

    /// <summary>Figures line up on the digit, so money and quantities get the mono face.</summary>
    public bool Numeric { get; init; }
}
