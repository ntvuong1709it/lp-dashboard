using System.Text.Json.Serialization;
using LpDashboard.Api.Auth;
using LpDashboard.Api.Background;
using LpDashboard.Application;
using LpDashboard.Application.Auth;
using LpDashboard.Application.Sync;
using LpDashboard.Infrastructure;
using LpDashboard.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddHostedService<SyncBackgroundService>();
builder.Services.AddHostedService<RiskBackgroundService>();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(corsOrigins).AllowAnyHeader().WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    // A requested password reset signs every device out before anyone can use the old sessions.
    await scope.ServiceProvider.GetRequiredService<AuthService>().ApplyPendingResetAsync(CancellationToken.None);
}

// Behind Azure's TLS-terminating proxy the app only sees http; trust X-Forwarded-Proto/Host so Request.Scheme/IsHttps
// (origin check, Secure cookie) reflect what the browser used. The proxy is the only way in, so default networks are cleared.
var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost };
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseMiddleware<AuthMiddleware>();

// Serve the built frontend (Docker image / `npm run build` copied into wwwroot) when present.
var spa = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
if (spa.Exists)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapControllers();

if (spa.Exists)
{
    // Client-side routes fall back to index.html; unknown /api paths and missing files (anything with an extension) still 404.
    app.MapFallbackToFile("{**path:regex(^(?!api(/|$))[^.]*$)}", "index.html");
}

await app.RunAsync();
