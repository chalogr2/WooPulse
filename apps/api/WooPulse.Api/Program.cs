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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var appDbConn = builder.Configuration.GetConnectionString("AppDb")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:AppDb in appsettings.json");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(appDbConn));

var connString = builder.Configuration.GetConnectionString("HangfireDb")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:HangfireDb in appsettings.json");

static StoreDto ToDto(Store s) =>
    new(
        s.Id,
        s.Name,
        s.OwnerSub,
        s.CreatedAtUtc,
        s.CreatedBySub,
        s.UpdatedAtUtc,
        s.UpdatedBySub,
        s.IsDeleted,
        s.DeletedAtUtc,
        s.DeletedBySub
    );

builder.Services.AddHangfire(cfg =>
{
    cfg.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
       .UseSimpleAssemblyNameTypeSerializer()
       .UseRecommendedSerializerSettings()
       .UsePostgreSqlStorage(connString, new PostgreSqlStorageOptions
       {
           SchemaName = "hangfire"
           // you can add more later if you want
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

var app = builder.Build();

app.UseCors("LocalDev");

app.UseHangfireDashboard(builder.Configuration["Hangfire:DashboardPath"] ?? "/hangfire");

app.UseAuthentication();
app.UseAuthorization();
app.UseExceptionHandler();

// smoke test background job
RecurringJob.AddOrUpdate(
    "heartbeat",
    () => Console.WriteLine($"Hangfire heartbeat: {DateTimeOffset.Now}"),
    Cron.Minutely
);

// Configure the HTTP request pipeline.
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

app.MapGet("/stores", async (HttpContext ctx, AppDbContext db) =>
{
    var sub =
        ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    if (string.IsNullOrWhiteSpace(sub))
        return Results.Unauthorized();

    var stores = await db.Stores
        .Where(s => s.OwnerSub == sub)
        .OrderByDescending(s => s.Id)
        .Select(s => new StoreDto(
            s.Id,
            s.Name,
            s.OwnerSub,
            s.CreatedAtUtc,
            s.CreatedBySub,
            s.UpdatedAtUtc,
            s.UpdatedBySub,
            s.IsDeleted,
            s.DeletedAtUtc,
            s.DeletedBySub
        ))
        .ToListAsync();

    return Results.Ok(stores);
})
.RequireAuthorization("stores:read")
.WithName("ListStores");

app.MapGet("/stores/{id:int}", async (HttpContext ctx, int id, AppDbContext db) =>
{
    var sub =
        ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    if (string.IsNullOrWhiteSpace(sub))
        return Results.Unauthorized();

    var store = await db.Stores
        .Where(s => s.Id == id && s.OwnerSub == sub)
        .Select(s => new StoreDto(
            s.Id,
            s.Name,
            s.OwnerSub,
            s.CreatedAtUtc,
            s.CreatedBySub,
            s.UpdatedAtUtc,
            s.UpdatedBySub,
            s.IsDeleted,
            s.DeletedAtUtc,
            s.DeletedBySub
        ))
        .SingleOrDefaultAsync();

    return store is null ? Results.NotFound() : Results.Ok(store);
})
.RequireAuthorization("stores:read")
.WithName("GetStore");

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

app.MapPost("/stores", async (HttpContext ctx, CreateStoreRequestDto req, AppDbContext db) =>
{
    var sub =
        ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    if (string.IsNullOrWhiteSpace(sub))
        return Results.Unauthorized();

    var name = StoreValidation.NormalizeName(req.Name);
    if (name is null || !StoreValidation.IsValidName(name))
    {
        return Results.Problem(
            title: "Invalid store name",
            detail: $"Name must be {StoreValidation.NameMin}-{StoreValidation.NameMax} characters.",
            statusCode: 400
        );
    }

    var store = new Store
    {
        Name = name,
        OwnerSub = sub,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        CreatedBySub = sub
    };

    db.Stores.Add(store);
    await db.SaveChangesAsync();

    return Results.Created($"/stores/{store.Id}", ToDto(store));
})
.RequireAuthorization("stores:write")
.WithName("CreateStore");

app.MapPut("/stores/{id:int}", async (HttpContext ctx, int id, RenameStoreRequestDto req, AppDbContext db) =>
{
    var sub =
        ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    if (string.IsNullOrWhiteSpace(sub))
        return Results.Unauthorized();

    var name = StoreValidation.NormalizeName(req.Name);
    if (name is null || !StoreValidation.IsValidName(name))
    {
        return Results.Problem(
            title: "Invalid store name",
            detail: $"Name must be {StoreValidation.NameMin}-{StoreValidation.NameMax} characters.",
            statusCode: 400
        );
    }

    var store = await db.Stores.SingleOrDefaultAsync(s => s.Id == id && s.OwnerSub == sub);
    if (store is null) return Results.NotFound();

    store.Name = name;
    store.UpdatedAtUtc = DateTimeOffset.UtcNow;
    store.UpdatedBySub = sub;

    await db.SaveChangesAsync();

    return Results.Ok(ToDto(store));
})
.RequireAuthorization("stores:write")
.WithName("RenameStore");

app.MapDelete("/stores/{id:int}", async (HttpContext ctx, int id, AppDbContext db) =>
{
    var sub =
        ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    if (string.IsNullOrWhiteSpace(sub))
        return Results.Unauthorized();

    var store = await db.Stores.SingleOrDefaultAsync(s => s.Id == id && s.OwnerSub == sub);
    if (store is null) return Results.NotFound();

    store.IsDeleted = true;
    store.DeletedAtUtc = DateTimeOffset.UtcNow;
    store.DeletedBySub = sub;

    await db.SaveChangesAsync();
    return Results.NoContent();
})
.RequireAuthorization("stores:delete") // or "stores:write" if you don't add delete perm yet
.WithName("DeleteStore");

app.MapGet("/stores/deleted", async (HttpContext ctx, AppDbContext db) =>
{
    var sub = GetSub(ctx);

    var deleted = await db.Stores
        .IgnoreQueryFilters()
        .Where(s => s.OwnerSub == sub && s.IsDeleted)
        .OrderByDescending(s => s.DeletedAtUtc)
        .ToListAsync();

    return Results.Ok(deleted);
})
.RequireAuthorization("stores:read")
.WithName("ListDeletedStores");

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        utc = DateTimeOffset.UtcNow
    });
})
.WithName("Health");

app.MapGet("/", () => Results.Ok(new { name = "WooPulse API", status = "running" }))
   .WithName("Root");

app.Run();

record CreateStoreRequest(string Name);
record RenameStoreRequest(string Name);

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
