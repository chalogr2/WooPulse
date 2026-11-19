using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;
using WooPulse.Api.Dtos;
using WooPulse.Api.Integrations.WooCommerce;
using WooPulse.Api.Services;
using WooPulse.Api.Tests.Infrastructure;
using static WooPulse.Api.Tests.Infrastructure.ResultAssert;

namespace WooPulse.Api.Tests;

/// <summary>
/// A store's WooCommerce connection: Owner-only changes, keys encrypted at rest and never
/// returned, data from two different shops never mixed.
/// </summary>
public sealed class ConnectionServiceTests : IDisposable
{
    private const string Key = "ck_live_key_abcd1234";
    private const string Secret = "cs_live_secret_9876";

    private readonly TestDb _db = new();
    private readonly FakeJobClient _jobs = new();
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
    private readonly int _storeId;

    // The fake shop accepts any request with a Basic header (i.e. the keys are fine).
    private readonly FakeWooHandler _shop = new(_ => FakeWooHandler.Ok("[]", ("X-WP-Total", "12")));

    public ConnectionServiceTests()
    {
        _storeId = _db.SeedStore(Users.Owner, (Users.Admin, StoreRole.Admin), (Users.Member, StoreRole.Member));
    }

    private ConnectionService Service(IDataProtectionProvider? protection = null) =>
        new(_db.NewContext(), new WooCommerceClient(new HttpClient(_shop)), protection ?? _protection, _jobs);

    private static SaveConnectionRequestDto Request(string url = "https://shop.example.test/") => new(url, Key, Secret);

    public void Dispose() => _db.Dispose();

    // ------------------------------------------------------------------ shop URL

