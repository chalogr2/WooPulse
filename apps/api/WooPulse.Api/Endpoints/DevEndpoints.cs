using System.Security.Claims;
using WooPulse.Api.Security;
using WooPulse.Api.Services;

namespace WooPulse.Api.Endpoints;

/// <summary>
/// What: Development-only endpoints: token claims, invite tokens, manual job triggers.
/// Safety: Program.cs only calls MapDevEndpoints() when IsDevelopment() is true, so in any
///         other environment these routes DON'T EXIST (404). Before, each handler checked
///         IsDevelopment() itself; one check at registration can't be forgotten on a new route.
/// </summary>
public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").RequireAuthorization();

        // GET /debug/claims: every claim in my access token (Auth0 setup troubleshooting)
        group.MapGet("/debug/claims", (ClaimsPrincipal user) =>
            Results.Ok(user.Claims.Select(c => new { c.Type, c.Value })));

        // GET /invites/mine/debug: my pending invites INCLUDING tokens. This is what the
        // local UI's "Accept this invite" button uses (DEVLOG #002).
        group.MapGet("/invites/mine/debug", async (ClaimsPrincipal user, StoreService stores) =>
        {
            var email = user.GetEmail();
            if (email is null) return Results.Unauthorized();
            return Results.Ok(await stores.ListMyInvitesDebugAsync(email));
        })
        .WithName("MyInvitesDebug");

        // POST /jobs/expire-invites: run the hourly job now
        group.MapPost("/jobs/expire-invites", async (MaintenanceService maintenance) =>
            Results.Ok(new { expiredInvites = await maintenance.ExpireInvitesAsync() }))
        .WithName("RunExpireInvitesJob");

        // POST /jobs/purge-deleted-stores?days=N: run the daily purge now
        // "int? days" binds from the query string; "?" makes it optional.
        group.MapPost("/jobs/purge-deleted-stores", async (int? days, MaintenanceService maintenance) =>
        {
            var olderThanDays = days ?? 30;
            var count = await maintenance.PurgeSoftDeletedStoresAsync(olderThanDays);
            return Results.Ok(new { purgedStores = count, olderThanDays });
        })
        .WithName("RunPurgeDeletedStoresJob");

        return app;
    }
}
