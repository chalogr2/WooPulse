using System.Security.Claims;
using WooPulse.Api.Security;

namespace WooPulse.Api.Endpoints;

/// <summary>
/// What: endpoints about the API itself: root, health check, "who am I".
/// </summary>
public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /: quick "is it running?" (no login)
        app.MapGet("/", () => Results.Ok(new { name = "WooPulse API", status = "running" }))
           .WithName("Root");

        // GET /health: used by the web app's /api/health (no login)
        app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }))
           .WithName("Health");

        // GET /me: the caller's Auth0 id
        app.MapGet("/me", (ClaimsPrincipal user) =>
            Results.Ok(new { sub = user.GetSub(), name = user.Identity?.Name }))
           .RequireAuthorization()
           .WithName("Me");

        return app;
    }
}
