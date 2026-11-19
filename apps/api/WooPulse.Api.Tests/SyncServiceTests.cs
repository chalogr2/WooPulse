using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WooPulse.Api.Data;
using WooPulse.Api.Integrations.WooCommerce;
using WooPulse.Api.Services;
using WooPulse.Api.Tests.Infrastructure;
using static WooPulse.Api.Tests.Infrastructure.ResultAssert;

namespace WooPulse.Api.Tests;

/// <summary>
/// Copying WooCommerce orders into our database: paging, upsert without duplicates,
/// the per-store sync lock, and recording failures instead of losing them.
/// </summary>
public sealed class SyncServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeJobClient _jobs = new();
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
    private readonly int _storeId;

    /// <summary>What the fake shop returns, per page number. Tests fill it in.</summary>
    private readonly Dictionary<int, string> _pages = new();
    private readonly FakeWooHandler _shop;

    public SyncServiceTests()
    {
        _storeId = _db.SeedStore(Users.Owner, (Users.Member, StoreRole.Member));
        _shop = new FakeWooHandler(req =>
        {
            var page = int.Parse(System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query)["page"] ?? "1");
            return FakeWooHandler.Ok(_pages.GetValueOrDefault(page, "[]"), ("X-WP-TotalPages", _pages.Count.ToString()));
        });
    }

    public void Dispose() => _db.Dispose();

    private SyncService Service(FakeWooHandler? shop = null, IDataProtectionProvider? protection = null)
    {
        var db = _db.NewContext();
        var woo = new WooCommerceClient(new HttpClient(shop ?? _shop));
        var connections = new ConnectionService(db, woo, protection ?? _protection, _jobs);
        return new SyncService(db, connections, woo, _jobs, NullLogger<SyncService>.Instance);
    }

    private void Connect(int storeId, Action<StoreConnection>? tweak = null)
    {
        var protector = _protection.CreateProtector("WooPulse.StoreConnection.v1");
        using var db = _db.NewContext();
        var c = new StoreConnection
        {
            StoreId = storeId,
            ShopUrl = "https://shop.example.test",
            ConsumerKeyProtected = protector.Protect("ck_key"),
            ConsumerSecretProtected = protector.Protect("cs_secret"),
            KeyHint = "ck_…_key",
            CreatedBySub = Users.Owner,
        };
        tweak?.Invoke(c);
        db.StoreConnections.Add(c);
        db.SaveChanges();
    }

    private StoreConnection ConnectionRow()
    {
        using var db = _db.NewContext();
        return db.StoreConnections.IgnoreQueryFilters().AsNoTracking().Single(c => c.StoreId == _storeId);
    }

    private static string OrderJson(long id, string status, string total, string items) => $$"""
        {
          "id": {{id}}, "number": "{{id}}", "status": "{{status}}", "currency": "USD",
          "total": "{{total}}", "total_tax": "0", "shipping_total": "0", "discount_total": "0",
          "date_created": "2026-11-01T22:15:00", "date_created_gmt": "2026-11-02T04:15:00",
          "date_modified_gmt": "2026-11-03T10:00:00",
          "line_items": [{{items}}]
        }
        """;

    private static string Item(long productId, string name, int qty, string total) =>
        $$"""{ "id": 1, "name": "{{name}}", "product_id": {{productId}}, "variation_id": 0, "quantity": {{qty}}, "total": "{{total}}" }""";

    // ------------------------------------------------------------------ the job

    [Fact]
    public async Task FirstSync_FetchesEveryPage_OfThreeYearsOfHistory()
    {
        Connect(_storeId);
        _pages[1] = $"[{OrderJson(1, "completed", "20.00", Item(7, "Tom &amp; Jerry", 2, "20.00"))}, {OrderJson(2, "processing", "5.00", Item(8, "Pen", 1, "5.00"))}]";
        _pages[2] = $"[{OrderJson(3, "completed", "9.00", Item(8, "Pen", 1, "9.00"))}]";

        await Service().SyncStoreAsync(_storeId, default);

        Assert.Equal(2, _shop.Requests.Count);
        Assert.Contains($"after={DateTime.UtcNow.Year - 3}-", _shop.Requests[0].RequestUri!.Query);

        var c = ConnectionRow();
        Assert.True(c.LastSyncOk);
        Assert.Equal("Full sync done: 3 orders (3 new).", c.LastSyncMessage);
        Assert.NotNull(c.InitialSyncCompletedAtUtc);
        Assert.Null(c.SyncLockedUntilUtc);   // lock released

        using var db = _db.NewContext();
        Assert.Equal(3, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Sync_MapsDatesInTheShopsTimeZone_AndDecodesProductNames()
    {
        Connect(_storeId);
        _pages[1] = $"[{OrderJson(1, "completed", "20.00", Item(7, "Tom &amp; Jerry", 2, "20.00"))}]";

        await Service().SyncStoreAsync(_storeId, default);

        using var db = _db.NewContext();
        var order = await db.Orders.Include(o => o.Items).SingleAsync();
        // Placed at 22:15 shop time on Nov 1 = 04:15 UTC on Nov 2. Daily charts must count it on Nov 1.
        Assert.Equal(new DateOnly(2026, 11, 1), order.CreatedLocalDate);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 4, 15, 0, TimeSpan.Zero), order.CreatedAtUtc);
        Assert.Equal(20.00m, order.Total);
        Assert.Equal("Tom & Jerry", Assert.Single(order.Items).Name);
    }

    [Fact]
    public async Task LaterSyncs_OnlyFetchTheLast30Days_AndUpdateOrdersInsteadOfDuplicatingThem()
    {
        Connect(_storeId);
        _pages[1] = $"[{OrderJson(1, "processing", "20.00", Item(7, "Mug", 2, "20.00"))}]";
        await Service().SyncStoreAsync(_storeId, default);

        // The customer got a refund and one mug was removed from the order.
        _pages[1] = $"[{OrderJson(1, "refunded", "10.00", Item(7, "Mug", 1, "10.00"))}]";
        _shop.Requests.Clear();
        await Service().SyncStoreAsync(_storeId, default);

        Assert.Contains($"after={DateTime.UtcNow.AddDays(-30):yyyy-MM-dd}", _shop.Requests[0].RequestUri!.Query);
        Assert.Equal("Synced 1 orders from the last 30 days (0 new).", ConnectionRow().LastSyncMessage);

        using var db = _db.NewContext();
        var order = await db.Orders.Include(o => o.Items).SingleAsync();
        Assert.Equal(("refunded", 10.00m), (order.Status, order.Total));
        var item = Assert.Single(order.Items);   // old line item replaced, not kept next to the new one
        Assert.Equal(1, item.Quantity);
        Assert.Equal(1, await db.OrderItems.CountAsync());
    }

    [Fact]
    public async Task Sync_WhileAnotherSyncHoldsTheLock_DoesNothing()
    {
        Connect(_storeId, c => c.SyncLockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10));
        _pages[1] = $"[{OrderJson(1, "completed", "1.00", Item(1, "A", 1, "1.00"))}]";

        await Service().SyncStoreAsync(_storeId, default);

        Assert.Empty(_shop.Requests);
        Assert.Null(ConnectionRow().LastSyncStartedAtUtc);
    }

    [Fact]
    public async Task Sync_AnExpiredLockFromACrashedRun_IsTakenOver()
    {
        Connect(_storeId, c => c.SyncLockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1));

        await Service().SyncStoreAsync(_storeId, default);

        Assert.True(ConnectionRow().LastSyncOk);
    }

    [Fact]
    public async Task Sync_WhenTheShopRejectsTheKeys_RecordsTheErrorAndReleasesTheLock()
    {
        Connect(_storeId);
        var rejecting = new FakeWooHandler(_ => FakeWooHandler.Unauthorized());

        await Service(shop: rejecting).SyncStoreAsync(_storeId, default);

        var c = ConnectionRow();
        Assert.False(c.LastSyncOk);
        Assert.Contains("rejected the API key", c.LastSyncMessage);
        Assert.Null(c.SyncLockedUntilUtc);
        Assert.Null(c.InitialSyncCompletedAtUtc);   // next run retries the full history
    }

    [Fact]
    public async Task Sync_WithUndecryptableKeys_FailsWithAClearMessage()
    {
        Connect(_storeId);

        await Service(protection: new EphemeralDataProtectionProvider()).SyncStoreAsync(_storeId, default);

        var c = ConnectionRow();
        Assert.False(c.LastSyncOk);
        Assert.Contains("can't be decrypted", c.LastSyncMessage);
        Assert.Empty(_shop.Requests);
    }

    [Fact]
    public async Task Sync_OfADeletedStore_DoesNothing()
    {
        Connect(_storeId);
        await new StoreService(_db.NewContext()).SoftDeleteAsync(Users.Owner, _storeId);

        await Service().SyncStoreAsync(_storeId, default);

        Assert.Empty(_shop.Requests);
    }

    // ------------------------------------------------------------------ entry points

    [Fact]
    public async Task RequestSync_AnyMemberCanQueueIt()
    {
        Connect(_storeId);

        HasStatus(202, await Service().RequestSyncAsync(Users.Member, _storeId));
        Assert.Equal(_storeId, Assert.Single(_jobs.Jobs).Args[0]);
    }

    [Fact]
    public async Task RequestSync_Refusals()
    {
        HasStatus(409, await Service().RequestSyncAsync(Users.Owner, _storeId));    // not connected yet
        Connect(_storeId, c => c.SyncLockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(5));
        HasStatus(409, await Service().RequestSyncAsync(Users.Owner, _storeId));    // already running
        HasStatus(404, await Service().RequestSyncAsync(Users.Stranger, _storeId));
        Assert.Empty(_jobs.Jobs);
    }

    [Fact]
    public async Task EnqueueAll_QueuesEveryConnectedStore_ExceptDeletedOnes()
    {
        Connect(_storeId);
        var deleted = _db.SeedStore(Users.Owner);
        Connect(deleted);
        _db.SeedStore(Users.Owner);   // never connected
        await new StoreService(_db.NewContext()).SoftDeleteAsync(Users.Owner, deleted);

        await Service().EnqueueAllAsync();

        Assert.Equal(_storeId, Assert.Single(_jobs.Jobs).Args[0]);
    }
}
