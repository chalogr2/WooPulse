using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;
using WooPulse.Api.Security;
using WooPulse.Api.Services;
using WooPulse.Api.Tests.Infrastructure;
using WooPulse.Api.Validation;

namespace WooPulse.Api.Tests;

/// <summary>Reading the caller's identity from the Auth0 token.</summary>
public sealed class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void GetSub_ReadsSub_OrTheRemappedNameIdentifier()
    {
        Assert.Equal("auth0|a", User(("sub", "auth0|a")).GetSub());
        Assert.Equal("auth0|b", User((ClaimTypes.NameIdentifier, "auth0|b")).GetSub());
        Assert.Null(User().GetSub());
    }

    [Fact]
    public void GetEmail_PrefersTheCustomClaim_AndNormalizesIt()
    {
        var user = User(("email", "fallback@example.test"), ("https://woopulse.local/email", "  Me@Example.TEST "));

        Assert.Equal("me@example.test", user.GetEmail());
    }

    [Fact]
    public void GetEmail_FallsBackToStandardClaims_AndIgnoresBlankValues()
    {
        Assert.Equal("a@example.test", User(("email", "A@example.test")).GetEmail());
        Assert.Equal("b@example.test", User((ClaimTypes.Email, "b@example.test")).GetEmail());
        Assert.Null(User(("email", "   ")).GetEmail());
    }
}

public sealed class StoreValidationTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Shop  ", "Shop")]
    public void NormalizeName_TrimsAndTurnsBlankIntoNull(string? input, string? expected)
        => Assert.Equal(expected, StoreValidation.NormalizeName(input));

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void IsValidName_EnforcesTheLengthLimits(int length, bool valid)
        => Assert.Equal(valid, StoreValidation.IsValidName(new string('x', length)));
}

/// <summary>The scheduled clean-up jobs.</summary>
public sealed class MaintenanceServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ExpireInvites_OnlyTouchesPendingInvitesPastTheirDate()
    {
        var store = _db.SeedStore(Users.Owner);
        using (var db = _db.NewContext())
        {
            var past = DateTimeOffset.UtcNow.AddHours(-1);
            db.StoreInvitations.AddRange(
                new StoreInvitation { StoreId = store, Email = "a@x.test", Token = "a", ExpiresAtUtc = past },
                new StoreInvitation { StoreId = store, Email = "b@x.test", Token = "b", ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1) },
                new StoreInvitation { StoreId = store, Email = "c@x.test", Token = "c", ExpiresAtUtc = past, Status = InvitationStatus.Accepted });
            await db.SaveChangesAsync();
        }

        var count = await new MaintenanceService(_db.NewContext()).ExpireInvitesAsync();

        Assert.Equal(1, count);
        using var check = _db.NewContext();
        var byToken = await check.StoreInvitations.ToDictionaryAsync(i => i.Token, i => i.Status);
        Assert.Equal(InvitationStatus.Expired, byToken["a"]);
        Assert.Equal(InvitationStatus.Pending, byToken["b"]);
        Assert.Equal(InvitationStatus.Accepted, byToken["c"]);
    }

    [Fact]
    public async Task Purge_HardDeletesStoresInTheTrashForTooLong_WithEverythingThatBelongsToThem()
    {
        var old = _db.SeedStore(Users.Owner, (Users.Member, StoreRole.Member));
        var recent = _db.SeedStore(Users.Owner);
        var active = _db.SeedStore(Users.Owner);
        using (var db = _db.NewContext())
        {
            db.StoreInvitations.Add(new StoreInvitation { StoreId = old, Email = "x@x.test", Token = "t" });
            db.StoreConnections.Add(new StoreConnection { StoreId = old, ShopUrl = "https://s.test" });
            db.Orders.Add(new WooOrder { StoreId = old, WooOrderId = 1, Items = { new WooOrderItem { Name = "A", Quantity = 1 } } });
            await db.SaveChangesAsync();

            var stores = await db.Stores.Where(s => s.Id == old || s.Id == recent).ToListAsync();
            stores.Single(s => s.Id == old).DeletedAtUtc = DateTimeOffset.UtcNow.AddDays(-31);
            stores.Single(s => s.Id == recent).DeletedAtUtc = DateTimeOffset.UtcNow.AddDays(-5);
            stores.ForEach(s => s.IsDeleted = true);
            await db.SaveChangesAsync();
        }

        var purged = await new MaintenanceService(_db.NewContext()).PurgeSoftDeletedStoresAsync(30);

        Assert.Equal(1, purged);
        using var check = _db.NewContext();
        var remaining = await check.Stores.IgnoreQueryFilters().Select(s => s.Id).OrderBy(id => id).ToListAsync();
        Assert.Equal(new[] { recent, active }, remaining);
        Assert.False(await check.StoreMembers.IgnoreQueryFilters().AnyAsync(m => m.StoreId == old));
        Assert.False(await check.StoreInvitations.IgnoreQueryFilters().AnyAsync(i => i.StoreId == old));
        Assert.False(await check.StoreConnections.IgnoreQueryFilters().AnyAsync(c => c.StoreId == old));
        Assert.False(await check.Orders.IgnoreQueryFilters().AnyAsync(o => o.StoreId == old));
        Assert.False(await check.OrderItems.IgnoreQueryFilters().AnyAsync());
    }
}
