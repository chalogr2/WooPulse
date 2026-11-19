using WooPulse.Api.Services;

namespace WooPulse.Api.Setup;

/// <summary>
/// What: registers WooPulse's own application services + CORS.
/// When you add a new service class (e.g. AnalyticsService in Step 2), register it here.
/// </summary>
public static class AppServicesSetup
{
    public const string LocalDevCorsPolicy = "LocalDev";

    public static IServiceCollection AddWooPulseServices(this IServiceCollection services)
    {
        // SCOPED = one instance per HTTP request (or per Hangfire job run).
        // Matches AppDbContext's lifetime, which these services depend on.
        services.AddScoped<StoreService>();
        services.AddScoped<MemberService>();
        services.AddScoped<MaintenanceService>();

        // CORS only matters if a browser calls this API *directly*. With the Next.js BFF,
        // the browser never does (Next.js calls it server-to-server), so this is a safety net.
        services.AddCors(options =>
        {
            options.AddPolicy(LocalDevCorsPolicy, policy => policy
                .WithOrigins("http://localhost:3000")
                .AllowAnyHeader()
                .AllowAnyMethod());
        });

        return services;
    }
}
