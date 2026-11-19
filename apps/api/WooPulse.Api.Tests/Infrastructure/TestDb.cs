using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WooPulse.Api.Data;

namespace WooPulse.Api.Tests.Infrastructure;

/// <summary>
/// A fresh, private database for one test: SQLite in memory, built from the real AppDbContext
/// model (same query filters, unique indexes and cascade deletes as production).
///
/// Why SQLite and not Postgres: the tests run anywhere (no Docker), in about a second.
/// What that does NOT cover, on purpose:
///   - truly simultaneous requests (e.g. two accepts racing on Postgres row locks);
///     the tests check the sequential behavior of the same conditional UPDATEs instead.
///   - Postgres numeric(18,4) precision: SQLite can't SUM/ORDER BY decimals, so the test
///     model stores money as double (the test amounts are exact in double).
///
/// Each NewContext() is like one HTTP request: a new DbContext over the same database,
/// so nothing is served from a previous context's change tracker.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        // The in-memory database lives as long as this open connection.
        // Foreign Keys=True: enforce FKs and ON DELETE CASCADE like Postgres does.
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _connection.Open();

        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public AppDbContext NewContext() =>
        new SqliteAppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    /// <summary>Creates a store with an Owner and any other members. Returns the store id.</summary>
    public int SeedStore(string ownerSub, params (string Sub, StoreRole Role)[] others)
    {
        using var db = NewContext();
        var store = new Store { Name = "Test shop", OwnerSub = ownerSub, CreatedBySub = ownerSub };
        store.Members.Add(new StoreMember { UserSub = ownerSub, Role = StoreRole.Owner, Email = Users.EmailOf(ownerSub) });
        foreach (var (sub, role) in others)
            store.Members.Add(new StoreMember { UserSub = sub, Role = role, Email = Users.EmailOf(sub) });

        db.Stores.Add(store);
        db.SaveChanges();
        return store.Id;
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>The production model, plus the two type mappings SQLite needs (see class comment).</summary>
    private sealed class SqliteAppDbContext : AppDbContext
    {
        public SqliteAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder builder)
        {
            // SQLite can't compare or sort DateTimeOffset; as a long it can. All test times are UTC.
            builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
            builder.Properties<decimal>().HaveConversion<double>();
        }
    }
}

/// <summary>Auth0-style user ids used across the tests.</summary>
public static class Users
{
    public const string Owner = "auth0|owner";
    public const string Admin = "auth0|admin";
    public const string Member = "auth0|member";
    public const string Stranger = "auth0|stranger";

    public static string EmailOf(string sub) => sub.Split('|')[1] + "@example.test";
}
