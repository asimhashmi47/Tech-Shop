using System.Drawing;
using Krypton.Toolkit;
using TechShop.Application.Common.Exceptions;
using TechShop.Application.Features.Catalog;
using TechShop.Application.Features.Parties;
using TechShop.Application.Features.Purchases;
using TechShop.Application.Features.Shops;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Purchases;

/// <summary>
/// Enters a supplier bill: header at the top, lines in the middle, totals and payment at
/// the bottom.
///
/// Quantity and cost are entered per line because a delivery often arrives at a different
/// price than the catalogue holds, and the cost on the bill is what the stock is valued
/// at — so it has to be the figure on the paper, not the one in the product record.
/// </summary>
public sealed class PurchaseBillEditor : Form
{
    private readonly PurchaseService _purchases;
    private readonly IReadOnlyList<ProductDto> _products;
    private readonly List<Line> _lines = [];

    private readonly KryptonComboBox _shop;
    private readonly KryptonComboBox _supplier;
    private readonly KryptonDateTimePicker _date;
    private readonly KryptonComboBox _product;
    private readonly KryptonNumericUpDown _qty;
    private readonly KryptonNumericUpDown _cost;
    private readonly DataGridView _grid;
    private readonly KryptonNumericUpDown _paidNow;
    private readonly KryptonComboBox _method;
    private readonly KryptonTextBox _note;
    private readonly Label _total;
    private readonly Label _error;
    private readonly AppButton _save;
    private bool _busy;

    private sealed record Line(long ProductId, string Sku, string Name, int Qty, decimal UnitCost)
    {
        public decimal LineTotal => Qty * UnitCost;
    }

    public PurchaseBillEditor(
        PurchaseService purchases,
        IReadOnlyList<ShopDto> shops,
        IReadOnlyList<SupplierDto> suppliers,
        IReadOnlyList<ProductDto> products,
        long? activeShopId)
    {
        _purchases = purchases;
        _products = products;

        Text = "New purchase bill";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 700);
        MinimumSize = new Size(860, 600);
        BackColor = Palette.AppBackground;
        Font = Typography.Body;
        ShowIcon = false;
        MinimizeBox = false;

        // ---------- header
        var header = Card();
        header.Dock = DockStyle.Top;
        header.Height = 96;

        _shop = Theme.ComboBox();
        foreach (var shop in shops)
        {
            _shop.Items.Add(new Item(shop.Id, shop.Name));
        }

        Select(_shop, activeShopId);

        _supplier = Theme.ComboBox();
        foreach (var supplier in suppliers)
        {
            _supplier.Items.Add(new Item(supplier.Id, supplier.Name));
        }

        if (_supplier.Items.Count > 0)
        {
            _supplier.SelectedIndex = 0;
        }

        _date = Theme.DatePicker();
        _date.Value = DateTime.Today;

        Place(header, "Shop", _shop, Metrics.Space5, 260);
        Place(header, "Supplier", _supplier, Metrics.Space5 + 276, 280);
        Place(header, "Bill date", _date, Metrics.Space5 + 572, 170);

        // ---------- add a line
        var adder = Card();
        adder.Dock = DockStyle.Top;
        adder.Height = 96;
        adder.Margin = new Padding(0, Metrics.Space3, 0, 0);

        _product = Theme.ComboBox();
        foreach (var product in products)
        {
            _product.Items.Add(new Item(product.Id, $"{product.Sku}  {product.Name}"));
        }

        if (_product.Items.Count > 0)
        {
            _product.SelectedIndex = 0;
        }

        _product.SelectedIndexChanged += (_, _) => PrefillCost();

        _qty = Theme.Numeric(0);
        _qty.Minimum = 1m;
        _qty.Value = 1m;

        _cost = Theme.Numeric();

        var add = new AppButton { Variant = ButtonVariant.Primary, Text = "Add line", Width = 110 };
        add.Click += (_, _) => AddLine();

        Place(adder, "Product", _product, Metrics.Space5, 420);
        Place(adder, "Quantity", _qty, Metrics.Space5 + 436, 110);
        Place(adder, Format.MoneyHeader("Unit cost"), _cost, Metrics.Space5 + 558, 150);
        add.Location = new Point(Metrics.Space5 + 722, 46);
        adder.Controls.Add(add);

        PrefillCost();

        // ---------- lines
        _grid = BuildGrid();
        var linesCard = Card();
        linesCard.Dock = DockStyle.Fill;
        linesCard.Margin = new Padding(0, Metrics.Space3, 0, 0);
        linesCard.Controls.Add(_grid);

        var remove = new AppButton { Variant = ButtonVariant.Ghost, Text = "Remove line" };
        remove.FitToText(Metrics.Space3);
        remove.Height = Metrics.ControlHeightSmall;
        remove.Click += (_, _) => RemoveLine();

