using System.Security.Claims;
using WooPulse.Api.Security;
using WooPulse.Api.Services;

namespace WooPulse.Api.Endpoints;

/// <summary>What: GET /stores/{storeId}/analytics: dashboard data (any member).</summary>
public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        // ?from=2026-10-01&to=2026-10-31 (both optional; default = last 30 days).
        // "DateOnly? from": ASP.NET binds it from the query string by parsing "YYYY-MM-DD".
        // An invalid date like "2026-13-45" is rejected with 400 before our code runs.
        app.MapGet("/stores/{storeId:int}/analytics",
            async (ClaimsPrincipal user, int storeId, DateOnly? from, DateOnly? to, AnalyticsService analytics) =>
            {
                var sub = user.GetSub();
                if (sub is null) return Results.Unauthorized();
                var (dto, error) = await analytics.GetAsync(sub, storeId, from, to);
                return error ?? Results.Ok(dto);
            })
            .RequireAuthorization()
            .WithName("GetAnalytics");

        return app;
    }
}
