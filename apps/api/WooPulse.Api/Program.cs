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

var app = builder.Build();

app.UseCors("LocalDev");
app.UseHangfireDashboard(builder.Configuration["Hangfire:DashboardPath"] ?? "/hangfire");
app.UseAuthentication();
app.UseAuthorization();
app.UseExceptionHandler();

// Smoke test background job
RecurringJob.AddOrUpdate(
    "heartbeat",
    () => Console.WriteLine($"Hangfire heartbeat: {DateTimeOffset.Now}"),
    Cron.Minutely
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

static string GetSub(HttpContext ctx)
{
    return ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new Exception("No sub claim");
}

// Get email from token
static string GetEmail(HttpContext ctx)
{
    var email =
        // custom namespaced claim (recommended)
        ctx.User.FindFirst("https://woopulse.local/email")?.Value

        // common non-namespaced fallbacks (might still be absent)
        ?? ctx.User.FindFirst("email")?.Value
        ?? ctx.User.FindFirst(ClaimTypes.Email)?.Value
        ?? ctx.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value;

    if (string.IsNullOrWhiteSpace(email))
        throw new Exception("No email claim");

    return email.Trim().ToLowerInvariant();
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
    var list = await stores.ListAsync(sub);
    return Results.Ok(list);
})
.RequireAuthorization("stores:read")
.WithName("ListStores");

// Get stores {id}
app.MapGet("/stores/{id:int}", async (HttpContext ctx, int id, StoreService stores) =>
{
    var sub = GetSub(ctx);
    var store = await stores.GetAsync(sub, id);
    return store is null ? Results.NotFound() : Results.Ok(store);
})
.RequireAuthorization("stores:read")
.WithName("GetStore");


// Create store
app.MapPost("/stores", async (HttpContext ctx, CreateStoreRequestDto req, StoreService stores) =>
{
    var sub = GetSub(ctx);
    var (dto, error) = await stores.CreateAsync(sub, req);
    if (error is not null) return error;

    return Results.Created($"/stores/{dto!.Id}", dto);
})
.RequireAuthorization("stores:write")
.WithName("CreateStore");

// Rename store
app.MapPut("/stores/{id:int}", async (HttpContext ctx, int id, RenameStoreRequestDto req, StoreService stores) =>
{
    var sub = GetSub(ctx);
    var (dto, error) = await stores.RenameAsync(sub, id, req);
    if (error is not null) return error;

    return Results.Ok(dto);
})
.RequireAuthorization("stores:write")
.WithName("RenameStore");

// Delete store
app.MapDelete("/stores/{id:int}", async (HttpContext ctx, int id, StoreService stores) =>
{
    var sub = GetSub(ctx);
    var error = await stores.SoftDeleteAsync(sub, id);
    if (error is not null) return error;

    return Results.NoContent();
})
.RequireAuthorization("stores:delete")
.WithName("DeleteStore");

// Get deleted stores
app.MapGet("/stores/deleted", async (HttpContext ctx, StoreService stores) =>
{
    var sub = GetSub(ctx);
    var deleted = await stores.ListDeletedAsync(sub);
    return Results.Ok(deleted);
})
.RequireAuthorization("stores:read")
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
    var invites = await stores.ListMyInvitesDebugAsync(email);
    return Results.Ok(invites);
})
.RequireAuthorization()
.WithName("MyInvitesDebug");

// Accept invite
app.MapPost("/invites/accept", async (HttpContext ctx, AcceptInviteRequestDto req, StoreService stores) =>
{
    var sub = GetSub(ctx);
    var email = GetEmail(ctx);

    var (store, error) = await stores.AcceptInviteAsync(sub, email, req.Token);
    if (error is not null) return error;

    return Results.Ok(store);
})
.RequireAuthorization()
.WithName("AcceptInvite");

// Debug claims
app.MapGet("/debug/claims", (HttpContext ctx) =>
{
    var claims = ctx.User.Claims.Select(c => new { c.Type, c.Value }).ToList();
    return Results.Ok(claims);
})
.RequireAuthorization();

app.MapGet("/", () => Results.Ok(new { name = "WooPulse API", status = "running" }))
   .WithName("Root");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
