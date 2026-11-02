using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LpLedger.Core;
using LpLedger.Core.Aerodrome;
using LpLedger.Core.Models;
using LpLedger.Infrastructure.Chain;
using LpLedger.Infrastructure.Data;
using LpLedger.Infrastructure.Demo;
using LpLedger.Infrastructure.Prices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
// With a database, scanned history survives restarts and a refresh reads only new blocks. Without one it lives in memory.
var ledgerDb = builder.Configuration.GetConnectionString("Ledger");
if (string.IsNullOrWhiteSpace(ledgerDb)) builder.Services.AddSingleton<IScanStore, MemoryScanStore>();
else
{
    builder.Services.AddDbContextFactory<LedgerDb>(o => o.UseNpgsql(ledgerDb));
    builder.Services.AddSingleton<IScanStore, EfScanStore>();
}
builder.Services.AddHttpClient<LlamaPrices>(c => c.BaseAddress = new Uri("https://coins.llama.fi/"))
    .AddStandardResilienceHandler();
// Free-tier RPCs rate-limit in bursts, so the default circuit breaker would trip and fail whole lookups.
builder.Services.AddHttpClient("rpc").AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
    o.CircuitBreaker.MinimumThroughput = 1000;
    o.CircuitBreaker.FailureRatio = 1.0;
    o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
if (!string.IsNullOrWhiteSpace(ledgerDb))
{
    // No migrations yet (see STATUS.md): this creates the tables on first run and leaves existing ones alone.
    await using var db = await app.Services.GetRequiredService<IDbContextFactory<LedgerDb>>().CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
}
else app.Logger.LogWarning("ConnectionStrings:Ledger is not set. Scanned history is kept in memory and is lost on restart.");
app.UseCors();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

var rpcUrl = app.Configuration["Base:RpcUrl"];
var managers = app.Configuration.GetSection("Aerodrome:PositionManagers").Get<string[]>() is { Length: > 0 } configured
    ? configured : AlchemyChainReader.DefaultManagers;
if (string.IsNullOrWhiteSpace(rpcUrl))
    app.Logger.LogWarning("Base:RpcUrl is not set. Serving demo data; set it (env Base__RpcUrl) to read the real chain.");

app.MapGet("/health", () => new { ok = true, demo = string.IsNullOrWhiteSpace(rpcUrl) });

app.MapGet("/api/wallets/{address}/positions", async (
    string address, IMemoryCache cache, IHttpClientFactory http, LlamaPrices llama, IScanStore store, ILogger<Program> log,
    bool demo = false) =>
{
    if (!Regex.IsMatch(address, "^0x[0-9a-fA-F]{40}$"))
        return Results.BadRequest(new { error = "Enter a 42-character address starting with 0x." });

    // ?demo=true serves the simulated chain even when an RPC is configured, for working on the front-end.
    var useDemo = demo || string.IsNullOrWhiteSpace(rpcUrl);
    var key = (useDemo ? "demo:" : "") + address.ToLowerInvariant();
    if (cache.TryGetValue(key, out WalletResult? hit) && hit is not null) return Results.Ok(hit);
    try
    {
        WalletResult result;
        if (useDemo)
        {
            var chain = new DemoChain(address);
            result = await new AerodromeAdapter(chain, chain).GetPositionsAsync(address, demo: true);
        }
        else
        {
            var reader = new AlchemyChainReader(new AlchemyClient(http.CreateClient("rpc"), rpcUrl), managers, store);
            result = await new AerodromeAdapter(reader, llama).GetPositionsAsync(address);
        }
        cache.Set(key, result, TimeSpan.FromSeconds(60));
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Reading positions for {Address} failed", address);
        return Results.Json(new { error = "Could not read positions from Base. Try again in a minute." }, statusCode: 502);
    }
}).WithName("GetWalletPositions").Produces<WalletResult>();

app.Run();

public partial class Program;
