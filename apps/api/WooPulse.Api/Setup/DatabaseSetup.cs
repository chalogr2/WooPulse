using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;

namespace WooPulse.Api.Setup;

/// <summary>
/// What: registers EF Core's AppDbContext with PostgreSQL (Npgsql).
/// Why a separate file: Program.cs should read like a table of contents. Each "AddX"
///      extension method owns one concern, so you know exactly where to look.
/// </summary>
public static class DatabaseSetup
{
    // "this IServiceCollection services" = extension method on builder.Services.
    // Returning services lets calls be chained, the same style ASP.NET uses (AddX().AddY()).
    public static IServiceCollection AddWooPulseDatabase(this IServiceCollection services, IConfiguration config)
    {
        // Connection string comes from user-secrets in development (never from git).
        var appDbConn = config.GetConnectionString("AppDb")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:AppDb (set it with dotnet user-secrets)");

        // AddDbContext registers AppDbContext as SCOPED: one instance per HTTP request.
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(appDbConn));
        return services;
    }
}
