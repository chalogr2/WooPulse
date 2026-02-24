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

    public StoreRole Role { get; set; } = StoreRole.Member;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}