    [Theory]
    [InlineData("https://shop.example.test/", "https://shop.example.test")]
    [InlineData("  https://Shop.Example.TEST  ", "https://shop.example.test")]
    [InlineData("https://site.example.test/shop/?utm=x#top", "https://site.example.test/shop")]
    [InlineData("http://shop.example.test", null)]     // keys travel in every request: HTTPS only
    [InlineData("shop.example.test", null)]
    [InlineData("ftp://shop.example.test", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeShopUrl(string? input, string? expected)
    {
        Assert.Equal(expected, ConnectionService.NormalizeShopUrl(input));
    }

    // ------------------------------------------------------------------ save

    [Fact]
    public async Task Save_StoresTheKeysEncrypted_AndReturnsOnlyAHint()
    {
        var (dto, error) = await Service().SaveAsync(Users.Owner, _storeId, Request());

        Assert.Null(error);
        Assert.Equal("https://shop.example.test", dto!.ShopUrl);
        Assert.Equal("ck_…1234", dto.KeyHint);

        // Nothing the API sends back contains the key or the secret.
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain(Key, json);
        Assert.DoesNotContain(Secret, json);

        // In the database: ciphertext, not the plain values, and it decrypts back correctly.
        using var db = _db.NewContext();
        var row = await db.StoreConnections.SingleAsync();
        Assert.DoesNotContain(Key, row.ConsumerKeyProtected);
        Assert.DoesNotContain(Secret, row.ConsumerSecretProtected);
        var creds = Service().TryUnprotect(row);
        Assert.Equal((Key, Secret), (creds!.ConsumerKey, creds.ConsumerSecret));
    }

    [Fact]
    public async Task Save_QueuesTheFirstSyncRightAway()
    {
        await Service().SaveAsync(Users.Owner, _storeId, Request());

        var job = Assert.Single(_jobs.Jobs);
        Assert.Equal(nameof(SyncService.SyncStoreAsync), job.Method.Name);
        Assert.Equal(_storeId, job.Args[0]);
    }

    [Theory]
    [InlineData(Users.Admin, 403)]
    [InlineData(Users.Member, 403)]
    [InlineData(Users.Stranger, 404)]
    public async Task Save_IsOwnerOnly_AndNobodyElseEvenReachesTheShop(string sub, int expected)
    {
        var (_, error) = await Service().SaveAsync(sub, _storeId, Request());

        HasStatus(expected, error);
        Assert.Empty(_shop.Requests);
    }

    [Theory]
    [InlineData("http://shop.example.test", Key, Secret)]
    [InlineData("https://shop.example.test", "wrong_prefix", Secret)]
    [InlineData("https://shop.example.test", Key, "wrong_prefix")]
    public async Task Save_InvalidInput_IsRejectedBeforeCallingTheShop(string url, string key, string secret)
    {
        var (_, error) = await Service().SaveAsync(Users.Owner, _storeId, new SaveConnectionRequestDto(url, key, secret));

        HasStatus(400, error);
        Assert.Empty(_shop.Requests);
    }

    [Fact]
    public async Task Save_KeysTheShopRejects_AreNeverSaved()
    {
        var rejectingShop = new FakeWooHandler(_ => FakeWooHandler.Unauthorized());
        var service = new ConnectionService(_db.NewContext(), new WooCommerceClient(new HttpClient(rejectingShop)), _protection, _jobs);

        var (_, error) = await service.SaveAsync(Users.Owner, _storeId, Request());

        HasStatus(400, error);
        using var db = _db.NewContext();
        Assert.False(await db.StoreConnections.AnyAsync());
        Assert.Empty(_jobs.Jobs);
    }

    [Fact]
    public async Task Save_ADifferentShop_WipesTheOldShopsOrders_SameShopKeepsThem()
    {
        await Service().SaveAsync(Users.Owner, _storeId, Request("https://shop.example.test"));
        AddOrder(1);

        await Service().SaveAsync(Users.Owner, _storeId, Request("https://shop.example.test/"));   // same shop, new keys
        Assert.Equal(1, OrderCount());

        await Service().SaveAsync(Users.Owner, _storeId, Request("https://other-shop.example.test"));
        Assert.Equal(0, OrderCount());
    }

    // ------------------------------------------------------------------ view, test, delete

    [Fact]
    public async Task Get_AnyMemberSeesTheStatus_StrangersGet404_UnconnectedIsNull()
    {
        var (before, beforeError) = await Service().GetAsync(Users.Member, _storeId);
        Assert.Null(before);
        Assert.Null(beforeError);

        await Service().SaveAsync(Users.Owner, _storeId, Request());

        var (dto, _) = await Service().GetAsync(Users.Member, _storeId);
        Assert.Equal("ck_…1234", dto!.KeyHint);
        var (_, stranger) = await Service().GetAsync(Users.Stranger, _storeId);
        HasStatus(404, stranger);
    }

    [Fact]
    public async Task Test_OwnerOrAdmin_ButNotMembers()
    {
        await Service().SaveAsync(Users.Owner, _storeId, Request());

        var (dto, adminError) = await Service().TestAsync(Users.Admin, _storeId);
        var (_, memberError) = await Service().TestAsync(Users.Member, _storeId);

        Assert.Null(adminError);
        Assert.True(dto!.LastTestOk);
        HasStatus(403, memberError);
    }

    [Fact]
    public async Task Test_WhenTheEncryptionKeysChanged_AsksForTheKeysAgain()
    {
        await Service().SaveAsync(Users.Owner, _storeId, Request());

        // A new provider = different Data Protection keys (e.g. the key folder was deleted).
        var (_, error) = await Service(new EphemeralDataProtectionProvider()).TestAsync(Users.Owner, _storeId);

        HasStatus(409, error);
    }

    [Fact]
    public async Task Delete_IsOwnerOnly_AndRemovesTheKeysAndTheSyncedOrders()
    {
        await Service().SaveAsync(Users.Owner, _storeId, Request());
        AddOrder(1);

        HasStatus(403, await Service().DeleteAsync(Users.Admin, _storeId));
        Assert.Null(await Service().DeleteAsync(Users.Owner, _storeId));

        using var db = _db.NewContext();
        Assert.False(await db.StoreConnections.AnyAsync());
        Assert.Equal(0, OrderCount());
    }

    private void AddOrder(long wooId)
    {
        using var db = _db.NewContext();
        db.Orders.Add(new WooOrder
        {
            StoreId = _storeId, WooOrderId = wooId, Status = "completed", Total = 10m,
            CreatedLocalDate = new DateOnly(2026, 11, 1),
            Items = { new WooOrderItem { WooProductId = 1, Name = "Item", Quantity = 1, Total = 10m } },
        });
        db.SaveChanges();
    }

    private int OrderCount()
    {
        using var db = _db.NewContext();
        return db.Orders.IgnoreQueryFilters().Count(o => o.StoreId == _storeId);
    }
}
