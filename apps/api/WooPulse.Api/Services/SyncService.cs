using System.Net;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;
using WooPulse.Api.Integrations.WooCommerce;

namespace WooPulse.Api.Services;

/// <summary>
/// What: copies WooCommerce orders into our Orders/OrderItems tables (DEVLOG #012).
///
/// Flow:
///   "Sync now" (any member) ─┐
///   every 15 min (Hangfire) ──┴→ enqueue SyncStoreAsync(storeId) as a Hangfire background job
///                                  1. claim the store's sync lock (or quit if one is running)
///                                  2. decrypt the API key
///                                  3. fetch pages of 100 orders, oldest first
///                                  4. upsert each page (insert new, update existing)
///                                  5. record the result, release the lock
///
/// Why background jobs: the first sync is ~60 HTTP calls (5,988 orders). That's far too long
/// for a web request, and Hangfire also survives API restarts.
/// </summary>
public class SyncService
{
    // Owner decisions (DEVLOG #011)
    public const int InitialHistoryYears = 3;
    public const int IncrementalWindowDays = 30;   // re-fetch window that catches status changes
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _db;
    private readonly ConnectionService _connections;
    private readonly WooCommerceClient _woo;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<SyncService> _log;

    public SyncService(AppDbContext db, ConnectionService connections, WooCommerceClient woo,
                       IBackgroundJobClient jobs, ILogger<SyncService> log)
    {
        _db = db;
        _connections = connections;
        _woo = woo;
        _jobs = jobs;
        _log = log;
    }

    // ---------------------------------------------------------------------
    // Entry points
    // ---------------------------------------------------------------------

    /// <summary>"Sync now" button (any member). Returns 202 Accepted: queued, not finished.</summary>
    public async Task<IResult> RequestSyncAsync(string sub, int storeId)
    {
        var me = await _db.FindMembershipAsync(storeId, sub);
        if (me is null) return Results.NotFound();

        var c = await _db.StoreConnections.SingleOrDefaultAsync(x => x.StoreId == storeId);
        if (c is null)
            return Results.Problem("Not connected", "Connect this store to WooCommerce first.", statusCode: 409);

        // Lifted comparison: if SyncLockedUntilUtc is null, "null > now" is simply false.
        if (c.SyncLockedUntilUtc > DateTimeOffset.UtcNow)
            return Results.Problem("Sync already running", "A sync for this store is already in progress.", statusCode: 409);

        Enqueue(storeId);
        return Results.Accepted();
    }

    /// <summary>Queue one store's sync. Also used by ConnectionService right after connecting.</summary>
    public void Enqueue(int storeId)
        // Hangfire stores "SyncStoreAsync(<storeId value>, CancellationToken)". Here we WANT the
        // argument captured at enqueue time (compare the heartbeat lesson in #008).
        // CancellationToken.None is a placeholder: Hangfire swaps in its own shutdown token.
        => _jobs.Enqueue<SyncService>(s => s.SyncStoreAsync(storeId, CancellationToken.None));

    /// <summary>Recurring job (every 15 min): queue a separate sync job for every connected store.</summary>
    public async Task EnqueueAllAsync()
    {
        // Query filter → stores in the trash are skipped automatically.
        var storeIds = await _db.StoreConnections.Select(c => c.StoreId).ToListAsync();
        foreach (var id in storeIds) Enqueue(id);
    }

    // ---------------------------------------------------------------------
    // The job
    // ---------------------------------------------------------------------

    /// <summary>
    /// [AutomaticRetry(Attempts = 0)]: Hangfire retries failed jobs 10 times by default. We
    /// catch and RECORD errors ourselves (shown in the UI), and the next 15-minute run is the retry.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task SyncStoreAsync(int storeId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Normal (filtered) query: is there a connection, and is the store not deleted?
        var exists = await _db.StoreConnections.AnyAsync(c => c.StoreId == storeId, ct);
        if (!exists) return;

