using Microsoft.EntityFrameworkCore;

namespace WooPulse.Api.Data;

/// <summary>
/// What: shared query "what is MY membership in this store?".
/// Why:  almost every team/invite rule starts with it, in more than one service.
///       One definition means one place to get it right.
/// Note: the StoreMember query filter hides memberships of soft-deleted stores, so this
///       returns null for a deleted store, and callers answer 404 ("doesn't exist for you").
/// </summary>
public static class MembershipQueries
{
    public static Task<StoreMember?> FindMembershipAsync(this AppDbContext db, int storeId, string sub)
        => db.StoreMembers.SingleOrDefaultAsync(m => m.StoreId == storeId && m.UserSub == sub);
}
