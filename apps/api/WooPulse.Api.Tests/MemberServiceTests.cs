using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;
using WooPulse.Api.Dtos;
using WooPulse.Api.Services;
using WooPulse.Api.Tests.Infrastructure;
using static WooPulse.Api.Tests.Infrastructure.ResultAssert;

namespace WooPulse.Api.Tests;

/// <summary>The team rules from DEVLOG #010: who can list, change roles, remove and leave.</summary>
public sealed class MemberServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly int _storeId;

    public MemberServiceTests()
    {
        _storeId = _db.SeedStore(Users.Owner,
            (Users.Admin, StoreRole.Admin),
            (Users.Member, StoreRole.Member),
            ("auth0|member2", StoreRole.Member));
    }

    private MemberService Service() => new(_db.NewContext());

    private int IdOf(string sub)
    {
        using var db = _db.NewContext();
        return db.StoreMembers.Single(m => m.UserSub == sub).Id;
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task List_AnyMemberSeesTheTeam_OwnerFirst_WithTheirOwnRowMarked()
    {
        var (members, error) = await Service().ListAsync(Users.Member, _storeId);

        Assert.Null(error);
        Assert.Equal(4, members!.Count);
        Assert.Equal("Owner", members[0].Role);
        Assert.Equal(Users.Member, members.Single(m => m.IsMe).UserSub);
    }

    [Fact]
    public async Task List_Stranger_GetsNotFound()
    {
        var (_, error) = await Service().ListAsync(Users.Stranger, _storeId);

        HasStatus(404, error);
    }

    [Fact]
    public async Task ChangeRole_OwnerCanPromoteAMember()
    {
        var (member, error) = await Service().ChangeRoleAsync(Users.Owner, _storeId, IdOf(Users.Member), new ChangeRoleRequestDto(2));

        Assert.Null(error);
        Assert.Equal("Admin", member!.Role);
    }

    [Fact]
    public async Task ChangeRole_AdminsCant()
    {
        var (_, error) = await Service().ChangeRoleAsync(Users.Admin, _storeId, IdOf(Users.Member), new ChangeRoleRequestDto(2));

        HasStatus(403, error);
    }

    [Fact]
    public async Task ChangeRole_TheOwnersRoleCantChange_AndNobodyBecomesOwner()
    {
        var (_, demoteOwner) = await Service().ChangeRoleAsync(Users.Owner, _storeId, IdOf(Users.Owner), new ChangeRoleRequestDto(3));
        var (_, makeOwner) = await Service().ChangeRoleAsync(Users.Owner, _storeId, IdOf(Users.Member), new ChangeRoleRequestDto(1));
        var (_, notARole) = await Service().ChangeRoleAsync(Users.Owner, _storeId, IdOf(Users.Member), new ChangeRoleRequestDto(42));

        HasStatus(400, demoteOwner);
        HasStatus(400, makeOwner);
        HasStatus(400, notARole);
    }

    [Fact]
    public async Task ChangeRole_MemberOfAnotherStore_IsNotFound()
    {
        var other = _db.SeedStore(Users.Stranger, ("auth0|x", StoreRole.Member));

        var (_, error) = await Service().ChangeRoleAsync(Users.Owner, _storeId, IdOf("auth0|x"), new ChangeRoleRequestDto(2));

        HasStatus(404, error);
        Assert.NotEqual(other, _storeId);
    }

    [Fact]
    public async Task Remove_OwnerCanRemoveAnAdmin()
    {
        Assert.Null(await Service().RemoveAsync(Users.Owner, _storeId, IdOf(Users.Admin)));

        using var db = _db.NewContext();
        Assert.False(await db.StoreMembers.AnyAsync(m => m.UserSub == Users.Admin));
    }

    [Fact]
    public async Task Remove_AdminCanRemoveMembersOnly()
    {
        Assert.Null(await Service().RemoveAsync(Users.Admin, _storeId, IdOf(Users.Member)));
        HasStatus(403, await Service().RemoveAsync(Users.Admin, _storeId, IdOf(Users.Owner)));
    }

    [Fact]
    public async Task Remove_MembersCantRemoveAnyone_AndNobodyRemovesThemself()
    {
        HasStatus(403, await Service().RemoveAsync(Users.Member, _storeId, IdOf("auth0|member2")));
        HasStatus(400, await Service().RemoveAsync(Users.Owner, _storeId, IdOf(Users.Owner)));
    }

    [Fact]
    public async Task Leave_MembersCanLeave_ThenTheStoreIsInvisibleToThem()
    {
        Assert.Null(await Service().LeaveAsync(Users.Member, _storeId));

        var (_, error) = await Service().ListAsync(Users.Member, _storeId);
        HasStatus(404, error);
    }

    [Fact]
    public async Task Leave_TheOwnerCant_SoAStoreAlwaysHasAnOwner()
    {
        HasStatus(409, await Service().LeaveAsync(Users.Owner, _storeId));
    }
}
