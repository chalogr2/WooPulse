namespace WooPulse.Api.Dtos;

// What the UI sees about a store's WooCommerce connection.
// NOTE: no key or secret here, ever. Only a hint like "ck_…a1b2".
public record StoreConnectionDto(
    string ShopUrl,
    string KeyHint,
    string AuthMode,                 // "Header" | "QueryString"
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastTestedAtUtc,
    bool? LastTestOk,
    string? LastTestMessage,

    // Order sync status (DEVLOG #012)
    bool SyncInProgress,
    DateTimeOffset? LastSyncStartedAtUtc,
    DateTimeOffset? LastSyncCompletedAtUtc,
    bool? LastSyncOk,
    string? LastSyncMessage,
    bool InitialSyncDone,
    int OrderCount                   // orders currently in OUR database for this store
);

// Body of PUT /stores/{storeId}/connection (Owner only).
// Sent once over the local BFF → API hop, then encrypted before it's stored.
public record SaveConnectionRequestDto(string ShopUrl, string ConsumerKey, string ConsumerSecret);
