using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using WooPulse.Api.Dtos;
using WooPulse.Api.Validation;
using WooPulse.Api.Services;
using WooPulse.Api.Security;

var builder = WebApplication.CreateBuilder(args);

// Container services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var appDbConn = builder.Configuration.GetConnectionString("AppDb")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:AppDb in appsettings.json");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(appDbConn));

var connString = builder.Configuration.GetConnectionString("HangfireDb")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:HangfireDb in appsettings.json");

builder.Services.AddHangfire(cfg =>
{
    cfg.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
       .UseSimpleAssemblyNameTypeSerializer()
       .UseRecommendedSerializerSettings()
       .UsePostgreSqlStorage(connString, new PostgreSqlStorageOptions
       {
           SchemaName = "hangfire"
       });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalDev", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var domain = builder.Configuration["Auth0:Domain"]
            ?? throw new InvalidOperationException("Missing Auth0:Domain in appsettings.json");

        var audience = builder.Configuration["Auth0:Audience"]
            ?? throw new InvalidOperationException("Missing Auth0:Audience in appsettings.json");

        options.Authority = $"https://{domain}/";
        options.Audience = audience;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
        };
    });

// What: permission-based policies (they check the Auth0 "permissions" claim).
// Status: DEFINED but NOT USED by any endpoint (see DEVLOG #007).
// Why not used: Auth0 gives brand-new users no permissions, so every new signup got 403.
//      Store access is enforced per store by StoreService (membership + role) instead.
// To re-enable: change an endpoint's .RequireAuthorization() back to
//      .RequireAuthorization("stores:read") (or "stores:write" / "stores:delete").
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("stores:read", policy =>
        policy.RequireClaim("permissions", "stores:read"));

    options.AddPolicy("stores:write", policy =>
        policy.RequireClaim("permissions", "stores:write"));

    options.AddPolicy("stores:delete", policy =>
        policy.RequireClaim("permissions", "stores:delete"));
});
builder.Services.AddHangfireServer();

builder.Services.AddProblemDetails();
builder.Services.AddScoped<StoreService>();
builder.Services.AddScoped<MaintenanceService>();

var app = builder.Build();

// What: global exception handler, registered FIRST.
// Why: middleware runs in the order it is added (a pipeline). An exception thrown by
//      anything registered *before* the handler would never reach it. Putting it first
//      means every later middleware and endpoint is covered, and the client gets a clean
//      ProblemDetails JSON (from AddProblemDetails above) instead of a stack trace.
app.UseExceptionHandler();

app.UseCors("LocalDev");

// What: Hangfire dashboard only in Development.
// Why: the dashboard can trigger/delete jobs. "LocalRequestsOnly" trusts any request that
//      *looks* local. Behind a reverse proxy, every request looks local. So in any
//      non-Development environment we don't expose the dashboard at all.
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire", new Hangfire.DashboardOptions
    {
        Authorization = new[] { new Hangfire.Dashboard.LocalRequestsOnlyAuthorizationFilter() }
    });
}

app.UseAuthentication();
app.UseAuthorization();

// Smoke test background job. Calls a method (no arguments) so the time is read when the
// job runs, not once at registration (see MaintenanceService.Heartbeat, DEVLOG #008).
RecurringJob.AddOrUpdate<MaintenanceService>(
    "heartbeat",
    svc => svc.Heartbeat(),
    Cron.Minutely
);

RecurringJob.AddOrUpdate<MaintenanceService>(
    "expire-invites",
    svc => svc.ExpireInvitesAsync(),
    Cron.Hourly
);

RecurringJob.AddOrUpdate<MaintenanceService>(
    "purge-soft-deleted-stores",
    svc => svc.PurgeSoftDeletedStoresAsync(30),
    Cron.Daily
);

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

static string? GetSub(HttpContext ctx)
{
    return ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
}

// Get email from token
static string? GetEmail(HttpContext ctx)
{
    var email =
        // custom namespaced claim (recommended)
        ctx.User.FindFirst("https://woopulse.local/email")?.Value

        // common non-namespaced fallbacks (might still be absent)
        ?? ctx.User.FindFirst("email")?.Value
        ?? ctx.User.FindFirst(ClaimTypes.Email)?.Value
        ?? ctx.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value;

    return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}

// Weather forecast
app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

// Me (find user)
app.MapGet("/me", (HttpContext ctx) =>
{
    var sub =
        ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    return Results.Ok(new
    {
        sub,
        name = ctx.User.Identity?.Name
    });
})
.RequireAuthorization()
.WithName("Me");

// Get stores
app.MapGet("/stores", async (HttpContext ctx, StoreService stores) =>
{
    var sub = GetSub(ctx);
    if (sub is null) return Results.Unauthorized();
    var list = await stores.ListAsync(sub);
    return Results.Ok(list);
})
.RequireAuthorization() // any logged-in user; StoreService checks store membership + role
.WithName("ListStores");

// Get stores {id}
app.MapGet("/stores/{id:int}", async (HttpContext ctx, int id, StoreService stores) =>
{
    var sub = GetSub(ctx);
    if (sub is null) return Results.Unauthorized();
    var store = await stores.GetAsync(sub, id);
    return store is null ? Results.NotFound() : Results.Ok(store);
})
.RequireAuthorization() // any logged-in user; StoreService checks store membership + role
.WithName("GetStore");


