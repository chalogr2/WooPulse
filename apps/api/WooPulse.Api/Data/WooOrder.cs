namespace WooPulse.Api.Data;

/// <summary>
/// What: a WooCommerce order, copied ("synced") into our database for fast analytics.
/// Why copy instead of asking WooCommerce live: dashboards read our own Postgres (fast,
///      works even if the shop is slow or down), and we control the queries.
///
/// PRIVACY: on purpose there are NO customer fields (name, email, address, phone).
/// Analytics only needs amounts, statuses, dates and products, so we store nothing personal.
///
/// Named WooOrder (not Order) so it's obvious it mirrors WooCommerce data. The table is
/// still called "Orders" (from the DbSet name in AppDbContext).
/// </summary>
public class WooOrder
{
    public int Id { get; set; }                 // our id

    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public long WooOrderId { get; set; }        // WooCommerce's id. Unique per store, used for upserts.
    public string Number { get; set; } = "";    // order number shown to customers (usually = id)
    public string Status { get; set; } = "";    // "processing", "completed", "refunded", ...
    public string Currency { get; set; } = "";  // e.g. "USD"

    // Money: always decimal (exact), never double/float (binary rounding errors: 0.1 + 0.2 != 0.3).
    public decimal Total { get; set; }          // grand total the customer paid
    public decimal TotalTax { get; set; }
    public decimal ShippingTotal { get; set; }
    public decimal DiscountTotal { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// The calendar day the order was placed, in the SHOP's local time zone (WooCommerce's
    /// "date_created"). Daily charts group by this, so an order placed at 11pm in the shop's
    /// country counts on that day, not the next UTC day.
    /// DateOnly maps to a Postgres "date" column (no time, no time zone).
    /// </summary>
    public DateOnly CreatedLocalDate { get; set; }

    public DateTimeOffset? ModifiedAtUtc { get; set; }
    public DateTimeOffset SyncedAtUtc { get; set; }   // when WE last copied it

    public List<WooOrderItem> Items { get; set; } = new();
}

/// <summary>One product line of an order (for "top products").</summary>
public class WooOrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public WooOrder Order { get; set; } = null!;

    public long WooProductId { get; set; }
    public long WooVariationId { get; set; }    // 0 if the product has no variations
    public string Name { get; set; } = "";      // product name at the time of the order
    public int Quantity { get; set; }
    public decimal Total { get; set; }          // line total after discounts, before tax
}
