using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;
using WooPulse.Api.Dtos;
using WooPulse.Api.Services;
using WooPulse.Api.Tests.Infrastructure;
using static WooPulse.Api.Tests.Infrastructure.ResultAssert;

namespace WooPulse.Api.Tests;

/// <summary>Stores: creation, tenant isolation, owner/admin/member permissions, soft delete.</summary>
public sealed class StoreServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    private StoreService Service() => new(_db.NewContext());

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_TrimsTheName_AndMakesTheCreatorOwner()
    {
        var (dto, error) = await Service().CreateAsync(Users.Owner, "owner@example.test", new CreateStoreRequestDto("  Diseño  "));

        Assert.Null(error);
        Assert.Equal("Diseño", dto!.Name);
        Assert.Equal("Owner", dto.MyRole);

        using var db = _db.NewContext();
        var member = await db.StoreMembers.SingleAsync();
        Assert.Equal((Users.Owner, StoreRole.Owner, "owner@example.test"), (member.UserSub, member.Role, member.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]                       // 1 character, minimum is 2
    [InlineData(" A ")]                     // trimmed to 1 character
    public async Task Create_RejectsNamesOutsideTheLimits(string name)
    {
        var (dto, error) = await Service().CreateAsync(Users.Owner, null, new CreateStoreRequestDto(name));

        Assert.Null(dto);
        HasStatus(400, error);
    }

    [Fact]
    public async Task Create_RejectsANameLongerThan60Characters()
    {
        var (_, ok) = await Service().CreateAsync(Users.Owner, null, new CreateStoreRequestDto(new string('x', 60)));
        var (_, tooLong) = await Service().CreateAsync(Users.Owner, null, new CreateStoreRequestDto(new string('x', 61)));

        Assert.Null(ok);
        HasStatus(400, tooLong);
    }

    [Fact]
    public async Task List_ReturnsOnlyStoresImIn_WithMyRoleInEach()
    {
        var mine = _db.SeedStore(Users.Owner);
        var joined = _db.SeedStore(Users.Admin, (Users.Owner, StoreRole.Member));
        _db.SeedStore(Users.Stranger);   // someone else's tenant

        var stores = await Service().ListAsync(Users.Owner);

        Assert.Equal(2, stores.Count);
        Assert.Equal("Owner", stores.Single(s => s.Id == mine).MyRole);
        Assert.Equal("Member", stores.Single(s => s.Id == joined).MyRole);
    }

    [Fact]
    public async Task Get_AnotherTenantsStore_IsNotFound()
    {
        var theirs = _db.SeedStore(Users.Stranger);

        Assert.Null(await Service().GetAsync(Users.Owner, theirs));
    }

    [Fact]
    public async Task Rename_OwnerAndAdminCan_MemberCant_StrangerGets404()
    {
        var id = _db.SeedStore(Users.Owner, (Users.Admin, StoreRole.Admin), (Users.Member, StoreRole.Member));

        var (byAdmin, adminError) = await Service().RenameAsync(Users.Admin, id, new RenameStoreRequestDto("Renamed"));
        var (_, memberError) = await Service().RenameAsync(Users.Member, id, new RenameStoreRequestDto("Nope"));
        var (_, strangerError) = await Service().RenameAsync(Users.Stranger, id, new RenameStoreRequestDto("Nope"));

        Assert.Null(adminError);
        Assert.Equal("Renamed", byAdmin!.Name);
        Assert.Equal(Users.Admin, byAdmin.UpdatedBySub);
        HasStatus(403, memberError);
        HasStatus(404, strangerError);   // 404, not 403: don't confirm the store exists
    }

    [Fact]
    public async Task SoftDelete_IsOwnerOnly()
    {
        var id = _db.SeedStore(Users.Owner, (Users.Admin, StoreRole.Admin));

        HasStatus(403, await Service().SoftDeleteAsync(Users.Admin, id));
        HasStatus(404, await Service().SoftDeleteAsync(Users.Stranger, id));
        Assert.NotNull(await Service().GetAsync(Users.Owner, id));
    }

    [Fact]
    public async Task SoftDelete_HidesTheStoreFromEveryone_AndRestoreBringsItBack()
    {
        var id = _db.SeedStore(Users.Owner, (Users.Member, StoreRole.Member));

        Assert.Null(await Service().SoftDeleteAsync(Users.Owner, id));

        Assert.Empty(await Service().ListAsync(Users.Owner));
        Assert.Empty(await Service().ListAsync(Users.Member));
        var trash = Assert.Single(await Service().ListDeletedAsync(Users.Owner));
        Assert.True(trash.IsDeleted);
        Assert.Equal(Users.Owner, trash.DeletedBySub);

        var (restored, error) = await Service().RestoreAsync(Users.Owner, id);

        Assert.Null(error);
        Assert.False(restored!.IsDeleted);
        Assert.Null(restored.DeletedAtUtc);
        Assert.Single(await Service().ListAsync(Users.Member));
    }

    [Fact]
    public async Task Restore_IsOwnerOnly_AndOnlyForDeletedStores()
    {
        var id = _db.SeedStore(Users.Owner, (Users.Admin, StoreRole.Admin));

        var (_, notDeleted) = await Service().RestoreAsync(Users.Owner, id);
        HasStatus(404, notDeleted);

        await Service().SoftDeleteAsync(Users.Owner, id);
        var (_, byAdmin) = await Service().RestoreAsync(Users.Admin, id);
        HasStatus(403, byAdmin);
    }

    // ------------------------------------------------------------------ invitations

    private async Task<string> TokenOfAsync(int inviteId)
    {
        using var db = _db.NewContext();
        return (await db.StoreInvitations.IgnoreQueryFilters().SingleAsync(i => i.Id == inviteId)).Token;
    }

    private async Task<InvitationDto> InviteAsync(int storeId, string email, StoreRole role = StoreRole.Member)
    {
        var (dto, error) = await Service().InviteAsync(Users.Owner, storeId, new InviteRequestDto(email, (int)role));
        Assert.Null(error);
        return dto!;
    }

    [Fact]
    public async Task Invite_NormalizesTheEmail_AndExpiresInSevenDays()
    {
        var id = _db.SeedStore(Users.Owner);

        var invite = await InviteAsync(id, "  New.Person@Example.TEST ");

        Assert.Equal("new.person@example.test", invite.Email);
        Assert.Equal("Pending", invite.Status);
        Assert.Equal("Test shop", invite.StoreName);
        Assert.InRange(invite.ExpiresAtUtc!.Value - invite.CreatedAtUtc, TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1), TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));
        Assert.Equal(64, (await TokenOfAsync(invite.Id)).Length);   // 32 random bytes as hex
    }

    [Theory]
    [InlineData("not-an-email", 3)]
    [InlineData("", 3)]
    [InlineData("a@b.test", 1)]     // can't invite a second Owner
    [InlineData("a@b.test", 99)]    // not a role at all
    public async Task Invite_RejectsBadInput(string email, int role)
    {
        var id = _db.SeedStore(Users.Owner);

        var (_, error) = await Service().InviteAsync(Users.Owner, id, new InviteRequestDto(email, role));

        HasStatus(400, error);
    }

    [Fact]
    public async Task Invite_MembersCant_StrangersGet404()
    {
        var id = _db.SeedStore(Users.Owner, (Users.Member, StoreRole.Member));

        var (_, member) = await Service().InviteAsync(Users.Member, id, new InviteRequestDto("x@example.test", 3));
        var (_, stranger) = await Service().InviteAsync(Users.Stranger, id, new InviteRequestDto("x@example.test", 3));

        HasStatus(403, member);
        HasStatus(404, stranger);
    }

    [Fact]
    public async Task Invite_SameEmailTwiceWhilePending_IsAConflict()
    {
        var id = _db.SeedStore(Users.Owner);
        await InviteAsync(id, "x@example.test");

        var (_, error) = await Service().InviteAsync(Users.Owner, id, new InviteRequestDto("X@example.test", 3));

        HasStatus(409, error);
    }

    [Fact]
    public async Task Accept_AddsMeWithTheInvitedRole_AndMarksTheInviteAccepted()
    {
        var id = _db.SeedStore(Users.Owner);
        var invite = await InviteAsync(id, Users.EmailOf(Users.Admin), StoreRole.Admin);

        var (store, error) = await Service().AcceptInviteAsync(Users.Admin, Users.EmailOf(Users.Admin), await TokenOfAsync(invite.Id));

        Assert.Null(error);
        Assert.Equal("Admin", store!.MyRole);
        using var db = _db.NewContext();
        var inv = await db.StoreInvitations.SingleAsync();
        Assert.Equal(InvitationStatus.Accepted, inv.Status);
        Assert.Equal(Users.Admin, inv.RespondedBySub);
    }

    [Fact]
    public async Task Accept_TheSameInviteTwice_SecondIsAConflict_AndAddsNoDuplicate()
    {
        var id = _db.SeedStore(Users.Owner);
        var token = await TokenOfAsync((await InviteAsync(id, Users.EmailOf(Users.Member))).Id);

        await Service().AcceptInviteAsync(Users.Member, Users.EmailOf(Users.Member), token);
        var (_, second) = await Service().AcceptInviteAsync(Users.Member, Users.EmailOf(Users.Member), token);

        HasStatus(409, second);
        using var db = _db.NewContext();
        Assert.Equal(1, await db.StoreMembers.CountAsync(m => m.UserSub == Users.Member));
    }

    [Fact]
    public async Task Accept_WithSomeoneElsesToken_IsForbidden_AndTheInviteStaysPending()
    {
        var id = _db.SeedStore(Users.Owner);
        var token = await TokenOfAsync((await InviteAsync(id, Users.EmailOf(Users.Member))).Id);

        var (_, error) = await Service().AcceptInviteAsync(Users.Stranger, Users.EmailOf(Users.Stranger), token);

        HasStatus(403, error);
        using var db = _db.NewContext();
        Assert.Equal(InvitationStatus.Pending, (await db.StoreInvitations.SingleAsync()).Status);
        Assert.False(await db.StoreMembers.AnyAsync(m => m.UserSub == Users.Stranger));
    }

    [Fact]
    public async Task Accept_ExpiredInvite_IsGone()
    {
        var id = _db.SeedStore(Users.Owner);
        var invite = await InviteAsync(id, Users.EmailOf(Users.Member));
        using (var db = _db.NewContext())
        {
            var inv = await db.StoreInvitations.SingleAsync();
            inv.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var (_, error) = await Service().AcceptInviteAsync(Users.Member, Users.EmailOf(Users.Member), await TokenOfAsync(invite.Id));

        HasStatus(410, error);
    }

    [Theory]
    [InlineData("", 400)]
    [InlineData("   ", 400)]
    [InlineData("DOESNOTEXIST", 404)]
    public async Task Accept_MissingOrUnknownToken(string token, int expected)
    {
        var (_, error) = await Service().AcceptInviteAsync(Users.Member, Users.EmailOf(Users.Member), token);

        HasStatus(expected, error);
    }

    [Fact]
    public async Task Accept_InviteToADeletedStore_IsNotFound()
    {
        var id = _db.SeedStore(Users.Owner);
        var token = await TokenOfAsync((await InviteAsync(id, Users.EmailOf(Users.Member))).Id);
        await Service().SoftDeleteAsync(Users.Owner, id);

        var (_, error) = await Service().AcceptInviteAsync(Users.Member, Users.EmailOf(Users.Member), token);

        HasStatus(404, error);
    }

    [Fact]
    public async Task ListMyInvites_OnlyPendingUnexpiredInvitesForMyEmail()
    {
        var id = _db.SeedStore(Users.Owner);
        await InviteAsync(id, Users.EmailOf(Users.Member));
        await InviteAsync(id, "someone.else@example.test");
        var expiredStore = _db.SeedStore(Users.Owner);
        var expired = await InviteAsync(expiredStore, Users.EmailOf(Users.Member));
        using (var db = _db.NewContext())
        {
            (await db.StoreInvitations.SingleAsync(i => i.Id == expired.Id)).ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        var mine = await Service().ListMyInvitesAsync(" MEMBER@example.test ");

        var only = Assert.Single(mine);
        Assert.Equal(id, only.StoreId);
    }

    [Fact]
    public async Task Revoke_StopsTheTokenWorking_AndCantRevokeTwice()
    {
        var id = _db.SeedStore(Users.Owner, (Users.Member, StoreRole.Member));
        var invite = await InviteAsync(id, "new@example.test");

        HasStatus(403, await Service().RevokeInviteAsync(Users.Member, id, invite.Id));
        Assert.Null(await Service().RevokeInviteAsync(Users.Owner, id, invite.Id));
        HasStatus(409, await Service().RevokeInviteAsync(Users.Owner, id, invite.Id));

        var (_, accept) = await Service().AcceptInviteAsync("auth0|new", "new@example.test", await TokenOfAsync(invite.Id));
        HasStatus(409, accept);
    }

    [Fact]
    public async Task Revoke_AnAcceptedInvite_IsAConflict()
    {
        var id = _db.SeedStore(Users.Owner);
        var invite = await InviteAsync(id, Users.EmailOf(Users.Member));
        await Service().AcceptInviteAsync(Users.Member, Users.EmailOf(Users.Member), await TokenOfAsync(invite.Id));

        HasStatus(409, await Service().RevokeInviteAsync(Users.Owner, id, invite.Id));
    }

    [Fact]
    public async Task Revoke_AnInviteOfAnotherStore_IsNotFound()
    {
        var mine = _db.SeedStore(Users.Owner);
        var theirs = _db.SeedStore(Users.Stranger);
        var (theirInvite, _) = await Service().InviteAsync(Users.Stranger, theirs, new InviteRequestDto("x@example.test", 3));

        HasStatus(404, await Service().RevokeInviteAsync(Users.Owner, mine, theirInvite!.Id));
    }
}
