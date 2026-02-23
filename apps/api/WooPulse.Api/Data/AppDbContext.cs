using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Entities;

namespace WooPulse.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Store> Stores => Set<Store>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Store>()
            .HasQueryFilter(s => !s.IsDeleted);
    }
}

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    // Tenant ownership (who "owns" and can access)
    public string OwnerSub { get; set; } = "";

    // Audit
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBySub { get; set; } = "";

    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public string? UpdatedBySub { get; set; }

    // Soft delete
    public bool IsDeleted { get; set; } = false;
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public string? DeletedBySub { get; set; }
}
