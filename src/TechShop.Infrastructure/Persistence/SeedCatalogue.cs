namespace TechShop.Infrastructure.Persistence;

/// <summary>
/// The reference rows the demo database is built from, kept apart from the seeding logic
/// so <see cref="DatabaseSeeder"/> stays readable. Prices are in rupees and roughly
/// track a real Pakistani computer shop, which matters: a catalogue of round numbers
/// makes the profit and ageing reports look fake even when the arithmetic is right.
/// </summary>
internal static class SeedCatalogue
{
    internal sealed record ProductRow(
        string Sku,
        string Name,
        string Category,
        string Brand,
        decimal Cost,
        decimal Price,
        int Reorder,
        int OpeningLow,
        int OpeningHigh);

    /// <summary>
    /// The standard sales tax rate applied to taxable lines in the demo data.
    /// </summary>
    internal const decimal StandardTaxRate = 17m;

    /// <summary>
    /// Categories that do not attract sales tax here, so the demo database has a
    /// genuinely mixed basket: a sale of a laptop plus a ream of paper has tax on one
    /// line and not the other, which is the case the till has to get right.
    /// </summary>
    internal static readonly string[] TaxExemptCategories = ["Printing", "Peripheral"];

    internal static readonly string[] Categories =
    [
        "Desktop", "Laptop", "Monitor", "Peripheral", "Storage", "Networking", "Printing", "Power",
    ];

    internal static readonly string[] ExpenseCategories =
    [
        "Rent", "Salaries", "Utilities", "Transport", "Marketing", "Repairs", "Packaging", "Bank charges",
    ];

