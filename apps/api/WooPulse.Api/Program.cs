using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

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

builder.Services.AddAuthorization();
builder.Services.AddHangfireServer();

var app = builder.Build();

app.UseCors("LocalDev");

app.UseHangfireDashboard(builder.Configuration["Hangfire:DashboardPath"] ?? "/hangfire");

app.UseAuthentication();
app.UseAuthorization();

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

app.MapGet("/stores", async (AppDbContext db) =>
{
    var stores = await db.Stores
        .OrderByDescending(s => s.Id)
        .ToListAsync();

    return Results.Ok(stores);
})
.WithName("ListStores");

app.MapGet("/me", (HttpContext ctx) =>
{
    return Results.Ok(new
    {
        name = ctx.User.Identity?.Name,
        claims = ctx.User.Claims.Select(c => new { c.Type, c.Value })
    });
})
.RequireAuthorization()
.WithName("Me");

app.MapPost("/stores", async (CreateStoreRequest req, AppDbContext db) =>
{
    var store = new Store
    {
        Name = req.Name,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    db.Stores.Add(store);
    await db.SaveChangesAsync();

    return Results.Created($"/stores/{store.Id}", store);
})
.RequireAuthorization()   
.WithName("CreateStore");

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

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
