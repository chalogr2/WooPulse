using Microsoft.EntityFrameworkCore;

namespace WooPulse.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreInvitation> StoreInvitations => Set<StoreInvitation>();
    public DbSet<StoreMember> StoreMembers => Set<StoreMember>(); 

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Store>()
            .HasQueryFilter(s => !s.IsDeleted);

        modelBuilder.Entity<StoreMember>()
            .HasQueryFilter(m => !m.Store.IsDeleted);

        modelBuilder.Entity<StoreInvitation>()
            .HasQueryFilter(i => !i.Store.IsDeleted);

        modelBuilder.Entity<StoreMember>()
            .HasIndex(m => new { m.StoreId, m.UserSub })
            .IsUnique();

        modelBuilder.Entity<StoreMember>()
            .HasIndex(m => m.UserSub);

        modelBuilder.Entity<StoreMember>()
            .Property(m => m.Role)
            .HasConversion<int>();
        
        modelBuilder.Entity<StoreInvitation>()
            .HasIndex(i => i.Token)
            .IsUnique();

        modelBuilder.Entity<StoreInvitation>()
            .HasIndex(i => new { i.StoreId, i.Email, i.Status });

        modelBuilder.Entity<StoreInvitation>()
            .Property(i => i.Role)
            .HasConversion<int>();

        modelBuilder.Entity<StoreInvitation>()
            .Property(i => i.Status)
            .HasConversion<int>();
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
     public List<StoreMember> Members { get; set; } = new();
}