    /// <summary>
    /// Sixty lines. Opening quantities are banded by value: nobody keeps thirty gaming
    /// rigs on the floor, and nobody reorders HDMI cables three at a time.
    /// </summary>
    internal static readonly ProductRow[] Products =
    [
        // ---- Desktop
        new("PC-RYZ5-001", "Desktop Ryzen 5 5600 / 16GB / 512GB", "Desktop", "Custom Build", 92000m, 112000m, 3, 6, 14),
        new("PC-I5-002", "Desktop Core i5 12400 / 8GB / 256GB", "Desktop", "Custom Build", 74000m, 92500m, 3, 6, 14),
        new("PC-I7-003", "Desktop Core i7 13700 / 32GB / 1TB", "Desktop", "Custom Build", 148000m, 179000m, 2, 4, 9),
        new("PC-OFF-004", "Office PC Dual Core / 4GB / 500GB", "Desktop", "Refurb", 26000m, 34500m, 5, 10, 24),
        new("PC-GAM-005", "Gaming Rig RTX 4060 / 16GB", "Desktop", "Custom Build", 235000m, 279000m, 2, 3, 7),
        new("PC-GAM-006", "Gaming Rig RTX 4070 Ti / 32GB", "Desktop", "Custom Build", 352000m, 414000m, 1, 2, 5),
        new("PC-AIO-007", "All-in-One 24in Core i3", "Desktop", "Lenovo", 88000m, 108000m, 3, 4, 10),
        new("PC-MIN-008", "Mini PC N100 / 8GB / 256GB", "Desktop", "Beelink", 42000m, 54500m, 4, 8, 18),

        // ---- Laptop
        new("LP-DEL-101", "Dell Latitude 5420 i5 11th Gen", "Laptop", "Dell", 118000m, 142000m, 3, 5, 12),
        new("LP-DEL-102", "Dell Vostro 3520 i5 12th Gen", "Laptop", "Dell", 134000m, 161000m, 3, 5, 12),
        new("LP-HP-103", "HP ProBook 450 G9 i5", "Laptop", "HP", 124000m, 149500m, 3, 5, 12),
        new("LP-HP-104", "HP Pavilion 15 Ryzen 7", "Laptop", "HP", 152000m, 182000m, 2, 4, 10),
        new("LP-LEN-105", "Lenovo ThinkPad T14 i7", "Laptop", "Lenovo", 186000m, 219000m, 2, 3, 9),
        new("LP-LEN-106", "Lenovo IdeaPad Slim 3 i3", "Laptop", "Lenovo", 82000m, 101000m, 4, 6, 15),
        new("LP-ASU-107", "Asus Vivobook 15 Ryzen 5", "Laptop", "Asus", 96000m, 117500m, 4, 6, 15),
        new("LP-ASU-108", "Asus TUF Gaming A15 RTX 3050", "Laptop", "Asus", 214000m, 252000m, 2, 3, 8),
        new("LP-ACE-109", "Acer Aspire 5 i3 12th Gen", "Laptop", "Acer", 78000m, 96000m, 4, 6, 16),
        new("LP-MAC-110", "MacBook Air M2 256GB", "Laptop", "Apple", 268000m, 309000m, 2, 3, 7),

        // ---- Monitor
        new("MN-DEL-201", "Monitor 24in IPS 75Hz", "Monitor", "Dell", 32000m, 39500m, 6, 10, 24),
        new("MN-DEL-202", "Monitor 27in QHD IPS", "Monitor", "Dell", 58000m, 71000m, 4, 6, 16),
        new("MN-SAM-203", "Monitor 22in VA 100Hz", "Monitor", "Samsung", 24500m, 31000m, 6, 12, 28),
        new("MN-LG-204", "Monitor 27in 4K IPS", "Monitor", "LG", 92000m, 112000m, 3, 4, 10),
        new("MN-AOC-205", "Gaming Monitor 24in 165Hz", "Monitor", "AOC", 46000m, 57500m, 4, 6, 15),
        new("MN-ARM-206", "Monitor Arm Dual", "Monitor", "North Bayou", 7200m, 10400m, 8, 14, 32),

        // ---- Peripheral
        new("PR-KEY-301", "Mechanical Keyboard RGB", "Peripheral", "Redragon", 6200m, 8900m, 10, 24, 60),
        new("PR-KEY-302", "Wireless Keyboard and Mouse Set", "Peripheral", "Logitech", 5400m, 7800m, 12, 28, 68),
        new("PR-MOU-303", "Wireless Mouse 2.4G", "Peripheral", "Logitech", 2100m, 3450m, 15, 36, 90),
        new("PR-MOU-304", "Gaming Mouse 16000 DPI", "Peripheral", "Razer", 8900m, 12400m, 8, 16, 40),
        new("PR-HDS-305", "Headset with Mic", "Peripheral", "HyperX", 7400m, 10500m, 8, 18, 44),
        new("PR-HDS-306", "Bluetooth Earbuds", "Peripheral", "Soundcore", 6800m, 9600m, 10, 20, 50),
        new("PR-WEB-307", "Webcam 1080p", "Peripheral", "Logitech", 6800m, 9400m, 8, 16, 40),
        new("PR-PAD-308", "Mouse Pad XL", "Peripheral", "Generic", 780m, 1600m, 20, 40, 100),
        new("PR-CAB-309", "HDMI Cable 2m", "Peripheral", "Generic", 650m, 1250m, 25, 60, 140),
        new("PR-HUB-310", "USB-C Hub 7-in-1", "Peripheral", "Ugreen", 4900m, 7200m, 10, 20, 50),

        // ---- Storage
        new("ST-SSD-401", "SSD 1TB NVMe Gen3", "Storage", "Samsung", 14500m, 18900m, 10, 20, 50),
        new("ST-SSD-402", "SSD 512GB NVMe Gen3", "Storage", "Kingston", 8200m, 11400m, 12, 24, 58),
        new("ST-SSD-403", "SSD 2TB NVMe Gen4", "Storage", "WD", 31000m, 38500m, 5, 8, 20),
        new("ST-HDD-404", "HDD 2TB SATA", "Storage", "Seagate", 11200m, 14800m, 8, 16, 40),
        new("ST-HDD-405", "HDD 4TB SATA", "Storage", "Seagate", 19500m, 25000m, 5, 8, 20),
        new("ST-RAM-406", "RAM 8GB DDR4 3200", "Storage", "Kingston", 5900m, 8200m, 12, 26, 64),
        new("ST-RAM-407", "RAM 16GB DDR4 3200", "Storage", "Corsair", 11400m, 15200m, 8, 16, 40),

        // ---- Networking
        new("NW-RTR-501", "Wi-Fi Router AC1200", "Networking", "TP-Link", 7900m, 10900m, 8, 16, 40),
        new("NW-RTR-502", "Wi-Fi 6 Router AX3000", "Networking", "TP-Link", 21500m, 27500m, 5, 8, 20),
        new("NW-SWT-503", "Switch 8 Port Gigabit", "Networking", "TP-Link", 6400m, 8800m, 6, 12, 30),
        new("NW-SWT-504", "Switch 24 Port Gigabit", "Networking", "D-Link", 28000m, 35500m, 3, 4, 10),
        new("NW-CAT-505", "Cat6 Cable 50m Box", "Networking", "Generic", 9200m, 12500m, 4, 8, 20),
        new("NW-EXT-506", "Wi-Fi Range Extender", "Networking", "TP-Link", 4600m, 6700m, 8, 16, 40),
        new("NW-NIC-507", "USB Wi-Fi Adapter AC600", "Networking", "TP-Link", 1900m, 3100m, 15, 30, 72),

        // ---- Printing
        new("PT-LAS-601", "Laser Printer Mono", "Printing", "HP", 42000m, 52500m, 4, 6, 16),
        new("PT-LAS-602", "Laser MFP Mono", "Printing", "Canon", 68000m, 83000m, 3, 4, 12),
        new("PT-INK-603", "Inkjet All-in-One Colour", "Printing", "Epson", 51000m, 63000m, 3, 5, 14),
        new("PT-TNR-604", "Toner Cartridge 79A", "Printing", "HP", 8400m, 11500m, 10, 20, 50),
        new("PT-INK-605", "Ink Bottle Set CMYK", "Printing", "Epson", 4200m, 6300m, 12, 24, 60),
        new("PT-PAP-606", "A4 Paper Ream 80gsm", "Printing", "Dunya", 1150m, 1750m, 30, 60, 150),

        // ---- Power
        new("PW-UPS-701", "UPS 1000VA Line Interactive", "Power", "APC", 21500m, 27400m, 5, 8, 22),
        new("PW-UPS-702", "UPS 650VA", "Power", "APC", 13200m, 17500m, 6, 12, 30),
        new("PW-PSU-703", "Power Supply 650W 80+ Bronze", "Power", "Corsair", 16800m, 21500m, 5, 10, 26),
        new("PW-PSU-704", "Power Supply 550W", "Power", "Antec", 11500m, 15200m, 6, 12, 30),
        new("PW-EXT-705", "Surge Protector 6 Socket", "Power", "Generic", 1850m, 2950m, 20, 40, 96),
        new("PW-BAT-706", "UPS Battery 12V 18Ah", "Power", "Volta", 9800m, 13200m, 8, 14, 36),
    ];

