using System.Drawing;
using Krypton.Toolkit;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Pos;

/// <summary>
/// Takes the money for a sale.
///
/// The amount tendered is typed and the change is worked out live, because that is the
/// bit the cashier is holding up the queue to do in their head. A walk-in has to pay in
/// full — there is no account to put a balance on — so part payment is only offered to a
/// named customer.
/// </summary>
public sealed class PaymentDialog : Form
{
    private readonly decimal _total;
    private readonly bool _walkIn;
    private readonly KryptonComboBox _method;
    private readonly KryptonNumericUpDown _tendered;
    private readonly KryptonTextBox _note;
    private readonly Label _changeLabel;
    private readonly Label _changeValue;
    private readonly Label _error;
    private readonly AppButton _confirm;

    public PaymentDialog(decimal total, bool walkIn)
    {
        _total = total;
        _walkIn = walkIn;

        Text = "Take payment";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 430);
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Palette.Surface;
        Font = Typography.Body;
        ShowIcon = false;

        var dueLabel = new Label
        {
            Text = "AMOUNT DUE",
            AutoSize = true,
            Location = new Point(Metrics.Space6, Metrics.Space5),
            ForeColor = Palette.InkMuted,
            Font = Typography.Get(Typography.Size2Xs, FontStyle.Bold),
        };

        var dueValue = new Label
        {
            Text = Format.Money(total),
            AutoSize = false,
            Size = new Size(400, 52),
            Location = new Point(Metrics.Space6, Metrics.Space5 + 18),
            ForeColor = Palette.InkStrong,
            Font = Typography.Numeric(Typography.Size3Xl, FontStyle.Bold),
        };

        var methodLabel = Theme.FieldLabel("Method");
        methodLabel.Location = new Point(Metrics.Space6, 110);

        _method = Pickers.PaymentMethodWithCredit();
        _method.Location = new Point(Metrics.Space6, 130);
        _method.Width = 400;
        _method.SelectedIndexChanged += (_, _) => Recalculate();

        var tenderedLabel = Theme.FieldLabel(Format.MoneyHeader("Tendered"));
        tenderedLabel.Location = new Point(Metrics.Space6, 182);

        _tendered = Theme.Numeric();
        _tendered.Location = new Point(Metrics.Space6, 202);
        _tendered.Width = 400;
        _tendered.Maximum = 9_999_999_999m;
        _tendered.Value = total;
        _tendered.ValueChanged += (_, _) => Recalculate();

        _changeLabel = new Label
        {
            Text = "CHANGE",
            AutoSize = true,
            Location = new Point(Metrics.Space6, 252),
            ForeColor = Palette.InkMuted,
            Font = Typography.Get(Typography.Size2Xs, FontStyle.Bold),
        };

        _changeValue = new Label
        {
            AutoSize = false,
            Size = new Size(400, 36),
            Location = new Point(Metrics.Space6, 270),
            ForeColor = Palette.EmeraldText,
            Font = Typography.Numeric(Typography.Size2Xl, FontStyle.Bold),
        };

        var noteLabel = Theme.FieldLabel("Note");
        noteLabel.Location = new Point(Metrics.Space6, 312);

        _note = Theme.TextBox("Optional");
        _note.Location = new Point(Metrics.Space6, 332);
        _note.Width = 400;

        _error = new Label
        {
            AutoSize = false,
            Size = new Size(400, 18),
            Location = new Point(Metrics.Space6, 374),
            ForeColor = Palette.DangerText,
            Font = Typography.Get(Typography.Size2Xs),
            Visible = false,
        };

        _confirm = new AppButton { Variant = ButtonVariant.Success, Text = "Confirm", Width = 140, Height = 44 };
        _confirm.Click += (_, _) => Confirm();

        var cancel = new AppButton { Variant = ButtonVariant.Secondary, Text = "Cancel", Width = 100, Height = 44 };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 68, BackColor = Palette.SurfaceSunken };
        footer.Controls.Add(_confirm);
        footer.Controls.Add(cancel);
        footer.Resize += (_, _) =>
        {
            var y = (footer.Height - _confirm.Height) / 2;
            _confirm.Location = new Point(footer.Width - _confirm.Width - Metrics.Space6, y);
            cancel.Location = new Point(_confirm.Left - cancel.Width - Metrics.Space2, y);
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Palette.BorderSubtle);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        Controls.AddRange(
        [
            dueLabel, dueValue,
            methodLabel, _method,
            tenderedLabel, _tendered,
            _changeLabel, _changeValue,
            noteLabel, _note,
            _error,
            footer,
        ]);

        AcceptButton = _confirm;
        CancelButton = cancel;

        Shown += (_, _) =>
        {
            _tendered.Focus();
            _tendered.Select(0, _tendered.Text.Length);
        };

        Recalculate();
    }

    public decimal PaidNow { get; private set; }

    public PaymentMethod Method { get; private set; } = PaymentMethod.Cash;

    public string? Note { get; private set; }

    /// <summary>Change to hand back, which is only ever possible on a cash sale.</summary>
    public decimal Change { get; private set; }

    private void Recalculate()
    {
        var method = Pickers.Selected(_method, PaymentMethod.Cash);
        var credit = method == PaymentMethod.Credit;

        // On credit nothing is handed over now, so the tendered box is meaningless.
        _tendered.Enabled = !credit;

        if (credit)
        {
            _tendered.Value = 0m;
            _changeValue.Text = Format.Money(0m);
            _changeLabel.Text = "CHANGE";
            _error.Visible = _walkIn;
            _error.Text = _walkIn ? "A walk-in sale cannot go on credit. Choose a customer first." : string.Empty;
            _confirm.Enabled = !_walkIn;
            return;
        }

        _error.Visible = false;
        _confirm.Enabled = true;

        // Only cash produces change; a card or bank payment is taken for the exact
        // amount, so anything above the total there is an input mistake.
        var tendered = _tendered.Value;

        if (method == PaymentMethod.Cash && tendered > _total)
        {
            Change = tendered - _total;
            _changeLabel.Text = "CHANGE";
            _changeValue.ForeColor = Palette.EmeraldText;
            _changeValue.Text = Format.Money(Change);
        }
        else if (tendered < _total)
        {
            Change = 0m;
            _changeLabel.Text = _walkIn ? "SHORT BY" : "REMAINING ON ACCOUNT";
            _changeValue.ForeColor = _walkIn ? Palette.DangerText : Palette.WarningText;
            _changeValue.Text = Format.Money(_total - tendered);
        }
        else
        {
            Change = 0m;
            _changeLabel.Text = "CHANGE";
            _changeValue.ForeColor = Palette.InkMuted;
            _changeValue.Text = Format.Money(0m);
        }
    }

    private void Confirm()
    {
        var method = Pickers.Selected(_method, PaymentMethod.Cash);
        var tendered = _tendered.Value;

        if (method == PaymentMethod.Credit && _walkIn)
        {
            ShowError("A walk-in sale cannot go on credit.");
            return;
        }

        if (method != PaymentMethod.Credit && tendered < _total && _walkIn)
        {
            ShowError("A walk-in sale has to be paid in full.");
            return;
        }

        Method = method;
        Note = string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim();

        // Never bank more than the sale: the surplus is change, not revenue.
        PaidNow = method == PaymentMethod.Credit ? 0m : Math.Min(tendered, _total);
        Change = method == PaymentMethod.Cash ? Math.Max(tendered - _total, 0m) : 0m;

        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = true;
    }
}
