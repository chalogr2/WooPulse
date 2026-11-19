namespace WooPulse.Api.Data;

public enum StoreRole
{
    Owner = 1,
    Admin = 2,
    Member = 3
}

public class StoreMember
{
    public int Id { get; set; }

    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public string UserSub { get; set; } = "";

    // Added DEVLOG #009 so member lists can show a readable email instead of "auth0|65f...".
    // Nullable ("string?") because rows created before this column existed have no email
    // until the user's next GET /stores (StoreService.SyncMemberEmailAsync fills it).
    public string? Email { get; set; }

    public StoreRole Role { get; set; } = StoreRole.Member;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}