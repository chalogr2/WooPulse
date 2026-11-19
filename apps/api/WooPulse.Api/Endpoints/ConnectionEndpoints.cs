using System.Security.Claims;
using WooPulse.Api.Dtos;
using WooPulse.Api.Security;
using WooPulse.Api.Services;

namespace WooPulse.Api.Endpoints;

/// <summary>
/// What: /stores/{storeId}/connection: the store's WooCommerce connection.
/// Rules live in ConnectionService.
/// </summary>
public static class ConnectionEndpoints
{
    public static IEndpointRouteBuilder MapConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/stores/{storeId:int}/connection").RequireAuthorization();

        // GET: connection status (any member). 204 No Content = not connected yet.
        group.MapGet("", async (ClaimsPrincipal user, int storeId, ConnectionService connections) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var (dto, error) = await connections.GetAsync(sub, storeId);
            return error ?? (dto is null ? Results.NoContent() : Results.Ok(dto));
        })
        .WithName("GetConnection");

        // PUT: save/replace (Owner). Tests the keys first; only saves if they work.
        // PUT (not POST) because there's at most one connection per store: PUT means
        // "set this resource to this value", and repeating it gives the same result.
        group.MapPut("", async (ClaimsPrincipal user, int storeId, SaveConnectionRequestDto req, ConnectionService connections) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var (dto, error) = await connections.SaveAsync(sub, storeId, req);
            return error ?? Results.Ok(dto);
        })
        .WithName("SaveConnection");

        // POST /test: re-test saved keys (Owner/Admin)
        group.MapPost("/test", async (ClaimsPrincipal user, int storeId, ConnectionService connections) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var (dto, error) = await connections.TestAsync(sub, storeId);
            return error ?? Results.Ok(dto);
        })
        .WithName("TestConnection");

        // DELETE: disconnect (Owner)
        group.MapDelete("", async (ClaimsPrincipal user, int storeId, ConnectionService connections) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var error = await connections.DeleteAsync(sub, storeId);
            return error ?? Results.NoContent();
        })
        .WithName("DeleteConnection");

        // POST /stores/{storeId}/sync: "Sync now" (any member, owner decision #011).
        // Returns 202 Accepted = "queued, not finished yet". The UI polls GET /connection
        // to watch SyncInProgress / OrderCount change.
        app.MapPost("/stores/{storeId:int}/sync", async (ClaimsPrincipal user, int storeId, SyncService sync) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            return await sync.RequestSyncAsync(sub, storeId);
        })
        .RequireAuthorization()
        .WithName("SyncStore");

        return app;
    }
}
