using Microsoft.EntityFrameworkCore;

namespace WooPulse.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreInvitation> StoreInvitations => Set<StoreInvitation>();
    public DbSet<StoreMember> StoreMembers => Set<StoreMember>();
    public DbSet<StoreConnection> StoreConnections => Set<StoreConnection>();
    public DbSet<WooOrder> Orders => Set<WooOrder>();
    public DbSet<WooOrderItem> OrderItems => Set<WooOrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Orders (DEVLOG #012). Entity<T>(e => { ... }) groups all config for one entity.
        modelBuilder.Entity<WooOrder>(e =>
        {
            e.HasQueryFilter(o => !o.Store.IsDeleted);

            // Upsert key: one row per WooCommerce order per store.
            e.HasIndex(o => new { o.StoreId, o.WooOrderId }).IsUnique();
            // Analytics queries filter by store + date range, so this index makes them fast.
            e.HasIndex(o => new { o.StoreId, o.CreatedLocalDate });

            // numeric(18,4): exact decimal with up to 4 decimal places in Postgres.
            e.Property(o => o.Total).HasPrecision(18, 4);
            e.Property(o => o.TotalTax).HasPrecision(18, 4);
            e.Property(o => o.ShippingTotal).HasPrecision(18, 4);
            e.Property(o => o.DiscountTotal).HasPrecision(18, 4);
        });

        modelBuilder.Entity<WooOrderItem>(e =>
        {
            // Matching filter on the child, so EF doesn't warn about an item whose
            // (filtered-out) order is missing.
            e.HasQueryFilter(i => !i.Order.Store.IsDeleted);
            e.Property(i => i.Total).HasPrecision(18, 4);
        });

        // StoreConnection (DEVLOG #011): hidden with its store when soft-deleted,
        // and at most ONE connection per store (unique index on StoreId).
        modelBuilder.Entity<StoreConnection>()
            .HasQueryFilter(c => !c.Store.IsDeleted);

        modelBuilder.Entity<StoreConnection>()
            .HasIndex(c => c.StoreId)
            .IsUnique();

        modelBuilder.Entity<StoreConnection>()
            .Property(c => c.AuthMode)
            .HasConversion<int>();

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