        var linesToolbar = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Color.Transparent };
        linesToolbar.Controls.Add(remove);
        linesToolbar.Resize += (_, _) => remove.Location = new Point(0, Metrics.Space2);
        linesCard.Controls.Add(linesToolbar);

        // ---------- footer
        _total = new Label
        {
            AutoSize = false,
            Size = new Size(380, 30),
            Font = Typography.Numeric(Typography.SizeXl, FontStyle.Bold),
            ForeColor = Palette.InkStrong,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
        };

        _paidNow = Theme.Numeric();
        _paidNow.Maximum = 9_999_999_999m;

        _method = Pickers.PaymentMethod(PaymentMethod.Bank);
        _note = Theme.TextBox("Optional");

        _error = new Label
        {
            AutoSize = false,
            Height = 20,
            ForeColor = Palette.DangerText,
            Font = Typography.Get(Typography.SizeSm),
            Visible = false,
            BackColor = Color.Transparent,
        };

        _save = new AppButton { Variant = ButtonVariant.Primary, Text = "Save bill", Width = 130, Height = 42 };
        _save.Click += async (_, _) => await SubmitAsync().ConfigureAwait(true);

        var cancel = new AppButton { Variant = ButtonVariant.Secondary, Text = "Cancel", Width = 100, Height = 42 };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 150, BackColor = Palette.Surface };
        footer.Controls.Add(_total);
        footer.Controls.Add(_error);
        footer.Controls.Add(_save);
        footer.Controls.Add(cancel);

        Place(footer, Format.MoneyHeader("Paid now"), _paidNow, Metrics.Space5, 160);
        Place(footer, "Method", _method, Metrics.Space5 + 176, 150);
        Place(footer, "Note", _note, Metrics.Space5 + 342, 300);

        footer.Resize += (_, _) =>
        {
            _total.Location = new Point(Metrics.Space5, 100);
            _error.Location = new Point(Metrics.Space5, 128);
            _error.Width = footer.Width - Metrics.Space10;
            _save.Location = new Point(footer.Width - _save.Width - Metrics.Space5, 94);
            cancel.Location = new Point(_save.Left - cancel.Width - Metrics.Space2, 94);
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Palette.BorderSubtle);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.AppBackground,
            Padding = new Padding(Metrics.Space5, Metrics.Space5, Metrics.Space5, Metrics.Space5),
        };
        body.Controls.Add(linesCard);
        body.Controls.Add(adder);
        body.Controls.Add(header);

        Controls.Add(body);
        Controls.Add(footer);

        CancelButton = cancel;
        Refresh();
    }

    /// <summary>
    /// Test seams. The bill editor is a form of its own rather than an
    /// <see cref="EditorForm"/>, so the QA harness drives it through these.
    /// </summary>
    internal string? VisibleError => _error.Visible ? _error.Text : null;

    internal Task SubmitForTestAsync() => SubmitAsync();

    internal bool AddLineForTest(long productId, int qty, decimal unitCost)
    {
        foreach (var item in _product.Items.OfType<Item>())
        {
            if (item.Id != productId)
            {
                continue;
            }

            _product.SelectedItem = item;
            _qty.Value = qty;
            _cost.Value = unitCost;
            AddLine();
            return _lines.Any(l => l.ProductId == productId);
        }

        return false;
    }

    private void PrefillCost()
    {
        // The catalogue cost is a starting point, not the answer: whatever is on the
        // delivery note wins, so the field stays editable.
        if (SelectedId(_product) is { } id
            && _products.FirstOrDefault(p => p.Id == id) is { } product)
        {
            _cost.Value = product.CostPrice;
        }
    }

    private void AddLine()
    {
        if (SelectedId(_product) is not { } productId)
        {
            ShowError("Choose a product.");
            return;
        }

        var product = _products.FirstOrDefault(p => p.Id == productId);
        if (product is null)
        {
            return;
        }

        var qty = (int)_qty.Value;
        if (qty <= 0)
        {
            ShowError("Quantity has to be at least one.");
            return;
        }

        // Adding the same product twice merges, rather than leaving two lines that have
        // to be reconciled by eye.
        var existing = _lines.FindIndex(l => l.ProductId == productId);
        if (existing >= 0)
        {
            var merged = _lines[existing];
            _lines[existing] = merged with { Qty = merged.Qty + qty, UnitCost = _cost.Value };
        }
        else
        {
            _lines.Add(new Line(productId, product.Sku, product.Name, qty, _cost.Value));
        }

        _qty.Value = 1m;
        _error.Visible = false;
        Refresh();
    }

    private void RemoveLine()
    {
        if (_grid.CurrentRow is { Index: >= 0 } row && row.Index < _lines.Count)
        {
            _lines.RemoveAt(row.Index);
            Refresh();
        }
    }

    private new void Refresh()
    {
        _grid.Rows.Clear();

        foreach (var line in _lines)
        {
            _grid.Rows.Add(
                line.Sku,
                line.Name,
                Format.Qty(line.Qty),
                Format.Amount(line.UnitCost),
                Format.Amount(line.LineTotal));
        }

        var total = _lines.Sum(l => l.LineTotal);
        _total.Text = $"Bill total   {Format.Money(total)}";
        _paidNow.Maximum = Math.Max(total, 0m);
        _save.Enabled = _lines.Count > 0;

        base.Refresh();
    }

    private async Task SubmitAsync()
    {
        if (_busy)
        {
            return;
        }

        if (_lines.Count == 0)
        {
            ShowError("Add at least one line.");
            return;
        }

        if (SelectedId(_shop) is not { } shopId)
        {
            ShowError("Choose a shop.");
            return;
        }

        if (SelectedId(_supplier) is not { } supplierId)
        {
            ShowError("Choose a supplier.");
            return;
        }

        SetBusy(true);

        try
        {
            await _purchases.CreateAsync(new CreatePurchaseRequest(
                shopId,
                supplierId,
                Format.ToUtc(_date.Value.Date),
                _paidNow.Value,
                Pickers.Selected(_method, PaymentMethod.Bank),
                _note.Text,
                _lines.Select(l => new PurchaseLineInput(l.ProductId, l.Qty, l.UnitCost)).ToList()))
                .ConfigureAwait(true);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ValidationFailedException validation)
        {
            ShowError(string.Join("  ", validation.Errors.SelectMany(e => e.Value)));
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

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _save.Enabled = !busy && _lines.Count > 0;
        _save.Text = busy ? "Saving…" : "Save bill";
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = true;
    }

    private static DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Palette.Surface,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Palette.BorderSubtle,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeight = Metrics.GridHeaderHeight,
            Font = Typography.GridBody,
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = Palette.SurfaceSunken;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.InkMuted;
        grid.ColumnHeadersDefaultCellStyle.Font = Typography.Get(Typography.Size2Xs, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(Metrics.Space3, 0, Metrics.Space3, 0);
        // A header is not selectable, so it must not pick up the system selection
        // colour when the grid takes focus — that showed as one header cell going blue.
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Palette.SurfaceSunken;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Palette.InkMuted;
        grid.DefaultCellStyle.BackColor = Palette.Surface;
        grid.DefaultCellStyle.ForeColor = Palette.InkBody;
        grid.DefaultCellStyle.SelectionBackColor = Palette.PrimarySoft;
        grid.DefaultCellStyle.SelectionForeColor = Palette.InkStrong;
        grid.DefaultCellStyle.Padding = new Padding(Metrics.Space3, 0, Metrics.Space3, 0);
        grid.RowTemplate.Height = Metrics.GridRowHeight;

        grid.Columns.Add(Column("sku", "SKU", 130, false));
        grid.Columns.Add(Fill("name", "Product"));
        grid.Columns.Add(Column("qty", "Qty", 80, true));
        grid.Columns.Add(Column("cost", Format.MoneyHeader("Unit cost"), 130, true));
        grid.Columns.Add(Column("total", Format.MoneyHeader("Line total"), 140, true));

        return grid;
    }

    private static DataGridViewTextBoxColumn Column(string name, string header, int width, bool numeric)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };

        if (numeric)
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Font = Typography.Numeric(Typography.SizeSm);
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }

        return column;
    }

    private static DataGridViewTextBoxColumn Fill(string name, string header) => new()
    {
        Name = name,
        HeaderText = header,
        AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private static Panel Card()
    {
        var card = new Panel { BackColor = Palette.Surface, Padding = new Padding(Metrics.Space4) };
        card.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Render.UseQualityRendering(g);
            var bounds = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
            Render.FillRounded(g, bounds, Metrics.RadiusLg, Palette.Surface);
            Render.DrawRoundedBorder(g, bounds, Metrics.RadiusLg, Palette.BorderSubtle);
        };
        return card;
    }

    private static void Place(Control parent, string label, Control input, int x, int width)
    {
        var caption = Theme.FieldLabel(label);
        caption.Location = new Point(x, Metrics.Space4);
        input.Location = new Point(x, Metrics.Space4 + 20);
        input.Width = width;
        parent.Controls.Add(caption);
        parent.Controls.Add(input);
    }

    private sealed record Item(long Id, string Name)
    {
        public override string ToString() => Name;
    }

    private static void Select(KryptonComboBox combo, long? id)
    {
        if (id is { } value)
        {
            foreach (var item in combo.Items.OfType<Item>())
            {
                if (item.Id == value)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static long? SelectedId(KryptonComboBox combo)
        => combo.SelectedItem is Item item ? item.Id : null;
}
