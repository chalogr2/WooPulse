using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;
using WooPulse.Api.Dtos;

namespace WooPulse.Api.Services;

/// <summary>
/// What: business rules for a store's team (list, change role, remove, leave).
/// Why a separate service: "members" is its own concern within the Store domain.
///      StoreService was already large, and these rules change for different reasons.
///
/// Permission rules (approved by the owner, DEVLOG #010):
///   list members  → any member
///   change role   → Owner only; can't change the Owner; new role must be Admin or Member
///   remove member → Owner removes anyone but themself; Admin removes Members only
///   leave store   → anyone except the Owner
///
/// Return style: same as StoreService, a tuple (result, error). Exactly one is non-null.
/// "error" is an IResult (404/403/...) the endpoint returns as-is.
/// </summary>
public class MemberService
{
    private readonly AppDbContext _db;

    // Expression-bodied constructor: same as { _db = db; }
    public MemberService(AppDbContext db) => _db = db;

    private static MemberDto ToDto(StoreMember m, string mySub) =>
        new(m.Id, m.UserSub, m.Email, m.Role.ToString(), m.CreatedAtUtc, m.UserSub == mySub);
    //  ^ "new(...)" = target-typed new: the compiler knows the type from the return type.

    public async Task<(List<MemberDto>? members, IResult? error)> ListAsync(string sub, int storeId)
    {
        // Not a member (or the store doesn't exist / is deleted) → 404, never 403:
        // a 403 would confirm the store exists to someone who isn't in it.
        var me = await _db.FindMembershipAsync(storeId, sub);
        if (me is null) return (null, Results.NotFound());

        var members = await _db.StoreMembers
            .Where(m => m.StoreId == storeId)
            .OrderBy(m => m.Role)              // enum ints: Owner(1), Admin(2), Member(3)
            .ThenBy(m => m.CreatedAtUtc)       // then by join date
            .Select(m => new MemberDto(m.Id, m.UserSub, m.Email, m.Role.ToString(), m.CreatedAtUtc, m.UserSub == sub))
            .ToListAsync();

        return (members, null);
    }

    public async Task<(MemberDto? member, IResult? error)> ChangeRoleAsync(string sub, int storeId, int memberId, ChangeRoleRequestDto req)
    {
        // Validate input first (cheapest check, no DB call).
        // "is not (A or B)" is C# 9 pattern matching: true unless the value is A or B.
        var newRole = (StoreRole)req.Role;
        if (newRole is not (StoreRole.Admin or StoreRole.Member))
            return (null, Results.Problem("Invalid role", "Role must be Admin (2) or Member (3).", statusCode: 400));

        var me = await _db.FindMembershipAsync(storeId, sub);
        if (me is null) return (null, Results.NotFound());

        if (me.Role != StoreRole.Owner)
            return (null, Results.Problem("Owner only", "Only the store owner can change roles.", statusCode: 403));

        var target = await _db.StoreMembers.SingleOrDefaultAsync(m => m.Id == memberId && m.StoreId == storeId);
        if (target is null) return (null, Results.NotFound());

        if (target.Role == StoreRole.Owner)
            return (null, Results.Problem("Not allowed", "The owner's role can't be changed.", statusCode: 400));

        // "target" is TRACKED by EF (loaded without AsNoTracking), so changing a property
        // and calling SaveChangesAsync generates: UPDATE "StoreMembers" SET "Role" = @p WHERE "Id" = @id
        target.Role = newRole;
        await _db.SaveChangesAsync();

        return (ToDto(target, sub), null);
    }

    public async Task<IResult?> RemoveAsync(string sub, int storeId, int memberId)
    {
        var me = await _db.FindMembershipAsync(storeId, sub);
        if (me is null) return Results.NotFound();

        var target = await _db.StoreMembers.SingleOrDefaultAsync(m => m.Id == memberId && m.StoreId == storeId);
        if (target is null) return Results.NotFound();

        if (target.Id == me.Id)
            return Results.Problem("Not allowed", "You can't remove yourself. Use \"Leave store\" instead.", statusCode: 400);

        // switch EXPRESSION (C# 8): picks a value based on me.Role.
        // "_" is the discard pattern = "any other value" (here: Member).
        var allowed = me.Role switch
        {
            StoreRole.Owner => true,                              // anyone except self (checked above)
            StoreRole.Admin => target.Role == StoreRole.Member,   // Members only
            _ => false
        };

        if (!allowed)
            return Results.Problem("Not allowed", "Owners can remove anyone; Admins can remove Members only.", statusCode: 403);

        _db.StoreMembers.Remove(target);
        await _db.SaveChangesAsync();
        return null;
    }

    public async Task<IResult?> LeaveAsync(string sub, int storeId)
    {
        var me = await _db.FindMembershipAsync(storeId, sub);
        if (me is null) return Results.NotFound();

        // A store must always have an owner. (Transferring ownership is out of MVP scope.)
        if (me.Role == StoreRole.Owner)
            return Results.Problem("Owner can't leave", "The owner can't leave the store. Delete the store instead.", statusCode: 409);

        _db.StoreMembers.Remove(me);
        await _db.SaveChangesAsync();
        return null;
    }
}
