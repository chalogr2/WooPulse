namespace WooPulse.Api.Data;

/// <summary>
/// How the API key is sent to WooCommerce (see DEVLOG #006):
/// Header      = "Authorization: Basic base64(key:secret)" (preferred)
/// QueryString = "?consumer_key=...&amp;consumer_secret=..." (fallback for servers that strip the
///               Authorization header; WooCommerce only allows this over HTTPS)
/// </summary>
public enum WooAuthMode
{
    Header = 1,
    QueryString = 2
}

/// <summary>
/// What: how ONE store connects to its WooCommerce shop (1-to-1 with Store).
/// Why a separate table (not columns on Store): the integration is its own concern. It holds
///      secrets and sync state, and a store can exist without being connected.
///
/// SECURITY: ConsumerKey/ConsumerSecret are stored ENCRYPTED with ASP.NET Data Protection
/// (see ConnectionService). The plain values never touch the database, logs or API responses.
/// </summary>
public class StoreConnection
{
    public int Id { get; set; }

    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;

    /// <summary>Normalized shop base URL, e.g. "https://shop.example.com" (no trailing slash).</summary>
    public string ShopUrl { get; set; } = "";

    public string ConsumerKeyProtected { get; set; } = "";
    public string ConsumerSecretProtected { get; set; } = "";

    /// <summary>Safe-to-show hint like "ck_…a1b2", so users can tell which key is saved.</summary>
    public string KeyHint { get; set; } = "";

    public WooAuthMode AuthMode { get; set; } = WooAuthMode.Header;

    // Audit
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBySub { get; set; } = "";
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public string? UpdatedBySub { get; set; }

    // Last "Test connection" result
    public DateTimeOffset? LastTestedAtUtc { get; set; }
    public bool? LastTestOk { get; set; }
    public string? LastTestMessage { get; set; }
}
