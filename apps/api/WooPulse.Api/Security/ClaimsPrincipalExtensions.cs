using System.Security.Claims;

namespace WooPulse.Api.Security;

/// <summary>
/// What: helpers to read the current user's identity from the validated JWT.
/// Why:  every endpoint needs "who is calling?". Before, Program.cs had two local
///       functions for this; now any endpoint file can use them.
///
/// Syntax: "this ClaimsPrincipal user" makes these EXTENSION METHODS. They are static
///         methods, but you call them as if they were built into ClaimsPrincipal:
///         user.GetSub()   instead of   ClaimsPrincipalExtensions.GetSub(user)
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The Auth0 user id, e.g. "auth0|65f..." or "google-oauth2|1234...".</summary>
    public static string? GetSub(this ClaimsPrincipal user)
        => user.FindFirst("sub")?.Value
        // ASP.NET may remap "sub" to this long claim type name, so we check both.
        ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// The user's email, trimmed and lowercased, or null if the token has none.
    /// Access tokens don't include email by default; an Auth0 Action adds the
    /// namespaced claim "https://woopulse.local/email" (see DEVLOG #002).
    /// </summary>
    public static string? GetEmail(this ClaimsPrincipal user)
    {
        var email =
            user.FindFirst("https://woopulse.local/email")?.Value // custom claim (recommended)
            ?? user.FindFirst("email")?.Value                     // common fallbacks
            ?? user.FindFirst(ClaimTypes.Email)?.Value;           // (ClaimTypes.Email is the
                                                                  //  long xmlsoap URI form)

        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }
}
