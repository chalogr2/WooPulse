using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;
using WooPulse.Api.Dtos;
using WooPulse.Api.Validation;
using Microsoft.AspNetCore.Http;

namespace WooPulse.Api.Services;

public class StoreService
{
    private readonly AppDbContext _db;

    // Helpers
    private static string NormalizeEmail(string email)
    => (email ?? "").Trim().ToLowerInvariant();

    private static bool IsValidInviteRole(StoreRole role)
        => role == StoreRole.Admin || role == StoreRole.Member;

    private static string NewToken()
        => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public StoreService(AppDbContext db)
    {
        _db = db;
    }

    // What: consistent DTO mapping in ONE place
    // Why: prevents mismatched DTO constructor order and avoids EF leakage
    private static StoreDto ToDto(Store s) =>
        new StoreDto(
            s.Id,
            s.Name,
            s.OwnerSub,
            s.CreatedAtUtc,
            s.CreatedBySub,
            s.UpdatedAtUtc,
            s.UpdatedBySub,
            s.IsDeleted,
            s.DeletedAtUtc,
            s.DeletedBySub
        );

    // Helper for "can write" depending on user membership
    private static bool CanWrite(StoreRole role)
        => role == StoreRole.Owner || role == StoreRole.Admin;

    // What: list only stores owned by this user (soft delete filter applies automatically)
    // Why: multi-tenant isolation + no deleted stores by default
    public async Task<List<StoreDto>> ListAsync(string sub)
    {
        return await _db.Stores
            .Where(s => s.Members.Any(m => m.UserSub == sub))
            .OrderByDescending(s => s.Id)
            .Select(s => ToDto(s))
            .ToListAsync();
    }

    // What: get store by id if owned
    // Why: standard CRUD & prevents id-guessing
    public async Task<StoreDto?> GetAsync(string sub, int id)
    {
        return await _db.Stores
            .Where(s => s.Id == id && s.Members.Any(m => m.UserSub == sub))
            .Select(s => ToDto(s))
            .SingleOrDefaultAsync();
    }

    // What: create store with validation + audit fields
    // Why: consistent validation + creator/owner tracking
    public async Task<(StoreDto? dto, IResult? error)> CreateAsync(string sub, CreateStoreRequestDto req)
    {
        var name = StoreValidation.NormalizeName(req.Name);
        if (name is null || !StoreValidation.IsValidName(name))
        {
            return (null, Results.Problem(
                title: "Invalid store name",
                detail: $"Name must be {StoreValidation.NameMin}-{StoreValidation.NameMax} characters.",
                statusCode: 400
            ));
        }

        var store = new Store
        {
            Name = name,
            OwnerSub = sub,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBySub = sub
        };

         store.Members.Add(new StoreMember
        {
            UserSub = sub,
            Role = StoreRole.Owner
        });

        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        return (ToDto(store), null);
    }

    // What: rename store if owned + validate + set audit fields
    // Why: consistent rename rules and audit history
    public async Task<(StoreDto? dto, IResult? error)> RenameAsync(string sub, int id, RenameStoreRequestDto req)
    {
        var name = StoreValidation.NormalizeName(req.Name);
        if (name is null || !StoreValidation.IsValidName(name))
        {
            return (null, Results.Problem(
                title: "Invalid store name",
                detail: $"Name must be {StoreValidation.NameMin}-{StoreValidation.NameMax} characters.",
                statusCode: 400
            ));
        }

        var store = await _db.Stores
            .Include(s => s.Members)
            .SingleOrDefaultAsync(s => s.Id == id);

        if (store is null) return (null, Results.NotFound());

        var member = store.Members.SingleOrDefault(m => m.UserSub == sub);
        if (member is null) return (null, Results.NotFound()); // don't leak existence

        if (!CanWrite(member.Role))
            return (null, Results.StatusCode(403));

        store.Name = name;
        store.UpdatedAtUtc = DateTimeOffset.UtcNow;
        store.UpdatedBySub = sub;

        await _db.SaveChangesAsync();
        return (ToDto(store), null);
    }