// Create store
app.MapPost("/stores", async (HttpContext ctx, CreateStoreRequestDto req, StoreService stores) =>
{
    var sub = GetSub(ctx);
    if (sub is null) return Results.Unauthorized();
    var (dto, error) = await stores.CreateAsync(sub, req);
    if (error is not null) return error;

    return Results.Created($"/stores/{dto!.Id}", dto);
})
.RequireAuthorization() // any logged-in user; StoreService checks store membership + role
.WithName("CreateStore");

// Rename store
app.MapPut("/stores/{id:int}", async (HttpContext ctx, int id, RenameStoreRequestDto req, StoreService stores) =>
{
    var sub = GetSub(ctx);
    if (sub is null) return Results.Unauthorized();
    var (dto, error) = await stores.RenameAsync(sub, id, req);
    if (error is not null) return error;

    return Results.Ok(dto);
})
.RequireAuthorization() // any logged-in user; StoreService checks store membership + role
.WithName("RenameStore");

// Delete store
app.MapDelete("/stores/{id:int}", async (HttpContext ctx, int id, StoreService stores) =>
{
    var sub = GetSub(ctx);
    if (sub is null) return Results.Unauthorized();
    var error = await stores.SoftDeleteAsync(sub, id);
    if (error is not null) return error;

    return Results.NoContent();
})
.RequireAuthorization() // any logged-in user; StoreService checks store membership + role
.WithName("DeleteStore");

// Get deleted stores
app.MapGet("/stores/deleted", async (HttpContext ctx, StoreService stores) =>
{
    var sub = GetSub(ctx);
    if (sub is null) return Results.Unauthorized();
    var deleted = await stores.ListDeletedAsync(sub);
    return Results.Ok(deleted);
})
.RequireAuthorization() // any logged-in user; StoreService checks store membership + role
.WithName("ListDeletedStores");

// Get API health
app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        utc = DateTimeOffset.UtcNow
    });
})
.WithName("Health");

// Invite to store
app.MapPost("/stores/{storeId:int}/invites", async (HttpContext ctx, int storeId, InviteRequestDto req, StoreService stores) =>
{
    var sub = GetSub(ctx);
    if (sub is null) return Results.Unauthorized();

    var (dto, error) = await stores.InviteAsync(sub, storeId, req);
    if (error is not null) return error;

    return Results.Ok(dto);
})
.RequireAuthorization()
.WithName("InviteToStore");

// List invites
app.MapGet("/invites/mine", async (HttpContext ctx, StoreService stores) =>
{
    var email = GetEmail(ctx);
    if (email is null) return Results.Unauthorized();

    var list = await stores.ListMyInvitesAsync(email);
    return Results.Ok(list);
})
.RequireAuthorization()
.WithName("MyInvites");

// List debug invites
app.MapGet("/invites/mine/debug", async (HttpContext ctx, StoreService stores) =>
{
    if (!app.Environment.IsDevelopment())
        return Results.NotFound();

    var email = GetEmail(ctx);
    if (email is null) return Results.Unauthorized();
    var invites = await stores.ListMyInvitesDebugAsync(email);
    return Results.Ok(invites);
})
.RequireAuthorization()
.WithName("MyInvitesDebug");

// Accept invite
app.MapPost("/invites/accept", async (HttpContext ctx, AcceptInviteRequestDto req, StoreService stores) =>
{
    var sub = GetSub(ctx);
    if (sub is null) return Results.Unauthorized();
    var email = GetEmail(ctx);
    if (email is null) return Results.Unauthorized();

    var (store, error) = await stores.AcceptInviteAsync(sub, email, req.Token);
    if (error is not null) return error;

    return Results.Ok(store);
})
.RequireAuthorization()
.WithName("AcceptInvite");

// Debug claims
// Same Development-only gate as the other debug/job endpoints below.
app.MapGet("/debug/claims", (HttpContext ctx) =>
{
    if (!app.Environment.IsDevelopment())
        return Results.NotFound();

    var claims = ctx.User.Claims.Select(c => new { c.Type, c.Value }).ToList();
    return Results.Ok(claims);
})
.RequireAuthorization();

// Dev endpoint to test expiration of invites
app.MapPost("/jobs/expire-invites", async (MaintenanceService maintenance) =>
{
    if (!app.Environment.IsDevelopment())
        return Results.NotFound();

    var count = await maintenance.ExpireInvitesAsync();
    return Results.Ok(new { expiredInvites = count });
})
.RequireAuthorization()
.WithName("RunExpireInvitesJob");

// Dev endpoint to test purging of soft-deleted stores
app.MapPost("/jobs/purge-deleted-stores", async (int? days, MaintenanceService maintenance) =>
{
    if (!app.Environment.IsDevelopment())
        return Results.NotFound();

    var olderThanDays = days ?? 30;
    var count = await maintenance.PurgeSoftDeletedStoresAsync(olderThanDays);
    return Results.Ok(new { purgedStores = count, olderThanDays });
})
.RequireAuthorization()
.WithName("RunPurgeDeletedStoresJob");

app.MapGet("/", () => Results.Ok(new { name = "WooPulse API", status = "running" }))
   .WithName("Root");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