    internal static readonly (string Name, string Phone, string Address)[] Suppliers =
    [
        ("Hafeez Centre Distributors", "042 3570001", "Hafeez Centre, Lahore"),
        ("Galaxy IT Supplies", "021 3480002", "Saddar, Karachi"),
        ("Blue Chip Computers", "051 2820003", "Blue Area, Islamabad"),
        ("Technocity Wholesale", "042 3570004", "Hall Road, Lahore"),
        ("Prime Peripherals", "021 3480005", "Tariq Road, Karachi"),
        ("Northern Tech Traders", "091 5840006", "University Road, Peshawar"),
        ("Digital Mart Imports", "042 3570007", "Gulberg III, Lahore"),
        ("Server Line Systems", "051 2820008", "I-9 Industrial, Islamabad"),
    ];

    /// <summary>
    /// Nineteen named accounts plus the walk-in. A mix of business and retail, because
    /// the ageing report is only interesting when some customers run a balance.
    /// </summary>
    internal static readonly (string Name, string Phone, string Address)[] Customers =
    [
        ("Crescent School System", "0301 2345671", "Model Town, Lahore"),
        ("Riaz Textiles", "0301 2345672", "Industrial Estate, Faisalabad"),
        ("Dr Amina Clinic", "0301 2345673", "Gulberg II, Lahore"),
        ("Nova Software House", "0301 2345674", "Johar Town, Lahore"),
        ("Faisal Traders", "0301 2345675", "Shadman, Lahore"),
        ("Alhamd Engineering", "0302 2345676", "Kot Lakhpat, Lahore"),
        ("Sunrise Public School", "0302 2345677", "Township, Lahore"),
        ("Mehran Logistics", "0302 2345678", "Ravi Road, Lahore"),
        ("Bright Future Academy", "0303 2345679", "Wapda Town, Lahore"),
        ("Hussain Brothers", "0303 2345680", "Anarkali, Lahore"),
        ("Orient Pharma", "0303 2345681", "Ferozepur Road, Lahore"),
        ("Pak Steel Fabricators", "0304 2345682", "Badami Bagh, Lahore"),
        ("Zenith Call Centre", "0304 2345683", "DHA Phase 5, Lahore"),
        ("Green Valley Hospital", "0304 2345684", "Raiwind Road, Lahore"),
        ("Tariq Photostat", "0305 2345685", "Urdu Bazar, Lahore"),
        ("Insight Consultants", "0305 2345686", "Garden Town, Lahore"),
        ("Al Noor Printers", "0305 2345687", "Circular Road, Lahore"),
        ("Victory Sports Co", "0306 2345688", "Sialkot Road, Gujranwala"),
        ("Kashif Mobile Zone", "0306 2345689", "Hall Road, Lahore"),
    ];
}
