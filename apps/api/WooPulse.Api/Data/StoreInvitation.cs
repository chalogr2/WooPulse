namespace WooPulse.Api.Data;

public enum InvitationStatus
{
    Pending = 1,
    Accepted = 2,
    Revoked = 3,
    Expired = 4
}

public class StoreInvitation
{
    public int Id { get; set; }

    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public string Email { get; set; } = "";        // invited email (lowercased)
    public StoreRole Role { get; set; } = StoreRole.Member;

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    public string Token { get; set; } = "";        // random secret for accept link

    // audit
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBySub { get; set; } = "";

    public DateTimeOffset? RespondedAtUtc { get; set; }
    public string? RespondedBySub { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }
}