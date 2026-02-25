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

        if (!CanWrite(member.Role))
            return Results.StatusCode(403);

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
            .Where(s => s.OwnerSub == sub && s.IsDeleted)
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
            inv.Email,
            (int)inv.Role,
            (int)inv.Status,
            inv.CreatedAtUtc,
            inv.ExpiresAtUtc
        ), null);
    }

    // What: 
    // Why: 
    public async Task<List<InvitationDto>> ListMyInvitesAsync(string email)
    {
        var norm = NormalizeEmail(email);

        return await _db.StoreInvitations
            .Where(i => i.Email == norm && i.Status == InvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new InvitationDto(
                i.Id, i.StoreId, i.Email,
                (int)i.Role, (int)i.Status,
                i.CreatedAtUtc, i.ExpiresAtUtc
            ))
            .ToListAsync();
    }

    // What: 
    // Why: 
    public async Task<(StoreDto? store, IResult? error)> AcceptInviteAsync(string sub, string email, string token)
    {
        var normEmail = NormalizeEmail(email);
        token = (token ?? "").Trim();

        if (string.IsNullOrWhiteSpace(token))
            return (null, Results.Problem("Invalid token", "Token is required.", statusCode: 400));

        var inv = await _db.StoreInvitations.SingleOrDefaultAsync(i => i.Token == token);
        if (inv is null) return (null, Results.NotFound());

        if (inv.Status != InvitationStatus.Pending)
            return (null, Results.Problem("Invite not pending", "This invite is no longer available.", statusCode: 409));

        if (inv.ExpiresAtUtc is not null && inv.ExpiresAtUtc < DateTimeOffset.UtcNow)
            return (null, Results.Problem("Invite expired", "This invite has expired.", statusCode: 410));

        if (inv.Email != normEmail)
            return (null, Results.StatusCode(403));

        var alreadyMember = await _db.StoreMembers.AnyAsync(m => m.StoreId == inv.StoreId && m.UserSub == sub);

        if (!alreadyMember)
        {
            _db.StoreMembers.Add(new StoreMember
            {
                StoreId = inv.StoreId,
                UserSub = sub,
                Role = inv.Role,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        inv.Status = InvitationStatus.Accepted;
        inv.RespondedAtUtc = DateTimeOffset.UtcNow;
        inv.RespondedBySub = sub;

        await _db.SaveChangesAsync();

        var store = await GetAsync(sub, inv.StoreId);
        return (store, null);
    }

    public async Task<List<InvitationDebugDto>> ListMyInvitesDebugAsync(string email)
    {
        email = NormalizeEmail(email);

        return await _db.StoreInvitations
            .Where(i => i.Email == email && i.Status == InvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new InvitationDebugDto(
                i.Id,
                i.StoreId,
                i.Email,
                (int)i.Role,
                (int)i.Status,
                i.Token,
                i.CreatedAtUtc,
                i.ExpiresAtUtc
            ))
            .ToListAsync();
    }
}