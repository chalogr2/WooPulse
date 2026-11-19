using System.Security.Claims;
using WooPulse.Api.Dtos;
using WooPulse.Api.Security;
using WooPulse.Api.Services;

namespace WooPulse.Api.Endpoints;

/// <summary>
/// What: HTTP endpoints for the Store feature (/stores/...).
/// Rule: endpoints stay THIN. They read the caller's identity, call StoreService, and turn
///       the result into an HTTP response. Business rules (who may rename, delete...) live
///       in StoreService, so they can't be bypassed and are easy to test.
/// </summary>
public static class StoreEndpoints
{
    public static IEndpointRouteBuilder MapStoreEndpoints(this IEndpointRouteBuilder app)
    {
        // MapGroup: every route below starts with "/stores" and inherits
        // RequireAuthorization() (valid Auth0 login). Write it once instead of on every route.
        var group = app.MapGroup("/stores").RequireAuthorization();

        // Handler parameters are filled in by ASP.NET automatically ("parameter binding"):
        //   ClaimsPrincipal user  → the logged-in user from the validated JWT
        //   StoreService stores   → resolved from dependency injection
        //   int id                → from the route "{id:int}" (":int" rejects non-numbers)
        //   CreateStoreRequestDto → deserialized from the JSON request body

        // GET /stores: stores I'm a member of
        group.MapGet("", async (ClaimsPrincipal user, StoreService stores) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();

            // Keep my email on my memberships up to date (DEVLOG #009). One UPDATE that
            // usually changes 0 rows; it backfills members created before the Email column.
            await stores.SyncMemberEmailAsync(sub, user.GetEmail());

            return Results.Ok(await stores.ListAsync(sub));
        })
        .WithName("ListStores");

        // GET /stores/deleted: my soft-deleted stores ("Trash")
        group.MapGet("/deleted", async (ClaimsPrincipal user, StoreService stores) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            return Results.Ok(await stores.ListDeletedAsync(sub));
        })
        .WithName("ListDeletedStores");

        // GET /stores/{id}
        group.MapGet("/{id:int}", async (ClaimsPrincipal user, int id, StoreService stores) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var store = await stores.GetAsync(sub, id);
            return store is null ? Results.NotFound() : Results.Ok(store);
        })
        .WithName("GetStore");

        // POST /stores: create (caller becomes Owner)
        group.MapPost("", async (ClaimsPrincipal user, CreateStoreRequestDto req, StoreService stores) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();

            // Tuple deconstruction: the service returns (dto, error); exactly one is non-null.
            var (dto, error) = await stores.CreateAsync(sub, user.GetEmail(), req);
            if (error is not null) return error;

            // "dto!" = null-forgiving operator: we know it's non-null when error is null.
            return Results.Created($"/stores/{dto!.Id}", dto);
        })
        .WithName("CreateStore");

        // PUT /stores/{id}: rename (Owner/Admin)
        group.MapPut("/{id:int}", async (ClaimsPrincipal user, int id, RenameStoreRequestDto req, StoreService stores) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var (dto, error) = await stores.RenameAsync(sub, id, req);
            return error ?? Results.Ok(dto);   // "??" → use error if non-null, else Ok
        })
        .WithName("RenameStore");

        // DELETE /stores/{id}: soft delete (Owner only)
        group.MapDelete("/{id:int}", async (ClaimsPrincipal user, int id, StoreService stores) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var error = await stores.SoftDeleteAsync(sub, id);
            return error ?? Results.NoContent();
        })
        .WithName("DeleteStore");

        // POST /stores/{id}/restore: undo a soft delete (Owner only)
        group.MapPost("/{id:int}/restore", async (ClaimsPrincipal user, int id, StoreService stores) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var (dto, error) = await stores.RestoreAsync(sub, id);
            return error ?? Results.Ok(dto);
        })
        .WithName("RestoreStore");

        return app;
    }
}
