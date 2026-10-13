using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Features.Settings;
using TechShop.Domain.Entities;
using TechShop.Domain.Enums;

namespace TechShop.Infrastructure.Persistence;

/// <summary>
/// Brings the database up to date and, on a fresh file, fills it with six months of
/// trading so every screen has something real on it: two shops, six accounts, a sixty
/// line catalogue, twenty customers, eight suppliers, opening stock, purchases, sales,
/// receipts, transfers, adjustments, voids, expenses, petty cash and an audit trail.
///
/// Both halves are guarded separately. Structure is topped up whenever a piece is
/// missing, so an existing database gains a new expense category without being
/// re-seeded. History is written only when there are no invoices at all, because a
/// half-seeded ledger is worse than an empty one.
///
/// Stock and the cash drawer are tracked in memory while the history is generated, and
/// the context is saved in batches. Reading the stock level back per line and saving per
/// invoice is what made the first version take forty seconds for two hundred invoices.
/// </summary>
public sealed class DatabaseSeeder(AppDbContext db, IPasswordHasher hasher)
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "admin123";

    /// <summary>How many days of history to generate.</summary>
    private const int HistoryDays = 183;

    /// <summary>Invoices held in the change tracker before a save.</summary>
    private const int BatchSize = 120;

    /// <summary>What each till keeps in the drawer after the week's takings are banked.</summary>
    private const decimal WorkingFloat = 120000m;

    /// <summary>
    /// Fixed seed, so the demo database is identical on every machine and a figure in a
    /// bug report is something the next person can reproduce.
    /// </summary>
    private readonly Random _rng = new(20260411);

    /// <summary>Live stock, keyed by shop and product, mutated as history is generated.</summary>
    private readonly Dictionary<(long ShopId, long ProductId), StockLevel> _levels = [];

    /// <summary>Running cash in each drawer, so banking knows what there is to bank.</summary>
    private readonly Dictionary<long, decimal> _drawer = [];

    private static readonly StandardAccount[] StandardAccounts =
    [
        new("sara", "Sara Khan", [0],
            [Module.Pos, Module.Invoices, Module.Customers],
            [Module.Dashboard, Module.Products, Module.Stock]),

        new("bilal", "Bilal Ahmed", [1],
            [Module.Pos, Module.Invoices, Module.Customers, Module.Expenses, Module.PettyCash],
            [Module.Dashboard, Module.Products, Module.Stock, Module.Reports]),

        new("hina", "Hina Yousaf", [0, 1],
            [Module.Products, Module.Stock, Module.Purchases, Module.Suppliers],
            [Module.Dashboard, Module.Invoices, Module.Reports]),

        new("omar", "Omar Farooq", [0, 1],
            [Module.Expenses, Module.PettyCash],
            [Module.Dashboard, Module.Reports, Module.Invoices]),

        new("zoya", "Zoya Malik", [0],
            [],
            [Module.Dashboard, Module.Invoices, Module.Products, Module.Stock, Module.Customers, Module.Reports]),
    ];

    private static readonly (Module Module, AuditAction Action, string Entity)[] Activities =
    [
        (Module.Products, AuditAction.Create, nameof(Product)),
        (Module.Products, AuditAction.Update, nameof(Product)),
        (Module.Stock, AuditAction.Update, nameof(StockLevel)),
        (Module.Invoices, AuditAction.Create, nameof(SalesInvoice)),
        (Module.Invoices, AuditAction.Void, nameof(SalesInvoice)),
        (Module.Customers, AuditAction.Create, nameof(Customer)),
        (Module.Customers, AuditAction.Update, nameof(Customer)),
        (Module.Purchases, AuditAction.Create, nameof(PurchaseBill)),
        (Module.Expenses, AuditAction.Create, nameof(Expense)),
        (Module.Reports, AuditAction.Export, "Report"),
        (Module.Suppliers, AuditAction.Update, nameof(Supplier)),
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct).ConfigureAwait(false);

        // WAL lets the reporting screens read while the till is writing.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);

        var shops = await EnsureShopsAsync(ct).ConfigureAwait(false);
        var users = await EnsureUsersAsync(shops, ct).ConfigureAwait(false);
        var reference = await EnsureReferenceDataAsync(ct).ConfigureAwait(false);

        if (await db.SalesInvoices.AnyAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        await SeedOpeningStockAsync(shops, reference.Products, ct).ConfigureAwait(false);
        await SeedTradingHistoryAsync(shops, reference, ct).ConfigureAwait(false);
        await SeedTransfersAndAdjustmentsAsync(shops, reference.Products, ct).ConfigureAwait(false);
        await SeedOnAccountReceiptsAsync(shops, reference.Customers, ct).ConfigureAwait(false);
        await SeedAuditTrailAsync(shops, users, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ structure

    private async Task<List<Shop>> EnsureShopsAsync(CancellationToken ct)
    {
        var existing = await db.Shops.OrderBy(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return existing;
        }

        var created = new List<Shop>
        {
            New(new Shop { Code = "MAIN", Name = "TechShop Main Street", Address = "12 Main Street, Lahore", Phone = "0300 1110001" }),
            New(new Shop { Code = "MALL", Name = "TechShop City Mall", Address = "Shop 48, City Mall, Lahore", Phone = "0300 1110002" }),
        };

        db.Shops.AddRange(created);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created;
    }

    private async Task<List<AppUser>> EnsureUsersAsync(List<Shop> shops, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Username == AdminUsername, ct).ConfigureAwait(false))
        {
            // An admin is granted every module and every shop implicitly by the
            // permission service, so it stores no permission or shop rows.
            db.Users.Add(New(new AppUser
            {
                Username = AdminUsername,
                FullName = "Shop Owner",
                Role = UserRole.Admin,
                PasswordHash = hasher.Hash(AdminPassword),
            }));
        }

        foreach (var account in StandardAccounts)
        {
            if (await db.Users.AnyAsync(u => u.Username == account.Username, ct).ConfigureAwait(false))
            {
                continue;
            }

            var user = New(new AppUser
            {
                Username = account.Username,
                FullName = account.FullName,
                Role = UserRole.Standard,

                // Each standard account's password is its username. Fine for a local
                // evaluation database; the README says to change them before real use.
                PasswordHash = hasher.Hash(account.Username),
            });

            foreach (var index in account.Shops.Where(i => i < shops.Count))
            {
                user.UserShops.Add(new UserShop { ShopId = shops[index].Id });
            }

            foreach (var module in account.Write)
            {
                user.Permissions.Add(new UserPermission { Module = module, CanRead = true, CanWrite = true });
            }

            // Write implies read, so a module in both lists is stored once.
            foreach (var module in account.Read.Except(account.Write))
            {
                user.Permissions.Add(new UserPermission { Module = module, CanRead = true, CanWrite = false });
            }

            db.Users.Add(user);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await db.Users.OrderBy(u => u.Id).ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<ReferenceData> EnsureReferenceDataAsync(CancellationToken ct)
    {
        if (!await db.AppSettings.AnyAsync(s => s.Key == SettingsService.CurrencyKey, ct).ConfigureAwait(false))
        {
            db.AppSettings.Add(New(new AppSetting
            {
                Key = SettingsService.CurrencyKey,
                Value = SettingsService.DefaultCurrency,
            }));
        }

        var categories = await db.Categories.OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var name in SeedCatalogue.Categories)
        {
            if (categories.TrueForAll(c => c.Name != name))
            {
                var category = New(new Category { Name = name });
                db.Categories.Add(category);
                categories.Add(category);
            }
        }

        var expenseCategories = await db.ExpenseCategories.OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var name in SeedCatalogue.ExpenseCategories)
        {
            if (expenseCategories.TrueForAll(c => c.Name != name))
            {
                var category = New(new ExpenseCategory { Name = name });
                db.ExpenseCategories.Add(category);
                expenseCategories.Add(category);
            }
        }

        // Saved here so the category ids exist before products reference them.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var byName = categories.ToDictionary(c => c.Name, c => c.Id, StringComparer.Ordinal);

        var products = await db.Products.OrderBy(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        if (products.Count == 0)
        {
            var barcode = 8964000000001L;

            products = SeedCatalogue.Products
                .Select(row => New(new Product
                {
                    Sku = row.Sku,

                    // A scannable 13-digit code, so the till's barcode box can be tried
                    // without anyone printing labels first.
                    Barcode = (barcode++).ToString(CultureInfo.InvariantCulture),
                    Name = row.Name,
                    CategoryId = byName[row.Category],
                    Brand = row.Brand,
                    CostPrice = row.Cost,
                    SalePrice = row.Price,
                    ReorderLevel = row.Reorder,

                    // A mixed catalogue, so an invoice can legitimately carry tax on
                    // one line and none on the next.
                    IsTaxable = !SeedCatalogue.TaxExemptCategories.Contains(row.Category),
                    TaxRate = SeedCatalogue.TaxExemptCategories.Contains(row.Category)
                        ? 0m
                        : SeedCatalogue.StandardTaxRate,
                }))
                .ToList();

            db.Products.AddRange(products);
        }

        var suppliers = await db.Suppliers.OrderBy(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        if (suppliers.Count == 0)
        {
            suppliers = SeedCatalogue.Suppliers
                .Select(s => New(new Supplier { Name = s.Name, Phone = s.Phone, Address = s.Address }))
                .ToList();
            db.Suppliers.AddRange(suppliers);
        }

        var customers = await db.Customers.OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        if (customers.Count == 0)
        {
            customers = [New(new Customer { Name = "Walk-in customer", IsWalkIn = true })];
            customers.AddRange(SeedCatalogue.Customers
                .Select(c => New(new Customer { Name = c.Name, Phone = c.Phone, Address = c.Address })));
            db.Customers.AddRange(customers);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new ReferenceData(products, suppliers, customers, expenseCategories);
    }

    // ------------------------------------------------------------------ history

    private async Task SeedOpeningStockAsync(List<Shop> shops, List<Product> products, CancellationToken ct)
    {
        var opened = Today.AddDays(-(HistoryDays + 2));
        var bands = SeedCatalogue.Products.ToDictionary(p => p.Sku, p => p, StringComparer.Ordinal);

        foreach (var shop in shops)
        {
            foreach (var product in products)
            {
                // Opening quantity is banded by what the product is: a gaming rig and a
                // box of HDMI cables do not sit on the shelf in the same numbers.
                var (low, high) = bands.TryGetValue(product.Sku, out var band)
                    ? (band.OpeningLow, band.OpeningHigh)
                    : (8, 24);

                var qty = _rng.Next(low, high + 1);

                var level = new StockLevel { ShopId = shop.Id, ProductId = product.Id, Qty = qty };
                db.StockLevels.Add(level);
                _levels[(shop.Id, product.Id)] = level;

                db.StockMovements.Add(New(new StockMovement
                {
                    ShopId = shop.Id,
                    ProductId = product.Id,
                    Type = StockMovementType.Opening,
                    Qty = qty,
                    UnitCost = product.CostPrice,
                    MovedAt = opened,
                    Reference = "Opening stock",
                }));
            }

            // Each drawer starts with a float, so cash in hand is never zero.
            EmitCash(shop.Id, opened, PettyCashDirection.In, 250000m, "Opening cash float", systemGenerated: false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Six months of trading, generated one day at a time.
    ///
    /// Chronological order is what makes the figures hold up. Generating all the
    /// purchases and then all the sales left the expensive lines at zero by month three,
    /// which silently skipped sales and left half the catalogue below its reorder level.
    /// Here each shop reorders what has run low, sells, pays its bills and banks the
    /// takings in the order a real week happens, so stock, the drawer and the ledgers all
    /// stay plausible.
    /// </summary>
    private async Task SeedTradingHistoryAsync(List<Shop> shops, ReferenceData reference, CancellationToken ct)
    {
        var products = reference.Products;
        var expenses = BuildExpenseSchedule(shops, reference.ExpenseCategories);
        var tills = StandardAccounts.Select(a => a.Username).ToList();

        var reorderTargets = SeedCatalogue.Products
            .ToDictionary(p => p.Sku, p => p.OpeningHigh, StringComparer.Ordinal);

        // Each shop restocks and banks on its own weekday, so the two branches do not
        // move in lockstep.
        var restockDay = shops.Select((shop, i) => (shop.Id, Day: i == 0 ? 1 : 3))
            .ToDictionary(x => x.Id, x => x.Day);
        var bankDay = shops.Select((shop, i) => (shop.Id, Day: i == 0 ? 4 : 5))
            .ToDictionary(x => x.Id, x => x.Day);

        var billSequence = 1;
        var invoiceSequence = 1;
        var pending = 0;
        var nextExpense = 0;

        for (var daysAgo = HistoryDays; daysAgo >= 0; daysAgo--)
        {
            var day = Today.AddDays(-daysAgo);

            // The schedule is in date order, so this walks it once rather than filtering
            // the whole list on every one of the 184 days.
            while (nextExpense < expenses.Count && expenses[nextExpense].Date.Date <= day.Date)
            {
                EmitExpense(expenses[nextExpense]);
                nextExpense++;
            }

            foreach (var shop in shops)
            {
                if ((int)day.DayOfWeek == restockDay[shop.Id])
                {
                    EmitRestock(shop, day, reference.Suppliers, products, reorderTargets, ref billSequence);
                }
            }

            // Sundays are quiet, weekends busier, and the last month busier still. Flat
            // traffic makes every chart a straight line.
            var baseline = day.DayOfWeek switch
            {
                DayOfWeek.Sunday => 1,
                DayOfWeek.Saturday => 6,
                DayOfWeek.Friday => 5,
                _ => 4,
            };

            var salesToday = daysAgo <= 30 ? baseline + _rng.Next(1, 4) : baseline + _rng.Next(-1, 2);

            for (var n = 0; n < Math.Max(salesToday, 0); n++)
            {
                if (EmitSale(shops, reference.Customers, products, day, tills, ref invoiceSequence))
                {
                    pending++;
                }
            }

            foreach (var shop in shops)
            {
                if ((int)day.DayOfWeek == bankDay[shop.Id])
                {
                    EmitBanking(shop, day);
                }
            }

            if (pending >= BatchSize)
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                pending = 0;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A restocking run: everything at or below its reorder level, brought back up to the
    /// quantity the shop likes to hold. Capped at ten lines so a bill looks like a bill;
    /// anything still short waits for next week, which is also what happens in practice.
    /// </summary>
    private void EmitRestock(
        Shop shop,
        DateTime day,
        List<Supplier> suppliers,
        List<Product> products,
        Dictionary<string, int> targets,
        ref int sequence)
    {
        var running = products
            .Select(p => (Product: p, OnHand: OnHand(shop.Id, p.Id)))
            .Where(x => x.OnHand <= x.Product.ReorderLevel)
            .OrderBy(x => x.OnHand - x.Product.ReorderLevel)
            .Take(10)
            .ToList();

        if (running.Count == 0)
        {
            return;
        }

        var billDate = day.AddHours(_rng.Next(10, 17));
        var supplier = suppliers[_rng.Next(suppliers.Count)];

        var bill = New(new PurchaseBill
        {
            BillNo = $"{shop.Code}-P{billDate:yyyyMM}-{sequence++:D4}",
            ShopId = shop.Id,
            SupplierId = supplier.Id,
            BillDate = billDate,
            PaymentMethod = PaymentMethod.Bank,
        });

        var total = 0m;

        foreach (var (product, onHand) in running)
        {
            var target = targets.TryGetValue(product.Sku, out var t) ? t : 24;
            var qty = Math.Max(target - onHand, Math.Max(product.ReorderLevel, 2));

            bill.Items.Add(new PurchaseItem
            {
                ProductId = product.Id,
                Qty = qty,
                UnitCost = product.CostPrice,
            });

            total += qty * product.CostPrice;

            AddStock(
                shop.Id, product.Id, qty, StockMovementType.Purchase,
                product.CostPrice, bill.BillNo, billDate);
        }

        // Most bills are settled on the spot; the rest leave a balance on the supplier
        // account so payables ageing has something in every bucket.
        var paid = _rng.Next(0, 10) switch
        {
            < 5 => total,
            < 8 => decimal.Round(total * 0.5m, 2),
            8 => decimal.Round(total * 0.25m, 2),
            _ => 0m,
        };

        bill.Total = total;
        bill.PaidAmount = paid;
        db.PurchaseBills.Add(bill);

        if (paid > 0)
        {
            db.SupplierPayments.Add(New(new SupplierPayment
            {
                ShopId = shop.Id,
                SupplierId = supplier.Id,

                // Set through the navigation so EF resolves the foreign key on save; the
                // bill has no id yet.
                PurchaseBill = bill,
                PaidAt = billDate,
                Amount = paid,
                Method = PaymentMethod.Bank,
                Note = $"Paid with bill {bill.BillNo}",
            }));
        }
    }

    /// <summary>Rings up one sale. Returns false when nothing on the list was in stock.</summary>
    private bool EmitSale(
        List<Shop> shops,
        List<Customer> customers,
        List<Product> products,
        DateTime day,
        List<string> tills,
        ref int sequence)
    {
        var shop = shops[_rng.Next(shops.Count)];
        var customer = _rng.Next(0, 10) < 6 ? customers[0] : customers[_rng.Next(1, customers.Count)];
        var at = day.AddHours(_rng.Next(10, 21)).AddMinutes(_rng.Next(0, 60));

        var candidates = Enumerable.Range(0, _rng.Next(1, 5))
            .Select(_ => products[_rng.Next(products.Count)])
            .DistinctBy(p => p.Id)
            .ToList();

        // Only sell what is on the shelf, so stock never goes negative and the stock
        // screen agrees with the movement history.
        var lines = new List<SalesItem>();
        foreach (var product in candidates)
        {
            var qty = _rng.Next(1, 4);
            if (OnHand(shop.Id, product.Id) < qty)
            {
                continue;
            }

            lines.Add(new SalesItem
            {
                ProductId = product.Id,
                Qty = qty,
                UnitPrice = product.SalePrice,
                UnitCost = product.CostPrice,

                // Tax follows the product, so a basket mixing taxable and exempt lines
                // is the normal case in the demo data rather than something contrived.
                TaxAmount = product.IsTaxable
                    ? decimal.Round(qty * product.SalePrice * product.TaxRate / 100m, 2)
                    : 0m,
            });
        }

        if (lines.Count == 0)
        {
            return false;
        }

        var subTotal = lines.Sum(l => l.Qty * l.UnitPrice);
        var discount = _rng.Next(0, 10) < 3 ? decimal.Round(subTotal * 0.03m, 2) : 0m;
        var taxTotal = lines.Sum(l => l.TaxAmount);
        var total = subTotal - discount + taxTotal;

        var method = customer.IsWalkIn
            ? (_rng.Next(0, 10) < 6 ? PaymentMethod.Cash : PaymentMethod.Card)
            : (PaymentMethod)_rng.Next(1, 5);

        // Roughly one invoice in sixty is voided, and only ever one that was not paid, so
        // a void never has to unwind a receipt.
        var voided = !customer.IsWalkIn && _rng.Next(0, 60) == 0;

        var paid = voided
            ? 0m
            : customer.IsWalkIn
                ? total
                : method == PaymentMethod.Credit
                    ? (_rng.Next(0, 10) < 5 ? 0m : decimal.Round(total * 0.4m, 2))
                    : total;

        var invoice = New(
            new SalesInvoice
            {
                InvoiceNo = $"{shop.Code}-{at:yyyyMM}-{sequence++:D4}",
                ShopId = shop.Id,
                CustomerId = customer.Id,
                InvoiceDate = at,
                SubTotal = subTotal,
                Discount = discount,
                TaxTotal = taxTotal,
                Total = total,
                PaidAmount = paid,
                PaymentMethod = method,
                Status = voided
                    ? InvoiceStatus.Void
                    : paid <= 0
                        ? InvoiceStatus.Unpaid
                        : paid >= total ? InvoiceStatus.Paid : InvoiceStatus.Partial,
            },
            tills.Count > 0 ? tills[_rng.Next(tills.Count)] : AdminUsername);

        if (voided)
        {
            invoice.VoidedAt = at.AddHours(_rng.Next(1, 48));
            invoice.VoidedBy = AdminUsername;
            invoice.VoidReason = "Customer cancelled the order";
        }

        foreach (var line in lines)
        {
            invoice.Items.Add(line);

            AddStock(
                shop.Id, line.ProductId, -line.Qty, StockMovementType.Sale,
                line.UnitCost, invoice.InvoiceNo, at);

            // A void puts the goods back, which is why a pair of movements is what the
            // stock history shows rather than a deleted row.
            if (voided)
            {
                AddStock(
                    shop.Id, line.ProductId, line.Qty, StockMovementType.SaleVoid,
                    line.UnitCost, invoice.InvoiceNo, invoice.VoidedAt!.Value);
            }
        }

        db.SalesInvoices.Add(invoice);

        if (paid > 0)
        {
            db.CustomerPayments.Add(New(new CustomerPayment
            {
                ShopId = shop.Id,
                CustomerId = customer.Id,
                SalesInvoice = invoice,
                ReceivedAt = at,
                Amount = paid,
                Method = method,
                Note = $"Received with invoice {invoice.InvoiceNo}",
            }));

            if (method == PaymentMethod.Cash)
            {
                EmitCash(shop.Id, at, PettyCashDirection.In, paid, $"Cash sale {invoice.InvoiceNo}", systemGenerated: true);
            }
        }

        return true;
    }

    /// <summary>
    /// Banks the week's takings, leaving a working float in the drawer. Without this the
    /// cash in hand figure climbs for six months and ends up absurd, and the drawer only
    /// ever moves one way.
    /// </summary>
    private void EmitBanking(Shop shop, DateTime day)
    {
        var balance = _drawer.TryGetValue(shop.Id, out var held) ? held : 0m;
        var sweep = decimal.Round(balance - WorkingFloat, 2);

        if (sweep < 20000m)
        {
            return;
        }

        EmitCash(
            shop.Id,
            day.AddHours(17),
            PettyCashDirection.Out,
            sweep,
            $"Cash banked, week to {day:dd MMM yyyy}",
            systemGenerated: false);
    }

    private void EmitExpense(ScheduledExpense scheduled)
    {
        var expense = New(new Expense
        {
            ShopId = scheduled.ShopId,
            ExpenseCategoryId = scheduled.CategoryId,
            ExpenseDate = scheduled.Date,
            Amount = scheduled.Amount,
            Method = scheduled.Method,
            Note = $"{scheduled.CategoryName} for {scheduled.Date:MMM yyyy}",
        });

        db.Expenses.Add(expense);

        if (scheduled.Method == PaymentMethod.Cash)
        {
            db.PettyCashEntries.Add(New(new PettyCashEntry
            {
                ShopId = scheduled.ShopId,
                EntryDate = scheduled.Date,
                Direction = PettyCashDirection.Out,
                Amount = scheduled.Amount,
                Expense = expense,
                Note = expense.Note,
                IsSystemGenerated = true,
            }));

            Track(scheduled.ShopId, PettyCashDirection.Out, scheduled.Amount);
        }
    }

    private void EmitCash(
        long shopId,
        DateTime at,
        PettyCashDirection direction,
        decimal amount,
        string note,
        bool systemGenerated)
    {
        db.PettyCashEntries.Add(New(new PettyCashEntry
        {
            ShopId = shopId,
            EntryDate = at,
            Direction = direction,
            Amount = amount,
            Note = note,
            IsSystemGenerated = systemGenerated,
        }));

        Track(shopId, direction, amount);
    }

    private void Track(long shopId, PettyCashDirection direction, decimal amount)
    {
        var balance = _drawer.TryGetValue(shopId, out var held) ? held : 0m;
        _drawer[shopId] = direction == PettyCashDirection.In ? balance + amount : balance - amount;
    }

    /// <summary>
    /// The monthly bills, worked out up front so the day loop can emit them in order
    /// alongside the sales that pay for them.
    /// </summary>
    private List<ScheduledExpense> BuildExpenseSchedule(List<Shop> shops, List<ExpenseCategory> categories)
    {
        var schedule = new List<ScheduledExpense>();
        var earliest = Today.AddDays(-HistoryDays);

        for (var monthsAgo = 6; monthsAgo >= 0; monthsAgo--)
        {
            foreach (var shop in shops)
            {
                foreach (var category in categories)
                {
                    if (_rng.Next(0, 10) < 2)
                    {
                        continue;
                    }

                    var date = MonthDay(monthsAgo, _rng.Next(1, 27));
                    if (date > Now || date < earliest)
                    {
                        continue;
                    }

                    var amount = category.Name switch
                    {
                        "Rent" => 85000m,
                        "Salaries" => decimal.Round(120000m + _rng.Next(0, 40000), 2),
                        "Utilities" => decimal.Round(18000m + _rng.Next(0, 12000), 2),
                        "Bank charges" => decimal.Round(1200m + _rng.Next(0, 2500), 2),
                        "Packaging" => decimal.Round(2500m + _rng.Next(0, 6000), 2),
                        _ => decimal.Round(4000m + _rng.Next(0, 16000), 2),
                    };

                    // Rent and payroll go through the bank; the rest come out of the till,
                    // which is what makes the petty cash drawer move.
                    var method = category.Name is "Rent" or "Salaries" or "Bank charges"
                        ? PaymentMethod.Bank
                        : PaymentMethod.Cash;

                    schedule.Add(new ScheduledExpense(
                        date, shop.Id, category.Id, category.Name, amount, method));
                }
            }
        }

        return [.. schedule.OrderBy(e => e.Date)];
    }

    /// <summary>
    /// Branch transfers and stock corrections, so the movement history shows every type
    /// the enum defines rather than only the three the till produces.
    /// </summary>
    private async Task SeedTransfersAndAdjustmentsAsync(List<Shop> shops, List<Product> products, CancellationToken ct)
    {
        if (shops.Count >= 2)
        {
            for (var i = 0; i < 24; i++)
            {
                var from = shops[_rng.Next(shops.Count)];
                var to = shops.First(s => s.Id != from.Id);
                var product = products[_rng.Next(products.Count)];
                var at = Today.AddDays(-_rng.Next(1, HistoryDays));
                var qty = _rng.Next(1, 5);

                if (OnHand(from.Id, product.Id) < qty)
                {
                    continue;
                }

                var reference = $"TRF-{at:yyyyMMdd}-{i + 1:D3}";

                AddStock(from.Id, product.Id, -qty, StockMovementType.TransferOut, product.CostPrice, reference, at);
                AddStock(to.Id, product.Id, qty, StockMovementType.TransferIn, product.CostPrice, reference, at);
            }
        }

        for (var i = 0; i < 30; i++)
        {
            var shop = shops[_rng.Next(shops.Count)];
            var product = products[_rng.Next(products.Count)];
            var at = Today.AddDays(-_rng.Next(1, HistoryDays));
            var qty = _rng.Next(1, 4);
            var increase = _rng.Next(0, 10) < 4;

            if (!increase && OnHand(shop.Id, product.Id) < qty)
            {
                continue;
            }

            AddStock(
                shop.Id,
                product.Id,
                increase ? qty : -qty,
                increase ? StockMovementType.AdjustmentIn : StockMovementType.AdjustmentOut,
                product.CostPrice,
                $"ADJ-{at:yyyyMMdd}-{i + 1:D3}",
                at,
                increase ? "Stock count correction, found" : "Damaged in handling");
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Payments received against an account rather than a single invoice. The receipts
    /// screen and the oldest-first allocation only make sense when some of these exist.
    /// </summary>
    private async Task SeedOnAccountReceiptsAsync(List<Shop> shops, List<Customer> customers, CancellationToken ct)
    {
        var owing = await db.SalesInvoices
            .Where(i => i.Status != InvoiceStatus.Void && i.PaidAmount < i.Total)
            .GroupBy(i => new { i.CustomerId, i.ShopId })
            .Select(g => new
            {
                g.Key.CustomerId,
                g.Key.ShopId,
                Outstanding = g.Sum(i => i.Total - i.PaidAmount),
                Oldest = g.Min(i => i.InvoiceDate),
            })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var account in owing)
        {
            if (account.Outstanding <= 0 || _rng.Next(0, 10) < 5)
            {
                continue;
            }

            var customer = customers.FirstOrDefault(c => c.Id == account.CustomerId);
            if (customer is null || customer.IsWalkIn)
            {
                continue;
            }

            // A part payment on account: enough to move the ageing, never enough to clear
            // it, so the receivables screen keeps something in the older buckets.
            var amount = decimal.Round(account.Outstanding * (decimal)(0.2 + (_rng.NextDouble() * 0.4)), 2);
            if (amount <= 0)
            {
                continue;
            }

            var receivedAt = account.Oldest.AddDays(_rng.Next(5, 40));
            if (receivedAt > Now)
            {
                receivedAt = Now.AddDays(-_rng.Next(1, 10));
            }

            var method = _rng.Next(0, 10) < 5 ? PaymentMethod.Cash : PaymentMethod.Bank;

            db.CustomerPayments.Add(New(new CustomerPayment
            {
                ShopId = account.ShopId,
                CustomerId = account.CustomerId,
                SalesInvoiceId = null,
                ReceivedAt = receivedAt,
                Amount = amount,
                Method = method,
                Note = "Payment on account",
            }));

            if (method == PaymentMethod.Cash)
            {
                db.PettyCashEntries.Add(New(new PettyCashEntry
                {
                    ShopId = account.ShopId,
                    EntryDate = receivedAt,
                    Direction = PettyCashDirection.In,
                    Amount = amount,
                    Note = $"Receipt from {customer.Name}",
                    IsSystemGenerated = true,
                }));
            }

            // Allocate it oldest invoice first, which is the rule the receipts screen
            // follows when a payment is not tied to a document.
            var remaining = amount;
            var invoices = await db.SalesInvoices
                .Where(i => i.CustomerId == account.CustomerId
                    && i.ShopId == account.ShopId
                    && i.Status != InvoiceStatus.Void
                    && i.PaidAmount < i.Total)
                .OrderBy(i => i.InvoiceDate)
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (var invoice in invoices)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var due = invoice.Total - invoice.PaidAmount;
                var applied = Math.Min(due, remaining);

                invoice.PaidAmount += applied;
                remaining -= applied;

                invoice.Status = invoice.PaidAmount <= 0
                    ? InvoiceStatus.Unpaid
                    : invoice.PaidAmount >= invoice.Total ? InvoiceStatus.Paid : InvoiceStatus.Partial;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A plausible audit trail. The log is written by the services in normal use, but a
    /// freshly seeded database has never had anyone sign in, so the screen would be empty
    /// on the one day someone is most likely to look at it.
    /// </summary>
    private async Task SeedAuditTrailAsync(List<Shop> shops, List<AppUser> users, CancellationToken ct)
    {
        if (await db.AuditLogs.AnyAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        var actors = users.Where(u => u.Username != AdminUsername).ToList();
        if (actors.Count == 0)
        {
            actors = users;
        }

        for (var daysAgo = HistoryDays; daysAgo >= 0; daysAgo--)
        {
            if (_rng.Next(0, 10) < 4)
            {
                continue;
            }

            var day = Today.AddDays(-daysAgo);

            foreach (var actor in actors.OrderBy(_ => _rng.Next()).Take(_rng.Next(1, 4)))
            {
                var shop = shops[_rng.Next(shops.Count)];
                var signIn = day.AddHours(_rng.Next(9, 11)).AddMinutes(_rng.Next(0, 59));

                db.AuditLogs.Add(new AuditLog
                {
                    At = signIn,
                    UserId = actor.Id,
                    Username = actor.Username,
                    Module = Module.Users,
                    Action = AuditAction.Login,
                    Entity = nameof(AppUser),
                    EntityId = actor.Id,
                    Summary = $"Login for '{actor.Username}'",
                    Machine = $"TILL-{shop.Code}",
                });

                var (module, action, entity) = Activities[_rng.Next(Activities.Length)];

                db.AuditLogs.Add(new AuditLog
                {
                    At = signIn.AddHours(_rng.Next(1, 7)),
                    UserId = actor.Id,
                    Username = actor.Username,
                    ShopId = shop.Id,
                    Module = module,
                    Action = action,
                    Entity = entity,
                    Summary = $"{action} on {entity}",
                    Machine = $"TILL-{shop.Code}",
                });

                db.AuditLogs.Add(new AuditLog
                {
                    At = signIn.AddHours(_rng.Next(7, 10)),
                    UserId = actor.Id,
                    Username = actor.Username,
                    Module = Module.Users,
                    Action = AuditAction.Logout,
                    Entity = nameof(AppUser),
                    EntityId = actor.Id,
                    Machine = $"TILL-{shop.Code}",
                });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ helpers

    private int OnHand(long shopId, long productId)
        => _levels.TryGetValue((shopId, productId), out var level) ? level.Qty : 0;

    /// <summary>
    /// Moves stock and records the movement. The level is a tracked entity held in
    /// <see cref="_levels"/>, so this never goes back to the database.
    /// </summary>
    private void AddStock(
        long shopId,
        long productId,
        int signedQty,
        StockMovementType type,
        decimal unitCost,
        string reference,
        DateTime movedAt,
        string? reason = null)
    {
        if (!_levels.TryGetValue((shopId, productId), out var level))
        {
            level = new StockLevel { ShopId = shopId, ProductId = productId, Qty = 0 };
            db.StockLevels.Add(level);
            _levels[(shopId, productId)] = level;
        }

        level.Qty += signedQty;

        db.StockMovements.Add(New(new StockMovement
        {
            ShopId = shopId,
            ProductId = productId,
            Type = type,
            Qty = signedQty,
            UnitCost = unitCost,
            MovedAt = movedAt,
            Reference = reference,
            Reason = reason,
        }));
    }

    private static DateTime Now => DateTime.UtcNow;

    private static DateTime Today => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private DateTime MonthDay(int monthsAgo, int day)
    {
        var month = new DateTime(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-monthsAgo);
        var safeDay = Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month));
        return month.AddDays(safeDay - 1).AddHours(_rng.Next(9, 19));
    }

    private static T New<T>(T entity, string by = "seed")
        where T : Domain.Common.AuditableEntity
    {
        entity.CreatedAt = Now;
        entity.CreatedBy = by;
        return entity;
    }

    private sealed record StandardAccount(
        string Username,
        string FullName,
        int[] Shops,
        Module[] Write,
        Module[] Read);

    private sealed record ReferenceData(
        List<Product> Products,
        List<Supplier> Suppliers,
        List<Customer> Customers,
        List<ExpenseCategory> ExpenseCategories);

    private sealed record ScheduledExpense(
        DateTime Date,
        long ShopId,
        long CategoryId,
        string CategoryName,
        decimal Amount,
        PaymentMethod Method);
}