    // What: soft delete store if owned + audit deletion
    // Why: real SaaS behavior (recoverable + auditable)
    public async Task<IResult?> SoftDeleteAsync(string sub, int id)
    {
        var store = await _db.Stores
            .Include(s => s.Members)
            .SingleOrDefaultAsync(s => s.Id == id);

        if (store is null) return Results.NotFound();

        var member = store.Members.SingleOrDefault(m => m.UserSub == sub);
        if (member is null) return Results.NotFound();

        // Owner-only (DEVLOG #008). Admins can rename/invite but not delete.
        // Results.Problem(...) instead of a bare 403 so the UI can show *why*.
        if (member.Role != StoreRole.Owner)
            return Results.Problem(
                title: "Owner only",
                detail: "Only the store owner can delete a store.",
                statusCode: 403);

        store.IsDeleted = true;
        store.DeletedAtUtc = DateTimeOffset.UtcNow;
        store.DeletedBySub = sub;

        await _db.SaveChangesAsync();
        return null;
    }

    // What: list deleted stores (explicitly ignoring query filter)
    // Why: lets UI show a “Trash” view later
    public async Task<List<StoreDto>> ListDeletedAsync(string sub)
    {
        return await _db.Stores
            .IgnoreQueryFilters()
            .Where(s => s.IsDeleted && s.Members.Any(m => m.UserSub == sub))
            .OrderByDescending(s => s.DeletedAtUtc)
            .Select(s => ToDto(s))
            .ToListAsync();
    }

    // What: 
    // Why: 
    public async Task<(InvitationDto? dto, IResult? error)> InviteAsync(string sub, int storeId, InviteRequestDto req)
    {
        var email = NormalizeEmail(req.Email);
        if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
        {
            return (null, Results.Problem("Invalid email", "Provide a valid email.", statusCode: 400));
        }

        if (!Enum.IsDefined(typeof(StoreRole), req.Role))
        {
            return (null, Results.Problem("Invalid role", "Role must be Admin (2) or Member (3).", statusCode: 400));
        }
        var role = (StoreRole)req.Role;
        if (!IsValidInviteRole(role))
        {
            return (null, Results.Problem("Invalid role", "Role must be Admin (2) or Member (3).", statusCode: 400));
        }

        // must be Owner/Admin to invite
        var store = await _db.Stores
            .Include(s => s.Members)
            .SingleOrDefaultAsync(s => s.Id == storeId);

        if (store is null) return (null, Results.NotFound());

        var member = store.Members.SingleOrDefault(m => m.UserSub == sub);
        if (member is null) return (null, Results.NotFound()); // don't leak store existence

        if (!CanWrite(member.Role)) return (null, Results.StatusCode(403));

        // block duplicate pending invites for the same store+email
        var exists = await _db.StoreInvitations.AnyAsync(i =>
            i.StoreId == storeId &&
            i.Email == email &&
            i.Status == InvitationStatus.Pending);

        if (exists)
        {
            return (null, Results.Problem(
                title: "Already invited",
                detail: "There is already a pending invitation for this email.",
                statusCode: 409
            ));
        }

        var inv = new StoreInvitation
        {
            StoreId = storeId,
            Email = email,
            Role = role,
            Status = InvitationStatus.Pending,
            Token = NewToken(),
            CreatedBySub = sub,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        _db.StoreInvitations.Add(inv);
        await _db.SaveChangesAsync();

        return (new InvitationDto(
            inv.Id,
            inv.StoreId,
            store.Name,
            inv.Email,
            inv.Role.ToString(),
            inv.Status.ToString(),
            inv.CreatedAtUtc,
            inv.ExpiresAtUtc
        ), null);
    }

    // What: pending, NOT-yet-expired invites for my email, with the store's name
    // Why: the hourly "expire-invites" job can lag up to an hour, so we also filter by
    //      ExpiresAtUtc here. Store name lets the UI show "Acme Shop" instead of "Store #12".
    // How: .Select(...) runs in SQL, so EF only fetches the columns the DTO needs.
    //      i.Store.Name follows the navigation property; EF turns it into a JOIN.
    public async Task<List<InvitationDto>> ListMyInvitesAsync(string email)
    {
        var norm = NormalizeEmail(email);
        var now = DateTimeOffset.UtcNow;

        return await _db.StoreInvitations
            .Where(i => i.Email == norm
                     && i.Status == InvitationStatus.Pending
                     && (i.ExpiresAtUtc == null || i.ExpiresAtUtc > now))
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new InvitationDto(
                i.Id, i.StoreId, i.Store.Name, i.Email,
                i.Role.ToString(), i.Status.ToString(),
                i.CreatedAtUtc, i.ExpiresAtUtc))
            .ToListAsync();
    }