        // 1) CLAIM THE LOCK (one UPDATE statement; see StoreConnection.SyncLockedUntilUtc).
        //    Two jobs for the same store → only one updates 1 row; the other gets 0 and quits.
        var claimed = await _db.StoreConnections
            .IgnoreQueryFilters()   // already checked above; keeps the UPDATE a simple single-table statement
            .Where(c => c.StoreId == storeId && (c.SyncLockedUntilUtc == null || c.SyncLockedUntilUtc < now))
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.SyncLockedUntilUtc, now + LockDuration)
                .SetProperty(c => c.LastSyncStartedAtUtc, now), ct);

        if (claimed == 0)
        {
            _log.LogInformation("Sync for store {StoreId} skipped: already running", storeId);
            return;
        }

        bool ok;
        string message;
        try
        {
            var (seen, added, initial) = await RunSyncAsync(storeId, now, ct);
            ok = true;
            message = initial
                ? $"Full sync done: {seen:N0} orders ({added:N0} new)."
                : $"Synced {seen:N0} orders from the last {IncrementalWindowDays} days ({added:N0} new).";
        }
        catch (Exception ex)
        {
            // Catch EVERYTHING here: whatever happens, we must record it and release the lock.
            ok = false;
            message = ex is WooApiException ? ex.Message : $"Sync failed: {ex.Message}";
            _log.LogError(ex, "Sync for store {StoreId} failed", storeId);
        }

        // 5) RECORD RESULT + RELEASE LOCK. CancellationToken.None: even during shutdown,
        //    finish this write so the lock doesn't stay stuck for 30 minutes.
        var finishedAt = DateTimeOffset.UtcNow;
        await _db.StoreConnections
            .IgnoreQueryFilters()
            .Where(c => c.StoreId == storeId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.SyncLockedUntilUtc, (DateTimeOffset?)null)
                .SetProperty(c => c.LastSyncCompletedAtUtc, finishedAt)
                .SetProperty(c => c.LastSyncOk, ok)
                .SetProperty(c => c.LastSyncMessage, message)
                // On success, mark the initial sync done (COALESCE keeps the first date if already set).
                .SetProperty(c => c.InitialSyncCompletedAtUtc,
                             c => ok ? (c.InitialSyncCompletedAtUtc ?? finishedAt) : c.InitialSyncCompletedAtUtc),
                CancellationToken.None);
    }

    /// <summary>Steps 2–4. Returns (orders seen, orders newly added, was this the full initial sync).</summary>
    private async Task<(int Seen, int Added, bool Initial)> RunSyncAsync(int storeId, DateTimeOffset now, CancellationToken ct)
    {
        // 2) DECRYPT CREDENTIALS
        var conn = await _db.StoreConnections.AsNoTracking().SingleAsync(c => c.StoreId == storeId, ct);
        var creds = _connections.TryUnprotect(conn)
            ?? throw new WooApiException("The saved API key can't be decrypted. Ask the owner to enter it again.");

        var initial = conn.InitialSyncCompletedAtUtc is null;
        var createdAfter = initial ? now.AddYears(-InitialHistoryYears) : now.AddDays(-IncrementalWindowDays);

        // 3 + 4) PAGE THROUGH ORDERS
        int page = 1, totalPages, seen = 0, added = 0;
        do
        {
            var (orders, pages) = await _woo.GetOrdersPageAsync(creds, createdAfter, page, ct);
            totalPages = pages;
            if (orders.Count == 0) break;

            added += await UpsertPageAsync(storeId, orders, ct);
            seen += orders.Count;
            _log.LogInformation("Store {StoreId}: synced page {Page}/{TotalPages}", storeId, page, totalPages);
            page++;
        }
        while (page <= totalPages);

        return (seen, added, initial);
    }

    /// <summary>
    /// UPSERT one page = INSERT orders we don't have, UPDATE the ones we do.
    /// Done with ONE query to find existing rows for the whole page (not one query per
    /// order), and ONE SaveChanges per page (a single transaction for 100 orders).
    /// </summary>
    private async Task<int> UpsertPageAsync(int storeId, List<WooOrderJson> page, CancellationToken ct)
    {
        var wooIds = page.Select(o => o.Id).ToList();

        // ToDictionaryAsync: id → entity, so lookups below are O(1) instead of searching a list.
        // Include(Items): needed because we replace each existing order's line items.
        var existing = await _db.Orders
            .IgnoreQueryFilters()
            .Include(o => o.Items)
            .Where(o => o.StoreId == storeId && wooIds.Contains(o.WooOrderId))   // SQL: = ANY(@ids)
            .ToDictionaryAsync(o => o.WooOrderId, ct);

        var syncedAt = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var w in page)
        {
            // "out var order": TryGetValue returns bool AND gives us the value if found.
            if (!existing.TryGetValue(w.Id, out var order))
            {
                order = new WooOrder { StoreId = storeId, WooOrderId = w.Id };
                _db.Orders.Add(order);
                added++;
            }
            Map(w, order, syncedAt);
        }

        await _db.SaveChangesAsync(ct);

        // Forget all tracked entities. Without this, EF would keep every order of every page
        // in memory for the whole sync (thousands of objects) and SaveChanges gets slower.
        _db.ChangeTracker.Clear();
        return added;
    }

    /// <summary>Copy WooCommerce's JSON shape into our entity.</summary>
    private static void Map(WooOrderJson w, WooOrder o, DateTimeOffset syncedAt)
    {
        o.Number = w.Number;
        o.Status = w.Status;
        o.Currency = w.Currency;
        o.Total = w.Total;
        o.TotalTax = w.TotalTax;
        o.ShippingTotal = w.ShippingTotal;
        o.DiscountTotal = w.DiscountTotal;

        // WooCommerce sends "_gmt" dates without a zone marker. They ARE UTC, so we say so
        // explicitly (SpecifyKind) before converting to DateTimeOffset.
        var createdUtc = w.DateCreatedGmt is { } gmt          // "is { } x" = "is not null, and call it x"
            ? new DateTimeOffset(DateTime.SpecifyKind(gmt, DateTimeKind.Utc))
            : syncedAt;
        o.CreatedAtUtc = createdUtc;
        o.CreatedLocalDate = DateOnly.FromDateTime(w.DateCreated ?? createdUtc.UtcDateTime);
        o.ModifiedAtUtc = w.DateModifiedGmt is { } mod
            ? new DateTimeOffset(DateTime.SpecifyKind(mod, DateTimeKind.Utc))
            : null;
        o.SyncedAtUtc = syncedAt;

        // Replace line items. For an existing order, Clear() marks the old items for DELETE
        // (EF removes "orphans" of a required relationship), then we add the current ones.
        o.Items.Clear();
        foreach (var li in w.LineItems)
        {
            o.Items.Add(new WooOrderItem
            {
                WooProductId = li.ProductId,
                WooVariationId = li.VariationId,
                Name = WebUtility.HtmlDecode(li.Name),   // WooCommerce sends "Tom &amp; Jerry" → "Tom & Jerry"
                Quantity = li.Quantity,
                Total = li.Total,
            });
        }
    }
}
