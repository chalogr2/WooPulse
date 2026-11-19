using System.Security.Claims;
using WooPulse.Api.Dtos;
using WooPulse.Api.Security;
using WooPulse.Api.Services;

namespace WooPulse.Api.Endpoints;

/// <summary>
/// What: HTTP endpoints for a store's team: /stores/{storeId}/members... and /leave.
/// Rules live in MemberService; these handlers only translate HTTP ↔ service calls.
/// </summary>
public static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        // Route parameters can live in the group prefix: "{storeId:int}" binds to the
        // "int storeId" parameter of every handler below.
        var group = app.MapGroup("/stores/{storeId:int}/members").RequireAuthorization();

        // GET /stores/{storeId}/members
        group.MapGet("", async (ClaimsPrincipal user, int storeId, MemberService members) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var (list, error) = await members.ListAsync(sub, storeId);
            return error ?? Results.Ok(list);
        })
        .WithName("ListMembers");

        // PUT /stores/{storeId}/members/{memberId}   body: { role: 2 | 3 }
        group.MapPut("/{memberId:int}", async (ClaimsPrincipal user, int storeId, int memberId, ChangeRoleRequestDto req, MemberService members) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var (dto, error) = await members.ChangeRoleAsync(sub, storeId, memberId, req);
            return error ?? Results.Ok(dto);
        })
        .WithName("ChangeMemberRole");

        // DELETE /stores/{storeId}/members/{memberId}
        group.MapDelete("/{memberId:int}", async (ClaimsPrincipal user, int storeId, int memberId, MemberService members) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var error = await members.RemoveAsync(sub, storeId, memberId);
            return error ?? Results.NoContent();
        })
        .WithName("RemoveMember");

        // POST /stores/{storeId}/leave: remove MY membership
        app.MapPost("/stores/{storeId:int}/leave", async (ClaimsPrincipal user, int storeId, MemberService members) =>
        {
            var sub = user.GetSub();
            if (sub is null) return Results.Unauthorized();
            var error = await members.LeaveAsync(sub, storeId);
            return error ?? Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("LeaveStore");

        return app;
    }
}