    // What: accept an invite, add member row + mark invite accepted atomically
    // Why: two writes must succeed or fail together (transaction).
    // Race safety: a transaction alone does NOT stop two simultaneous accepts. At Postgres's
    //      default "Read Committed" isolation both requests can read Status == Pending.
    //      So we "claim" the invite with a conditional UPDATE (see below); only one request
    //      can flip Pending → Accepted, the other gets 0 rows and a clean 409. (DEVLOG #008)
    public async Task<(StoreDto? store, IResult? error)> AcceptInviteAsync(string sub, string email, string token)
    {
        var normEmail = NormalizeEmail(email);
        token = (token ?? "").Trim();

        if (string.IsNullOrWhiteSpace(token))
            return (null, Results.Problem("Invalid token", "Token is required.", statusCode: 400));

        await using var tx = await _db.Database.BeginTransactionAsync();

        var inv = await _db.StoreInvitations.SingleOrDefaultAsync(i => i.Token == token);
        if (inv is null) return (null, Results.NotFound());

        if (inv.Status != InvitationStatus.Pending)
            return (null, Results.Problem("Invite not pending", "This invite is no longer available.", statusCode: 409));

        if (inv.ExpiresAtUtc is not null && inv.ExpiresAtUtc < DateTimeOffset.UtcNow)
            return (null, Results.Problem("Invite expired", "This invite has expired.", statusCode: 410));

        if (inv.Email != normEmail)
            return (null, Results.StatusCode(403));

        // Atomic claim. ExecuteUpdateAsync sends ONE SQL statement straight to the DB:
        //   UPDATE "StoreInvitations" SET "Status" = 2, ... WHERE "Id" = @id AND "Status" = 1
        // If another request already accepted it, Postgres makes us wait for its row lock,
        // re-checks the WHERE, finds Status = 2, and updates 0 rows.
        // IgnoreQueryFilters(): we already loaded this invite through the normal filter
        // (store not deleted), so skipping it keeps the UPDATE a simple single-table statement.
        var now = DateTimeOffset.UtcNow;
        var claimed = await _db.StoreInvitations
            .IgnoreQueryFilters()
            .Where(i => i.Id == inv.Id && i.Status == InvitationStatus.Pending)
            .ExecuteUpdateAsync(set => set
                .SetProperty(i => i.Status, InvitationStatus.Accepted)
                .SetProperty(i => i.RespondedAtUtc, now)
                .SetProperty(i => i.RespondedBySub, sub));

        if (claimed == 0)
            // Returning without CommitAsync → "await using" disposes tx → automatic rollback.
            return (null, Results.Problem("Invite not pending", "This invite was already used.", statusCode: 409));

        var alreadyMember = await _db.StoreMembers.AnyAsync(m => m.StoreId == inv.StoreId && m.UserSub == sub);

        if (!alreadyMember)
        {
            _db.StoreMembers.Add(new StoreMember
            {
                StoreId = inv.StoreId,
                UserSub = sub,
                Role = inv.Role,
                CreatedAtUtc = now
            });
        }

        // Invite status was already updated by the claim above; this only inserts the member.
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var store = await GetAsync(sub, inv.StoreId);
        return (store, null);
    }

    // What: same as ListMyInvitesAsync but includes the token (Development only; this is
    //       what the local UI's "Accept this invite" button uses, see DEVLOG #002).
    public async Task<List<InvitationDebugDto>> ListMyInvitesDebugAsync(string email)
    {
        email = NormalizeEmail(email);
        var now = DateTimeOffset.UtcNow;

        return await _db.StoreInvitations
            .Where(i => i.Email == email
                     && i.Status == InvitationStatus.Pending
                     && (i.ExpiresAtUtc == null || i.ExpiresAtUtc > now))
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new InvitationDebugDto(
                i.Id,
                i.StoreId,
                i.Store.Name,
                i.Email,
                i.Role.ToString(),
                i.Status.ToString(),
                i.Token,
                i.CreatedAtUtc,
                i.ExpiresAtUtc))
            .ToListAsync();
    }
}