using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;

namespace WooPulse.Api.Services;

public class MaintenanceService
{
    private readonly AppDbContext _db;

    public MaintenanceService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// What: Mark expired invites as Expired.
    /// Why: Invitations shouldn't stay "Pending" forever once ExpiresAtUtc passes.
    /// </summary>
    public async Task<int> ExpireInvitesAsync()
    {
        var now = DateTimeOffset.UtcNow;

        // Load pending invites that have expired
        var expired = await _db.StoreInvitations
            .Where(i => i.Status == InvitationStatus.Pending
                     && i.ExpiresAtUtc != null
                     && i.ExpiresAtUtc < now)
            .ToListAsync();

        foreach (var inv in expired)
        {
            inv.Status = InvitationStatus.Expired;
            // Optional: track responded metadata if your model allows it.
            // inv.RespondedAtUtc = now;
            // inv.RespondedBySub = "system";
        }

        await _db.SaveChangesAsync();
        return expired.Count;
    }

    /// <summary>
    /// What: Permanently delete stores that were soft-deleted more than N days ago.
    /// Why: Keeps DB clean and prevents soft-deleted data from growing forever.
    ///
    /// IMPORTANT: This is a *hard delete*, so we delete dependent rows too
    /// (StoreMembers + StoreInvitations) to avoid FK errors.
    /// </summary>
    public async Task<int> PurgeSoftDeletedStoresAsync(int olderThanDays)
    {
        if (olderThanDays < 1) olderThanDays = 1;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-olderThanDays);

        // We must ignore query filters because soft-deleted stores are filtered out by default
        var storesToPurge = await _db.Stores
            .IgnoreQueryFilters()
            .Where(s => s.IsDeleted && s.DeletedAtUtc != null && s.DeletedAtUtc < cutoff)
            .Select(s => s.Id)
            .ToListAsync();

        if (storesToPurge.Count == 0)
            return 0;

        // Transaction because we are deleting multiple tables that must stay consistent
        await using var tx = await _db.Database.BeginTransactionAsync();

        // Delete dependent rows first (avoid FK constraint issues)
        await _db.StoreMembers
            .Where(m => storesToPurge.Contains(m.StoreId))
            .ExecuteDeleteAsync();

        await _db.StoreInvitations
            .Where(i => storesToPurge.Contains(i.StoreId))
            .ExecuteDeleteAsync();

        // Now delete the stores
        var deletedStores = await _db.Stores
            .IgnoreQueryFilters()
            .Where(s => storesToPurge.Contains(s.Id))
            .ExecuteDeleteAsync();

        await tx.CommitAsync();
        return deletedStores;
    }
}