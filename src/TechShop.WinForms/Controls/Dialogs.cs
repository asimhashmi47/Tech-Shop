using Krypton.Toolkit;

namespace TechShop.WinForms.Controls;

/// <summary>
/// The app's prompts, in one place so the wording and the button order stay consistent.
///
/// Confirmations name the thing they are about to do ("Void invoice INV-0042") rather than
/// asking "Are you sure?", because the operator has usually already forgotten which row
/// they clicked by the time the box appears.
/// </summary>
public static class Dialogs
{
    public static void Info(IWin32Window? owner, string heading, string message)
        => Show(owner, heading, message, KryptonMessageBoxIcon.Information);

    public static void Warn(IWin32Window? owner, string heading, string message)
        => Show(owner, heading, message, KryptonMessageBoxIcon.Warning);

    public static void Error(IWin32Window? owner, string heading, string message)
        => Show(owner, heading, message, KryptonMessageBoxIcon.Error);

    /// <summary>A yes/no question. Returns true only on an explicit yes.</summary>
    public static bool Confirm(IWin32Window? owner, string heading, string message)
        => KryptonMessageBox.Show(
            Owner(owner),
            message,
            heading,
            KryptonMessageBoxButtons.YesNo,
            KryptonMessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>
    /// A confirmation for something that cannot be undone. Separate from
    /// <see cref="Confirm"/> so destructive actions default to No and are never one
    /// careless Enter away.
    /// </summary>
    public static bool ConfirmDestructive(IWin32Window? owner, string heading, string message)
        => KryptonMessageBox.Show(
            Owner(owner),
            message,
            heading,
            KryptonMessageBoxButtons.YesNo,
            KryptonMessageBoxIcon.Warning,
            KryptonMessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private static void Show(IWin32Window? owner, string heading, string message, KryptonMessageBoxIcon icon)
        => KryptonMessageBox.Show(Owner(owner), message, heading, KryptonMessageBoxButtons.OK, icon);

    /// <summary>
    /// A page passes its containing form, which is null while the page is being built.
    /// Falling back to the active form keeps the box modal to something rather than
    /// letting it appear behind the shell.
    /// </summary>
    private static IWin32Window Owner(IWin32Window? owner)
        => owner ?? Form.ActiveForm ?? (IWin32Window)new NullOwner();

    private sealed class NullOwner : IWin32Window
    {
        public IntPtr Handle => IntPtr.Zero;
    }
}
