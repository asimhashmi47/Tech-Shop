using System.Drawing;
using Krypton.Toolkit;
using TechShop.Application.Common.Exceptions;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Controls;

/// <summary>
/// The modal used to add or change a record: a titled header, a stack of labelled fields,
/// and Save / Cancel.
///
/// Validation errors come back from the service keyed by field name, and
/// <see cref="ShowFieldErrors"/> puts each message under the input it belongs to rather
/// than in a message box. The operator should not have to work out which of six fields
/// the complaint is about.
/// </summary>
public abstract class EditorForm : Form
{
    private readonly Panel _fields;
    private readonly AppButton _save;
    private readonly Label _error;
    private readonly Dictionary<string, Label> _fieldErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Control> _fieldInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Control> _explicitlySized = [];
    private int _y;
    private bool _busy;

    /// <summary>
    /// The inputs on this form, by the name their validation errors arrive under. The
    /// dialog already has to know this to put an error under the right box; exposing it
    /// lets the QA harness fill a form the way an operator would.
    /// </summary>
    internal IReadOnlyDictionary<string, Control> Inputs => _fieldInputs;

    /// <summary>Runs the same save the Save button runs, for the QA harness.</summary>
    internal Task SubmitForTestAsync() => SubmitAsync();

    /// <summary>
    /// The summary error currently shown, or null.
    ///
    /// Read from the text rather than from Visible: a Label on a form that has not been
    /// shown reports Visible as false whatever it was set to, which would make this lie
    /// to the QA harness.
    /// </summary>
    internal string? VisibleError
        => string.IsNullOrEmpty(_error.Text) ? null : _error.Text;

    /// <summary>The message currently shown under one field, or null.</summary>
    internal string? ErrorFor(string field)
        => _fieldErrors.TryGetValue(field, out var slot) && !string.IsNullOrEmpty(slot.Text)
            ? slot.Text
            : null;

    protected EditorForm(string title, string? subtitle = null, int width = 520)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Palette.Surface;
        Font = Typography.Body;
        ClientSize = new Size(width, 400);

        var heading = new Label
        {
            Text = title,
            AutoSize = true,
            Font = Typography.H3,
            ForeColor = Palette.InkStrong,
            Location = new Point(Metrics.Space6, Metrics.Space5),
        };

        var sub = new Label
        {
            Text = subtitle ?? string.Empty,
            AutoSize = true,
            Font = Typography.Get(Typography.SizeSm),
            ForeColor = Palette.InkMuted,
            Location = new Point(Metrics.Space6 + 2, Metrics.Space5 + 32),
            Visible = !string.IsNullOrWhiteSpace(subtitle),
        };

        _fields = new Panel
        {
            Location = new Point(Metrics.Space6, sub.Visible ? 96 : 76),
            Width = width - (Metrics.Space6 * 2),
            AutoSize = false,
            BackColor = Color.Transparent,
        };

        _error = new Label
        {
            AutoSize = false,
            Width = width - (Metrics.Space6 * 2),
            Height = 34,
            ForeColor = Palette.DangerText,
            Font = Typography.Get(Typography.SizeSm),
            Visible = false,
        };

        _save = new AppButton { Variant = ButtonVariant.Primary, Text = "Save", Width = 110 };
        _save.Click += async (_, _) => await SubmitAsync().ConfigureAwait(true);

