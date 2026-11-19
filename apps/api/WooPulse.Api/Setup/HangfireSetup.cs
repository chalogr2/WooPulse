using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using WooPulse.Api.Services;

namespace WooPulse.Api.Setup;

/// <summary>
/// What: everything Hangfire (background jobs): storage, worker server, dashboard and the
///       recurring job schedule, in one place.
/// </summary>
public static class HangfireSetup
{
    // ---- Services (called on builder.Services) ----
    public static IServiceCollection AddWooPulseHangfire(this IServiceCollection services, IConfiguration config)
    {
        var hangfireConn = config.GetConnectionString("HangfireDb")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:HangfireDb (set it with dotnet user-secrets)");

        // Where jobs are stored: a separate "hangfire" database, schema "hangfire".
        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(hangfireConn, new PostgreSqlStorageOptions { SchemaName = "hangfire" }));

        // The worker that actually runs jobs, as a background service inside this API process.
        services.AddHangfireServer();
        return services;
    }

    // ---- Middleware (called on app) ----
    /// <summary>
    /// Dashboard at /hangfire, Development only (DEVLOG #003): LocalRequestsOnly trusts any
    /// request that *looks* local, and behind a reverse proxy every request looks local.
    /// </summary>
    public static WebApplication UseWooPulseHangfireDashboard(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = new IDashboardAuthorizationFilter[] { new LocalRequestsOnlyAuthorizationFilter() }
            });
        }
        return app;
    }

    // ---- Recurring jobs (called once at startup) ----
    /// <summary>
    /// Why IRecurringJobManager instead of the static RecurringJob class (DEVLOG #009):
    /// the static class needs a global "JobStorage.Current" that, in this setup, only got
    /// initialized as a side effect of UseHangfireDashboard. With the dashboard dev-only,
    /// startup outside Development would crash. Asking DI for IRecurringJobManager
    /// gives a manager already wired to our Postgres storage, in every environment.
    ///
    /// Hangfire stores "which method + its arguments". Compute anything time-based INSIDE
    /// the job method, never in this expression (see MaintenanceService.Heartbeat).
    /// </summary>
    public static WebApplication RegisterWooPulseRecurringJobs(this WebApplication app)
    {
        var jobs = app.Services.GetRequiredService<IRecurringJobManager>();

        // <MaintenanceService>: Hangfire creates the service from DI (new scope) per run.
        jobs.AddOrUpdate<MaintenanceService>("heartbeat", svc => svc.Heartbeat(), Cron.Minutely());
        jobs.AddOrUpdate<MaintenanceService>("expire-invites", svc => svc.ExpireInvitesAsync(), Cron.Hourly());
        jobs.AddOrUpdate<MaintenanceService>("purge-soft-deleted-stores", svc => svc.PurgeSoftDeletedStoresAsync(30), Cron.Daily());

        // Every 15 minutes (owner decision #011). Cron: "*/15 * * * *" = minute 0,15,30,45 of every hour.
        // This job only QUEUES one SyncStoreAsync job per connected store (SyncService.EnqueueAllAsync),
        // so stores sync independently and one slow shop doesn't delay the others.
        jobs.AddOrUpdate<SyncService>("sync-woocommerce", svc => svc.EnqueueAllAsync(), "*/15 * * * *");
        return app;
    }
}
