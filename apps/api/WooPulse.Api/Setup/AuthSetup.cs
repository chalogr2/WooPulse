using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace WooPulse.Api.Setup;

/// <summary>
/// What: authentication (validate Auth0 JWTs) + authorization policies.
///
/// Authentication = "who are you?"  → every request's Bearer token is checked against
///                  your Auth0 tenant: signature, issuer, audience, expiry.
/// Authorization  = "what may you do?" → endpoints use .RequireAuthorization() (valid
///                  login). Per-store rules live in StoreService (membership + role).
/// </summary>
public static class AuthSetup
{
    public static IServiceCollection AddWooPulseAuth(this IServiceCollection services, IConfiguration config)
    {
        var domain = config["Auth0:Domain"]
            ?? throw new InvalidOperationException("Missing Auth0:Domain (set it with dotnet user-secrets)");
        var audience = config["Auth0:Audience"]
            ?? throw new InvalidOperationException("Missing Auth0:Audience in appsettings.json");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Authority = who issues tokens. The JwtBearer handler downloads Auth0's
                // public signing keys from {Authority}/.well-known/openid-configuration.
                options.Authority = $"https://{domain}/";
                // Audience = which API the token was issued FOR. Rejects tokens meant for other APIs.
                options.Audience = audience;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                };
            });

        // What: permission-based policies (they check the Auth0 "permissions" claim).
        // Status: DEFINED but NOT USED by any endpoint (see DEVLOG #007).
        // Why not used: Auth0 gives brand-new users no permissions, so every new signup got 403.
        // To re-enable: change an endpoint's .RequireAuthorization() to
        //      .RequireAuthorization("stores:read") (or "stores:write" / "stores:delete").
        services.AddAuthorization(options =>
        {
            options.AddPolicy("stores:read", p => p.RequireClaim("permissions", "stores:read"));
            options.AddPolicy("stores:write", p => p.RequireClaim("permissions", "stores:write"));
            options.AddPolicy("stores:delete", p => p.RequireClaim("permissions", "stores:delete"));
        });

        return services;
    }
}