        var cancel = new AppButton { Variant = ButtonVariant.Secondary, Text = "Cancel", Width = 100 };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Footer = new Panel { Height = 64, Dock = DockStyle.Bottom, BackColor = Palette.SurfaceSunken };
        Footer.Controls.Add(_save);
        Footer.Controls.Add(cancel);
        Footer.Resize += (_, _) =>
        {
            var y = (Footer.Height - _save.Height) / 2;
            _save.Location = new Point(Footer.Width - _save.Width - Metrics.Space6, y);
            cancel.Location = new Point(_save.Left - cancel.Width - Metrics.Space2, y);
        };
        Footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Palette.BorderSubtle);
            e.Graphics.DrawLine(pen, 0, 0, Footer.Width, 0);
        };

        Controls.Add(_fields);
        Controls.Add(_error);
        Controls.Add(sub);
        Controls.Add(heading);
        Controls.Add(Footer);

        AcceptButton = _save;
        CancelButton = cancel;
    }

    protected Panel Footer { get; }

    /// <summary>Does the write. Throwing a validation failure is the normal unhappy path.</summary>
    protected abstract Task SaveAsync();

    /// <summary>Adds a labelled field and returns the input so the caller can keep it.</summary>
    protected TControl Field<TControl>(string label, TControl input, string? help = null, string? name = null)
        where TControl : Control
    {
        var caption = Theme.FieldLabel(label);
        caption.Location = new Point(0, _y);
        _fields.Controls.Add(caption);
        _y += 20;

        input.Location = new Point(0, _y);

        // Stretch to the form width unless the caller deliberately sized it. Krypton
        // controls default to 100px, so a "only widen small controls" rule left every
        // field in every dialog as a stubby box.
        if (!_explicitlySized.Contains(input))
        {
            input.Width = _fields.Width;
        }

        _fields.Controls.Add(input);
        _y += input.Height + 4;

        // A per-field error slot, reserved now so showing a message later does not
        // reflow the form under the operator's cursor.
        var error = new Label
        {
            AutoSize = false,
            Location = new Point(0, _y),
            Width = _fields.Width,
            Height = 16,
            ForeColor = Palette.DangerText,
            Font = Typography.Get(Typography.Size2Xs),
            Visible = false,
        };
        _fields.Controls.Add(error);
        _fieldErrors[name ?? label] = error;
        _fieldInputs[name ?? label] = input;
        _y += 18;

        if (!string.IsNullOrWhiteSpace(help))
        {
            var hint = new Label
            {
                Text = help,
                AutoSize = false,
                Location = new Point(0, _y - 2),
                Width = _fields.Width,
                Height = 16,
                ForeColor = Palette.InkMuted,
                Font = Typography.Get(Typography.Size2Xs),
            };
            _fields.Controls.Add(hint);
            _y += 18;
        }

        _y += Metrics.Space2;
        return input;
    }

    /// <summary>Two fields on one row, for things that belong together such as cost and price.</summary>
    protected (TLeft Left, TRight Right) FieldPair<TLeft, TRight>(
        string leftLabel,
        TLeft left,
        string rightLabel,
        TRight right)
        where TLeft : Control
        where TRight : Control
    {
        var half = (_fields.Width - Metrics.Space4) / 2;

        var leftCaption = Theme.FieldLabel(leftLabel);
        leftCaption.Location = new Point(0, _y);
        var rightCaption = Theme.FieldLabel(rightLabel);
        rightCaption.Location = new Point(half + Metrics.Space4, _y);
        _fields.Controls.Add(leftCaption);
        _fields.Controls.Add(rightCaption);
        _y += 20;

        left.Location = new Point(0, _y);
        left.Width = half;
        right.Location = new Point(half + Metrics.Space4, _y);
        right.Width = half;
        _explicitlySized.Add(left);
        _explicitlySized.Add(right);
        _fieldInputs[leftLabel] = left;
        _fieldInputs[rightLabel] = right;
        _fields.Controls.Add(left);
        _fields.Controls.Add(right);
        _y += Math.Max(left.Height, right.Height) + Metrics.Space4;

        return (left, right);
    }

    /// <summary>Call once the fields are added, to size the dialog to its content.</summary>
    protected void FinishLayout()
    {
        _fields.Height = _y;
        _error.Location = new Point(Metrics.Space6, _fields.Bottom + Metrics.Space2);
        ClientSize = new Size(ClientSize.Width, _error.Bottom + Metrics.Space2 + Footer.Height);
    }

    private async Task SubmitAsync()
    {
        if (_busy)
        {
            return;
        }

        ClearErrors();
        SetBusy(true);

        try
        {
            await SaveAsync().ConfigureAwait(true);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ValidationFailedException validation)
        {
            ShowFieldErrors(validation);
        }
        catch (ConflictException conflict)
        {
            ShowError(conflict.Message);
        }
        catch (ForbiddenException forbidden)
        {
            ShowError(forbidden.Message);
        }
        catch (NotFoundException missing)
        {
            ShowError(missing.Message);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Puts each message under the field it names. Anything that does not match a field
    /// on this form still has to be shown, so it falls back to the summary line.
    /// </summary>
    private void ShowFieldErrors(ValidationFailedException validation)
    {
        var unmatched = new List<string>();

        foreach (var (field, messages) in validation.Errors)
        {
            var message = string.Join(" ", messages);

            if (_fieldErrors.TryGetValue(field, out var slot))
            {
                slot.Text = message;
                slot.Visible = true;
            }
            else
            {
                unmatched.Add(message);
            }
        }

        if (unmatched.Count > 0)
        {
            ShowError(string.Join(Environment.NewLine, unmatched));
        }
    }

    protected void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = true;
    }

    private void ClearErrors()
    {
        // Text is cleared as well as hidden, so "has text" is a reliable answer to
        // "is this error currently being shown".
        _error.Text = string.Empty;
        _error.Visible = false;

        foreach (var slot in _fieldErrors.Values)
        {
            slot.Text = string.Empty;
            slot.Visible = false;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _save.Enabled = !busy;
        _save.Text = busy ? "Saving…" : "Save";
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    /// <summary>A combo bound to a lookup list, showing Name and carrying Id.</summary>
    protected static KryptonComboBox Lookup<TItem>(
        IEnumerable<TItem> items,
        Func<TItem, long> id,
        Func<TItem, string> name,
        long? selected = null)
    {
        var combo = Theme.ComboBox();

        foreach (var item in items)
        {
            combo.Items.Add(new LookupItem(id(item), name(item)));
        }

        if (selected is { } value)
        {
            foreach (var entry in combo.Items.OfType<LookupItem>())
            {
                if (entry.Id == value)
                {
                    combo.SelectedItem = entry;
                    break;
                }
            }
        }
        else if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }

        return combo;
    }

    protected static long? SelectedId(KryptonComboBox combo)
        => combo.SelectedItem is LookupItem item ? item.Id : null;

    protected sealed record LookupItem(long Id, string Name)
    {
        public override string ToString() => Name;
    }
}
