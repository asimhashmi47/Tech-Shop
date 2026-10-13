using Krypton.Toolkit;
using TechShop.Domain.Enums;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// The small pickers several editors need. Enum combos are built from the enum rather
/// than a hand-written list, so adding a payment method cannot leave one screen behind.
/// </summary>
public static class Pickers
{
    public sealed record EnumItem<TEnum>(TEnum Value, string Label)
        where TEnum : struct, Enum
    {
        public override string ToString() => Label;
    }

    public static KryptonComboBox ForEnum<TEnum>(TEnum? selected = null, params TEnum[] exclude)
        where TEnum : struct, Enum
    {
        var combo = Theme.ComboBox();
        var skip = exclude.ToHashSet();

        foreach (var value in Enum.GetValues<TEnum>())
        {
            if (skip.Contains(value))
            {
                continue;
            }

            combo.Items.Add(new EnumItem<TEnum>(value, Format.Humanise(value.ToString()!)));
        }

        var target = selected ?? Enum.GetValues<TEnum>().FirstOrDefault(v => !skip.Contains(v));

        foreach (var item in combo.Items.OfType<EnumItem<TEnum>>())
        {
            if (Equals(item.Value, target))
            {
                combo.SelectedItem = item;
                break;
            }
        }

        if (combo.SelectedItem is null && combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }

        return combo;
    }

    public static TEnum Selected<TEnum>(KryptonComboBox combo, TEnum fallback)
        where TEnum : struct, Enum
        => combo.SelectedItem is EnumItem<TEnum> item ? item.Value : fallback;

    /// <summary>
    /// Payment methods for money actually moving. Credit is excluded because it means
    /// "not paid", which is not a way of paying.
    /// </summary>
    public static KryptonComboBox PaymentMethod(Domain.Enums.PaymentMethod? selected = null)
        => ForEnum<Domain.Enums.PaymentMethod>(
            selected ?? Domain.Enums.PaymentMethod.Cash,
            Domain.Enums.PaymentMethod.Credit);

    public static KryptonComboBox PaymentMethodWithCredit(Domain.Enums.PaymentMethod? selected = null)
        => ForEnum<Domain.Enums.PaymentMethod>(selected ?? Domain.Enums.PaymentMethod.Cash);

    public static KryptonComboBox Direction(PettyCashDirection? selected = null)
        => ForEnum<PettyCashDirection>(selected ?? PettyCashDirection.Out);
}
