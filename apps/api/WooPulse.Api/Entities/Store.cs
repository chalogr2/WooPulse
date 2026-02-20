namespace WooPulse.Api.Entities;

public class Store
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
