using System.Security.Claims;
using WooPulse.Api.Dtos;
using WooPulse.Api.Security;
using WooPulse.Api.Services;

namespace WooPulse.Api.Endpoints;

/// <summary>
/// What: HTTP endpoints for store invitations.
/// The dev-only "/invites/mine/debug" (returns tokens) lives in DevEndpoints.
/// </summary>
public static class InviteEndpoints
{
    public static IEndpointRouteBuilder MapInviteEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /stores/{storeId}/invites: invite an email to a store (Owner/Admin)
        app.MapPost("/stores/{storeId:int}/invites", async (ClaimsPrincipal user, int storeId, InviteRequestDto req, StoreService stores) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var (dto, error) = await stores.InviteAsync(sub, storeId, req);
            return error ?? Results.Ok(dto);
        })
        .RequireAuthorization()
        .WithName("InviteToStore");

        // GET /stores/{storeId}/invites: pending invites of this store (Owner/Admin)
        app.MapGet("/stores/{storeId:int}/invites", async (ClaimsPrincipal user, int storeId, StoreService stores) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var (list, error) = await stores.ListStoreInvitesAsync(sub, storeId);
            return error ?? Results.Ok(list);
        })
        .RequireAuthorization()
        .WithName("ListStoreInvites");

        // POST /stores/{storeId}/invites/{inviteId}/revoke (Owner/Admin)
        // POST + "/revoke" (not DELETE) because the invite isn't deleted: its status changes
        // to Revoked and stays in the table for the audit trail.
        app.MapPost("/stores/{storeId:int}/invites/{inviteId:int}/revoke", async (ClaimsPrincipal user, int storeId, int inviteId, StoreService stores) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var error = await stores.RevokeInviteAsync(sub, storeId, inviteId);
            return error ?? Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("RevokeInvite");

        var group = app.MapGroup("/invites").RequireAuthorization();

        // GET /invites/mine: my pending invites (matched by the email in my token)
        group.MapGet("/mine", async (ClaimsPrincipal user, StoreService stores) =>
        {
            var email = user.GetEmail();
            if (email is null) return Results.Unauthorized();
            return Results.Ok(await stores.ListMyInvitesAsync(email));
        })
        .WithName("MyInvites");

        // POST /invites/accept: body { token }
        group.MapPost("/accept", async (ClaimsPrincipal user, AcceptInviteRequestDto req, StoreService stores) =>
        {
            var sub = user.GetSub();
            var email = user.GetEmail();
            if (sub is null || email is null) return Results.Unauthorized();

            var (store, error) = await stores.AcceptInviteAsync(sub, email, req.Token);
            return error ?? Results.Ok(store);
        })
        .WithName("AcceptInvite");

        return app;
    }
}
