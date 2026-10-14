using System.Drawing;
using Krypton.Toolkit;
using TechShop.Application.Features.Catalog;
using TechShop.Application.Features.Parties;
using TechShop.Application.Features.Sales;
using TechShop.Domain.Enums;
using TechShop.WinForms.Controls;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms.Features.Pos;

/// <summary>
/// The till. Search or scan on the left, cart on the right, one button to take payment.
///
/// Built for the counter rather than for a desk: F2 puts the cursor in the search box,
/// F9 takes payment, Enter on a barcode adds the item straight to the cart, and the
/// running total is the largest thing on the screen. Nothing here needs the mouse.
/// </summary>
public sealed class PosPage : PageBase
{
    private readonly PageHeader _header;
    private readonly KryptonTextBox _search;
    private readonly FlowLayoutPanel _results;
    private readonly DataGridView _cart;
    private readonly KryptonComboBox _customer;
    private readonly KryptonNumericUpDown _discount;
    private readonly CartSummary _summary = new();

    /// <summary>Width of the discount box, which shares the top row with the customer.</summary>
    private const int DiscountWidth = 150;

    /// <summary>Where the money block starts, under that shared row.</summary>
    private const int SummaryTop = 76;
    private readonly AppButton _takePayment;
    private readonly AppButton _clear;
    private readonly Label _empty;
    private readonly System.Windows.Forms.Timer _searchDebounce;

    private readonly List<CartLine> _lines = [];
    private IReadOnlyList<CustomerDto> _customers = [];

    private sealed record CartLine(
        long ProductId,
        string Sku,
        string Name,
        decimal UnitPrice,
        int Qty,
        int Available,
        bool IsTaxable,
        decimal TaxRate,
        decimal TaxAmount)
    {
        public decimal LineTotal => Qty * UnitPrice;

        public decimal LineTotalWithTax => (Qty * UnitPrice) + TaxAmount;

        /// <summary>
        /// Tax at the product's own rate for the current quantity. Used to refill the
        /// line when the quantity changes, and as the starting figure when it is added.
        /// </summary>
        public decimal TaxAtRate => IsTaxable
            ? decimal.Round(Qty * UnitPrice * TaxRate / 100m, 2)
            : 0m;
    }

    public PosPage()
        : base(Module.Pos, "Point of sale")
    {
        _header = new PageHeader("Point of sale", "F2 search  ·  F9 take payment");

        // ---------------- left: search and results
        _search = Theme.TextBox("Scan a barcode, or search by name or SKU…");
        _search.Dock = DockStyle.Top;
        _search.Height = 44;
        _search.StateCommon.Content.Font = Typography.Get(Typography.SizeMd);

        _searchDebounce = new System.Windows.Forms.Timer { Interval = 220 };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await SearchAsync().ConfigureAwait(true);
        };

