namespace WooPulse.Api.Integrations.WooCommerce;

// What: the shape of WooCommerce's JSON (GET /wp-json/wc/v3/orders), only the fields we use.
// Why separate from Data/WooOrder: this is THEIR format (snake_case, money as strings).
//      Data/WooOrder is OURS. SyncService maps one to the other, so changes on their
//      side don't ripple through our database model.
//
// Property names are PascalCase here; WooCommerceClient's JsonSerializerOptions uses
// JsonNamingPolicy.SnakeCaseLower, so "DateCreatedGmt" reads the JSON key "date_created_gmt".
// Money comes as strings ("123.45"); NumberHandling.AllowReadingFromString parses them into decimal.

public class WooOrderJson
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string Status { get; set; } = "";
    public string Currency { get; set; } = "";

    public decimal Total { get; set; }
    public decimal TotalTax { get; set; }
    public decimal ShippingTotal { get; set; }
    public decimal DiscountTotal { get; set; }

    public DateTime? DateCreated { get; set; }      // shop local time, e.g. "2024-05-01T22:15:00"
    public DateTime? DateCreatedGmt { get; set; }   // same moment in UTC
    public DateTime? DateModifiedGmt { get; set; }

    public List<WooLineItemJson> LineItems { get; set; } = new();
}

public class WooLineItemJson
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long ProductId { get; set; }
    public long VariationId { get; set; }
    public int Quantity { get; set; }
    public decimal Total { get; set; }
}

/// <summary>A WooCommerce call failed. Message is safe to show to users (no secrets).</summary>
public class WooApiException : Exception
{
    public WooApiException(string message) : base(message) { }
}
