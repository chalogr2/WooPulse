using WooPulse.Api.Endpoints;
using WooPulse.Api.Setup;

// Program.cs is the app's "table of contents" (DEVLOG #009).
// Each line below is an extension method defined in Setup/ or Endpoints/.
// To find out what one does: Ctrl+Click it (Go to Definition).

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1) SERVICES: register everything in the dependency-injection container.
//    Order here doesn't matter; nothing runs yet.
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();   // Swagger needs this for minimal APIs
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();         // errors become RFC 7807 JSON { title, detail, status }

builder.Services.AddWooPulseDatabase(builder.Configuration);  // Setup/DatabaseSetup.cs
builder.Services.AddWooPulseAuth(builder.Configuration);      // Setup/AuthSetup.cs
builder.Services.AddWooPulseHangfire(builder.Configuration);  // Setup/HangfireSetup.cs
builder.Services.AddWooPulseServices();                       // Setup/AppServicesSetup.cs

var app = builder.Build();

// ---------------------------------------------------------------------------
// 2) MIDDLEWARE PIPELINE: every request passes through these IN ORDER.
//    Order matters: each one wraps everything registered after it.
// ---------------------------------------------------------------------------
app.UseExceptionHandler();      // FIRST, so it catches exceptions from everything below
app.UseHttpsRedirection();
app.UseCors(AppServicesSetup.LocalDevCorsPolicy);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();           // /swagger: interactive API docs
    app.UseSwaggerUI();
}
app.UseWooPulseHangfireDashboard();   // /hangfire, Development only

app.UseAuthentication();        // read + validate the JWT → sets the ClaimsPrincipal
app.UseAuthorization();         // then enforce .RequireAuthorization() on endpoints

// ---------------------------------------------------------------------------
// 3) BACKGROUND JOBS: register the recurring schedule (Hangfire)
// ---------------------------------------------------------------------------
app.RegisterWooPulseRecurringJobs();

// ---------------------------------------------------------------------------
// 4) ENDPOINTS: one file per feature in Endpoints/
// ---------------------------------------------------------------------------
app.MapSystemEndpoints();       // /, /health, /me
app.MapStoreEndpoints();        // /stores...
app.MapMemberEndpoints();       // /stores/{id}/members..., /stores/{id}/leave
app.MapInviteEndpoints();       // /stores/{id}/invites, /invites...

if (app.Environment.IsDevelopment())
{
    app.MapDevEndpoints();      // /debug/claims, /invites/mine/debug, /jobs/...
}

app.Run();