        _search.TextChanged += (_, _) =>
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        };

        // A scanner ends with Enter. If exactly one product matches, it goes in the cart
        // and the box clears, so the next scan can follow immediately.
        _search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _searchDebounce.Stop();
                await ScanAsync().ConfigureAwait(true);
            }
        };

        _results = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, Metrics.Space3, 0, 0),
        };

        // Re-flow when the pane changes width, so the shelf stays full as the window
        // is resized or the sidebar collapses.
        _results.Resize += (_, _) => SizeTiles();

        _empty = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Scan a barcode or start typing to find a product.",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Palette.InkMuted,
            Font = Typography.Get(Typography.SizeMd),
            BackColor = Color.Transparent,
        };

        var left = Card();
        left.Dock = DockStyle.Fill;
        left.Controls.Add(_results);
        left.Controls.Add(_empty);
        left.Controls.Add(_search);

        // ---------------- right: cart
        _cart = BuildCart();

        _customer = Theme.ComboBox();
        _discount = Theme.Numeric();
        _discount.ValueChanged += (_, _) => RefreshTotals();


        _takePayment = new AppButton
        {
            Variant = ButtonVariant.Success,
            Text = "Take payment   F9",
            Height = 56,
            Dock = DockStyle.Bottom,
        };
        _takePayment.Click += async (_, _) => await TakePaymentAsync().ConfigureAwait(true);

        _clear = new AppButton
        {
            Variant = ButtonVariant.Ghost,
            Text = "Clear cart",
            Height = Metrics.ControlHeightSmall,
        };
        _clear.FitToText(Metrics.Space3);
        _clear.Click += (_, _) => ClearCart();

        var right = BuildCartPanel();

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520f));
        split.Controls.Add(left, 0, 0);
        split.Controls.Add(right, 1, 0);

        Controls.Add(split);
        Controls.Add(_header);

        RefreshCart();
    }


    private Panel BuildCartPanel()
    {
        var card = Card();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(Metrics.Space4, 0, 0, 0);

        var footer = new Panel { Dock = DockStyle.Bottom, BackColor = Color.Transparent };

        // Customer and discount share a row. Stacked, they cost the cart seventy pixels
        // of rows on a laptop screen, which is where the list of what is being sold
        // matters most.
        var customerLabel = Theme.FieldLabel("Customer");
        customerLabel.Location = new Point(0, 4);
        _customer.Location = new Point(0, 24);

        var discountLabel = Theme.FieldLabel(Format.MoneyHeader("Discount"));
        _discount.Width = DiscountWidth;

        _summary.Location = new Point(0, SummaryTop);

        footer.Controls.AddRange([customerLabel, _customer, discountLabel, _discount, _summary]);

        // One place decides every width and position here, so a caption can no longer
        // drift away from the figure it belongs to.
        void LayOutFooter()
        {
            var width = footer.ClientSize.Width;
            if (width <= 0)
            {
                return;
            }

            _customer.Width = Math.Max(140, width - DiscountWidth - Metrics.Space4);

            discountLabel.Location = new Point(width - DiscountWidth, 4);
            _discount.Location = new Point(width - DiscountWidth, 24);

            _summary.Width = width;
            _summary.Location = new Point(0, SummaryTop);

            var wanted = SummaryTop + _summary.Height + Metrics.Space2;
            if (footer.Height != wanted)
            {
                footer.Height = wanted;
            }
        }

        footer.Resize += (_, _) => LayOutFooter();
        _summary.Resize += (_, _) => LayOutFooter();
        LayOutFooter();

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.Transparent };
        var cartHeading = new Label
        {
            Text = "Cart",
            AutoSize = true,
            Location = new Point(0, 8),
            Font = Typography.Get(Typography.SizeMd, FontStyle.Bold),
            ForeColor = Palette.InkStrong,
            BackColor = Color.Transparent,
        };
        toolbar.Controls.Add(cartHeading);
        toolbar.Controls.Add(_clear);
        toolbar.Resize += (_, _) => _clear.Location = new Point(toolbar.Width - _clear.Width, 2);

        card.Controls.Add(_cart);
        card.Controls.Add(toolbar);
        card.Controls.Add(footer);
        card.Controls.Add(_takePayment);

        return card;
    }

    private DataGridView BuildCart()
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
            RowHeadersVisible = false,
            EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeight = 34,
            Font = Typography.GridBody,
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = Palette.SurfaceSunken;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.InkMuted;
        grid.ColumnHeadersDefaultCellStyle.Font = Typography.Get(Typography.Size2Xs, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(Metrics.Space2, 0, Metrics.Space2, 0);
        // A header is not selectable, so it must not pick up the system selection
        // colour when the grid takes focus — that showed as one header cell going blue.
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Palette.SurfaceSunken;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Palette.InkMuted;
        grid.DefaultCellStyle.BackColor = Palette.Surface;
        grid.DefaultCellStyle.ForeColor = Palette.InkBody;
        grid.DefaultCellStyle.SelectionBackColor = Palette.PrimarySoft;
        grid.DefaultCellStyle.SelectionForeColor = Palette.InkStrong;
        grid.DefaultCellStyle.Padding = new Padding(Metrics.Space2, 0, Metrics.Space2, 0);
        grid.RowTemplate.Height = 38;

        var name = new DataGridViewTextBoxColumn
        {
            Name = "name",
            HeaderText = "Item",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };

        var qty = new DataGridViewTextBoxColumn
        {
            Name = "qty",
            HeaderText = "Qty",
            Width = 52,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        qty.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        qty.DefaultCellStyle.Font = Typography.Numeric(Typography.SizeSm);

        // Editable: the product supplies a default, but the operator has the final say
        // on a line — an exempt customer, a part-exempt item, a rounding the shop
        // applies by hand.
        var tax = new DataGridViewTextBoxColumn
        {
            Name = "tax",
            HeaderText = Format.MoneyHeader("Tax"),
            Width = 104,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        tax.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        tax.DefaultCellStyle.Font = Typography.Numeric(Typography.SizeSm);

        var total = new DataGridViewTextBoxColumn
        {
            Name = "total",
            HeaderText = Format.MoneyHeader("Total"),
            Width = 118,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        total.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        total.DefaultCellStyle.Font = Typography.Numeric(Typography.SizeSm);

        var remove = new DataGridViewButtonColumn
        {
            Name = "remove",
            HeaderText = string.Empty,
            Text = "✕",
            UseColumnTextForButtonValue = true,
            Width = 34,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };

        grid.Columns.AddRange([name, qty, tax, total, remove]);

        grid.CellContentClick += (_, e) =>
        {
            if (e.ColumnIndex == 4 && e.RowIndex >= 0 && e.RowIndex < _lines.Count)
            {
                _lines.RemoveAt(e.RowIndex);
                RefreshCart();
            }
        };

        // The quantity cell is editable in place, which is how a cashier corrects "two,
        // not one" without removing the line and scanning again.
        grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _lines.Count)
            {
                return;
            }

            var line = _lines[e.RowIndex];
            var raw = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();

            switch (e.ColumnIndex)
            {
                case 1:
                {
                    if (!int.TryParse(raw, out var wanted) || wanted <= 0)
                    {
                        RefreshCart();
                        return;
                    }

                    if (wanted > line.Available)
                    {
                        Toast.Show(FindForm(), $"Only {line.Available} of {line.Sku} in stock", ToastTone.Warning);
                        wanted = line.Available;
                    }

                    // The tax follows the quantity unless the operator has already
                    // overridden it, in which case their figure stands.
                    var changed = line with { Qty = wanted };
                    _lines[e.RowIndex] = line.TaxAmount == line.TaxAtRate
                        ? changed with { TaxAmount = changed.TaxAtRate }
                        : changed;

                    break;
                }

                case 2:
                {
                    if (!decimal.TryParse(
                            raw,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.CurrentCulture,
                            out var amount)
                        || amount < 0m)
                    {
                        Toast.Show(FindForm(), "Tax has to be zero or more", ToastTone.Warning);
                        RefreshCart();
                        return;
                    }

                    _lines[e.RowIndex] = line with { TaxAmount = decimal.Round(amount, 2) };
                    break;
                }

                default:
                    return;
            }

            RefreshCart();
        };

        return grid;
    }

    protected override async Task LoadAsync()
    {
        if (ActiveShopId is null)
        {
            // A sale belongs to exactly one till. "All shops" is a reporting view and
            // cannot be sold from, so the screen says so rather than guessing.
            _header.Subtitle = "Choose a single shop in the top bar to start selling.";
            _takePayment.Enabled = false;
            _search.Enabled = false;
            _empty.Text = "Point of sale needs one shop selected. Pick one in the top bar.";
            _empty.Visible = true;
            return;
        }

        _takePayment.Enabled = true;
        _search.Enabled = true;
        _header.Subtitle = "F2 search  ·  F9 take payment";

        _customers = await Service<CustomerService>().LookupAsync().ConfigureAwait(true);

        _customer.Items.Clear();
        foreach (var customer in _customers)
        {
            _customer.Items.Add(new CustomerItem(customer.Id, customer.Name, customer.IsWalkIn));
        }

        // Defaults to walk-in, which is what most sales are.
        var walkIn = _customer.Items.OfType<CustomerItem>().FirstOrDefault(c => c.IsWalkIn);
        _customer.SelectedItem = walkIn ?? _customer.Items.OfType<CustomerItem>().FirstOrDefault();

        await SearchAsync().ConfigureAwait(true);
        RefreshCart();
    }

    private async Task SearchAsync()
    {
        if (ActiveShopId is not { } shopId)
        {
            return;
        }

        try
        {
            var found = await Service<ProductService>()
                .SearchForPosAsync(shopId, _search.Text, 40)
                .ConfigureAwait(true);

            ShowResults(found);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void ShowResults(List<PosProductDto> products)
    {
        _results.SuspendLayout();

        foreach (Control existing in _results.Controls)
        {
            existing.Dispose();
        }

        _results.Controls.Clear();

        foreach (var product in products)
        {
            _results.Controls.Add(new ProductTile(product, p => AddToCart(p)));
        }

        SizeTiles();
        _results.ResumeLayout();

        _empty.Visible = products.Count == 0;
        _empty.Text = string.IsNullOrWhiteSpace(_search.Text)
            ? "Scan a barcode or start typing to find a product."
            : $"Nothing matches “{_search.Text}”.";
        _results.Visible = products.Count > 0;
    }

    /// <summary>
    /// Stretches the tiles so a row divides the shelf area exactly.
    ///
    /// With a fixed tile width the flow panel fitted as many as it could and left the
    /// remainder empty — on a narrow pane that showed as two columns of tiles and a
    /// wide blank strip beside them. Widening the tiles to share the leftover closes it.
    /// </summary>
    private void SizeTiles()
    {
        var tiles = _results.Controls.OfType<ProductTile>().ToList();
        if (tiles.Count == 0)
        {
            return;
        }

        var gap = Metrics.Space3;
        var available = _results.ClientSize.Width - _results.Padding.Horizontal;

        if (available <= 0)
        {
            return;
        }

        // Every tile carries its right margin, including the last one on a row, so a
        // tile's footprint is width + gap and the row budget is columns * footprint.
        // Counting the trailing gap only between tiles made the arithmetic one gap too
        // generous, which pushed the last tile of each row onto the next line and left
        // its space empty — the blank strip beside the grid.
        var columns = Math.Max(1, available / (ProductTile.MinWidth + gap));
        columns = Math.Min(columns, tiles.Count);

        var width = Math.Min((available / columns) - gap, ProductTile.MaxWidth);

        foreach (var tile in tiles)
        {
            if (tile.Width != width)
            {
                tile.Width = width;
            }
        }
    }

    private async Task ScanAsync()
    {
        if (ActiveShopId is not { } shopId || string.IsNullOrWhiteSpace(_search.Text))
        {
            return;
        }

        try
        {
            var found = await Service<ProductService>()
                .SearchForPosAsync(shopId, _search.Text, 40)
                .ConfigureAwait(true);

            if (found.Count == 1)
            {
                AddToCart(found[0]);
                _search.Text = string.Empty;
                _search.Focus();
                return;
            }

            ShowResults(found);

            if (found.Count == 0)
            {
                Toast.Show(FindForm(), $"Nothing matches “{_search.Text}”", ToastTone.Warning);
            }
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void AddToCart(PosProductDto product)
    {
        if (product.Qty <= 0)
        {
            Toast.Show(FindForm(), $"{product.Sku} is out of stock", ToastTone.Warning);
            return;
        }

        var index = _lines.FindIndex(l => l.ProductId == product.Id);

        if (index >= 0)
        {
            var line = _lines[index];

            if (line.Qty >= product.Qty)
            {
                Toast.Show(FindForm(), $"Only {product.Qty} of {product.Sku} in stock", ToastTone.Warning);
                return;
            }

            // The tax follows the quantity, unless the operator has already overridden
            // it — in which case their figure is left alone.
            var grown = line with { Qty = line.Qty + 1 };
            _lines[index] = line.TaxAmount == line.TaxAtRate
                ? grown with { TaxAmount = grown.TaxAtRate }
                : grown;
        }
        else
        {
            var line = new CartLine(
                product.Id,
                product.Sku,
                product.Name,
                product.SalePrice,
                1,
                product.Qty,
                product.IsTaxable,
                product.TaxRate,
                0m);

            _lines.Add(line with { TaxAmount = line.TaxAtRate });
        }

        RefreshCart();
    }

    private void ClearCart()
    {
        if (_lines.Count == 0)
        {
            return;
        }

        if (!Dialogs.Confirm(FindForm(), "Clear cart", $"Remove all {_lines.Count} lines from the cart?"))
        {
            return;
        }

        _lines.Clear();
        _discount.Value = 0m;
        RefreshCart();
    }

    private void RefreshCart()
    {
        _cart.Rows.Clear();

        foreach (var line in _lines)
        {
            var row = _cart.Rows.Add(
                line.Name,
                line.Qty.ToString(System.Globalization.CultureInfo.CurrentCulture),

                // An exempt line shows a dash rather than 0.00, so "no tax applies" is
                // visibly different from "tax worked out to nothing".
                line.IsTaxable ? Format.Amount(line.TaxAmount) : "—",
                Format.Amount(line.LineTotalWithTax),
                "✕");

            // The name is ellipsised in a narrow cart, so the full identity — including
            // the SKU, which is no longer a column — stays reachable on hover.
            _cart.Rows[row].Cells[0].ToolTipText = $"{line.Sku}  {line.Name}";

            _cart.Rows[row].Cells[2].ToolTipText = line.IsTaxable
                ? $"Tax at {line.TaxRate:0.##}% is {Format.Money(line.TaxAtRate)}. Type to override."
                : "This product is exempt from sales tax.";
        }

        RefreshTotals();
    }

    private void RefreshTotals()
    {
        var subTotal = _lines.Sum(l => l.LineTotal);

        // A discount larger than the sale would make the invoice negative, so the box is
        // capped at the subtotal rather than validated after the fact.
        _discount.Maximum = Math.Max(subTotal, 0m);
        var discount = Math.Min(_discount.Value, subTotal);

        // Tax is charged per line and added after the discount; it is never discounted,
        // because the shop still owes the full amount to the tax authority.
        var tax = _lines.Sum(l => l.TaxAmount);
        var total = subTotal - discount + tax;

        _summary.Show(subTotal, discount, tax, _lines.Any(l => l.IsTaxable));

        _takePayment.Enabled = _lines.Count > 0 && ActiveShopId is not null;
    }

    private async Task TakePaymentAsync()
    {
        if (_lines.Count == 0 || ActiveShopId is not { } shopId)
        {
            return;
        }

        if (_customer.SelectedItem is not CustomerItem customer)
        {
            Dialogs.Warn(FindForm(), "No customer", "Choose a customer, or the walk-in account.");
            return;
        }

        var subTotal = _lines.Sum(l => l.LineTotal);
        var discount = Math.Min(_discount.Value, subTotal);
        var tax = _lines.Sum(l => l.TaxAmount);
        var total = subTotal - discount + tax;

        using var payment = new PaymentDialog(total, customer.IsWalkIn);

        if (payment.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var request = new CreateSaleRequest(
            shopId,
            customer.Id,
            discount,
            payment.PaidNow,
            payment.Method,
            payment.Note,
            _lines.Select(l => new SaleLineInput(l.ProductId, l.Qty, l.UnitPrice, l.TaxAmount)).ToList());

        InvoiceDetailDto? invoice = null;

        var ok = await TryAsync(async () =>
        {
            invoice = await Service<SalesService>().CreateSaleAsync(request).ConfigureAwait(true);
        }).ConfigureAwait(true);

        if (!ok || invoice is null)
        {
            return;
        }

        _lines.Clear();
        _discount.Value = 0m;
        RefreshCart();

        if (payment.Change > 0)
        {
            Dialogs.Info(
                FindForm(),
                "Change due",
                $"{invoice.InvoiceNo} saved.\n\nChange to give: {Format.Money(payment.Change)}");
        }
        else
        {
            Toast.Show(FindForm(), $"{invoice.InvoiceNo} saved");
        }

        if (Dialogs.Confirm(FindForm(), "Print receipt", $"Print {invoice.InvoiceNo}?"))
        {
            // The counter prints a till roll. The office can switch to A4 in the
            // dialog, but the default here is the paper actually in this printer.
            using var receipt = new Sales.InvoiceDetailDialog(invoice, Sales.InvoiceFormat.Receipt80);
            receipt.ShowDialog(FindForm());
        }

        // Stock has moved, so what is on screen is now out of date.
        await SearchAsync().ConfigureAwait(true);
        _search.Focus();
    }

    /// <summary>
    /// Till shortcuts. The counter is driven from the keyboard, so these are handled
    /// before the focused control sees them.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F2:
                _search.Focus();
                _search.SelectAll();
                return true;

            case Keys.F9 when _takePayment.Enabled:
                _ = TakePaymentAsync();
                return true;

            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchDebounce.Stop();
            _searchDebounce.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed record CustomerItem(long Id, string Name, bool IsWalkIn)
    {
        public override string ToString() => Name;
    }

    // ---------------------------------------------------------------- test seams

    /// <summary>
    /// The QA harness drives the till through these. The payment dialog is modal, so the
    /// harness supplies the figures it would have returned and the rest of the path —
    /// cart, discount, customer, request building, the service call — is the real one.
    /// </summary>
    internal int CartLineCount => _lines.Count;

    internal decimal CartTotal
    {
        get
        {
            var subTotal = _lines.Sum(l => l.LineTotal);
            return subTotal - Math.Min(_discount.Value, subTotal) + _lines.Sum(l => l.TaxAmount);
        }
    }

    /// <summary>Tax currently in the cart, so the QA harness can check it per line.</summary>
    internal decimal CartTax => _lines.Sum(l => l.TaxAmount);

    internal bool CartHasTaxableLine => _lines.Any(l => l.IsTaxable);

    internal bool CartHasExemptLine => _lines.Any(l => !l.IsTaxable);

    /// <summary>
    /// True when a row of tiles uses the shelf width with less than one tile of slack
    /// left over. This is what the "two columns and a blank strip" report was.
    /// </summary>
    /// <summary>
    /// Fills the cart with one taxable line and one exempt line, so the screenshot pass
    /// and the QA checks see the mixed basket the tax feature exists for rather than an
    /// empty cart.
    /// </summary>
    internal int LoadMixedBasketForTest()
    {
        var added = 0;

        foreach (var tile in _results.Controls.OfType<ProductTile>())
        {
            var wantTaxable = added == 0;

            if (tile.IsTaxableForTest == wantTaxable && tile.InStockForTest)
            {
                tile.PickForTest();

                if (_lines.Count > added)
                {
                    added++;
                }

                if (added == 2)
                {
                    break;
                }
            }
        }

        return added;
    }

    /// <summary>Overrides a line's tax the way typing into the Tax cell does.</summary>
    internal void SetLineTaxForTest(int index, decimal amount)
    {
        _lines[index] = _lines[index] with { TaxAmount = amount };
        RefreshCart();
    }

    /// <summary>Changes a line's quantity the way editing the Qty cell does.</summary>
    internal void SetLineQtyForTest(int index, int qty)
    {
        var line = _lines[index];
        var changed = line with { Qty = qty };

        _lines[index] = line.TaxAmount == line.TaxAtRate
            ? changed with { TaxAmount = changed.TaxAtRate }
            : changed;

        RefreshCart();
    }

    internal decimal LineTaxForTest(int index) => _lines[index].TaxAmount;

    internal decimal LineTaxAtRateForTest(int index) => _lines[index].TaxAtRate;

    internal void ClearCartForTest()
    {
        _lines.Clear();
        _discount.Value = 0m;
        RefreshCart();
    }

    internal bool TilesFillWidthForTest()
    {
        var tiles = _results.Controls.OfType<ProductTile>().ToList();
        if (tiles.Count == 0)
        {
            return true;
        }

        var available = _results.ClientSize.Width - _results.Padding.Horizontal;
        var firstRow = tiles.Where(t => t.Top == tiles[0].Top).ToList();

        // A search matching fewer products than fit on a row legitimately leaves space.
        if (firstRow.Count < tiles.Count && firstRow.Count == 0)
        {
            return true;
        }

        var rowRight = firstRow.Max(t => t.Right);
        var slack = available - rowRight;

        // Tolerated only when the tiles are already at their maximum width, which is
        // what happens when a single result cannot sensibly be stretched further.
        return slack < ProductTile.MinWidth || firstRow.TrueForAll(t => t.Width >= ProductTile.MaxWidth);
    }

    internal async Task<bool> SearchForTestAsync(string term)
    {
        _search.Text = term;
        await SearchAsync().ConfigureAwait(true);
        return _results.Controls.Count > 0;
    }

    internal bool AddFirstResultToCartForTest()
    {
        if (_results.Controls.OfType<ProductTile>().FirstOrDefault() is not { } tile)
        {
            return false;
        }

        var before = _lines.Count;
        tile.PickForTest();
        return _lines.Count > before || _lines.Any(l => l.Qty > 1);
    }

    internal void SetDiscountForTest(decimal amount)
    {
        _discount.Value = amount;
        RefreshTotals();
    }

    internal async Task<InvoiceDetailDto?> CompleteSaleForTest(decimal paidNow, PaymentMethod method)
    {
        if (ActiveShopId is not { } shopId || _customer.SelectedItem is not CustomerItem customer)
        {
            return null;
        }

        var subTotal = _lines.Sum(l => l.LineTotal);
        var discount = Math.Min(_discount.Value, subTotal);

        var request = new CreateSaleRequest(
            shopId,
            customer.Id,
            discount,
            paidNow,
            method,
            "QA till sale",
            _lines.Select(l => new SaleLineInput(l.ProductId, l.Qty, l.UnitPrice, l.TaxAmount)).ToList());

        var invoice = await Service<SalesService>().CreateSaleAsync(request).ConfigureAwait(true);

        _lines.Clear();
        _discount.Value = 0m;
        RefreshCart();

        return invoice;
    }
}
