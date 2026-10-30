using LpDashboard.Application.Abstractions;
using LpDashboard.Infrastructure.Evm;
using LpDashboard.Infrastructure.Persistence;
using LpDashboard.Infrastructure.Pricing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Infrastructure;

public static class DependencyInjection
{
    public const string SqliteMigrationsAssembly = "LpDashboard.Infrastructure.Migrations.Sqlite";
    public const string PostgresMigrationsAssembly = "LpDashboard.Infrastructure.Migrations.Postgres";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, string? contentRoot = null)
    {
        services.AddPersistence(configuration, contentRoot);
        services.AddPricing(configuration);
        services.AddEvmProviders(configuration);
        return services;
    }

    /// <summary>Database provider is selected by "Database:Provider" (Sqlite | Postgres); each provider has its own migrations assembly.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration, string? contentRoot = null)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=lpdashboard.db";

        services.AddDbContext<AppDbContext>(options =>
        {
            switch (provider.ToLowerInvariant())
            {
                case "sqlite":
                    options.UseSqlite(ResolveSqlitePath(connectionString, contentRoot), o => o.MigrationsAssembly(SqliteMigrationsAssembly));
                    break;
                case "postgres":
                case "postgresql":
                    options.UseNpgsql(connectionString, o => o.MigrationsAssembly(PostgresMigrationsAssembly));
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Database:Provider '{provider}'. Use Sqlite or Postgres.");
            }
        });

        services.AddScoped<IWatchedAddressRepository, WatchedAddressRepository>();
        services.AddScoped<ITokenRepository, TokenRepository>();
        services.AddScoped<IPoolRepository, PoolRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IPositionEventRepository, PositionEventRepository>();
        services.AddScoped<ISnapshotRepository, SnapshotRepository>();
        services.AddScoped<ITokenPriceRepository, TokenPriceRepository>();
        services.AddScoped<ISyncCursorRepository, SyncCursorRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }

    private static string ResolveSqlitePath(string connectionString, string? contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (contentRoot is not null && builder.DataSource != ":memory:" && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.Combine(contentRoot, builder.DataSource);
        }
        return builder.ToString();
    }

    private static void AddPricing(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["Pricing:DefiLlamaBaseUrl"] ?? "https://coins.llama.fi/";
        services.AddHttpClient<IPriceSource, DefiLlamaPriceSource>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }

    private static void AddEvmProviders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EvmOptions>(configuration.GetSection(EvmOptions.SectionName));
        services.AddHttpClient(EvmRpcClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(90));

        var options = configuration.GetSection(EvmOptions.SectionName).Get<EvmOptions>() ?? new EvmOptions();
        foreach (var chain in options.Chains.Where(c => !string.IsNullOrWhiteSpace(c.RpcUrl)))
        {
            // One client per chain so all protocols share the same request pacing.
            EvmRpcClient? rpc = null;
            foreach (var protocol in chain.Protocols.Where(p => p.Enabled && UniswapV3StyleProvider.Supports(p.Protocol)))
            {
                services.AddSingleton<IPositionProvider>(sp => new UniswapV3StyleProvider(
                    rpc ??= new EvmRpcClient(sp.GetRequiredService<IHttpClientFactory>(), chain),
                    chain,
                    protocol,
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger<UniswapV3StyleProvider>()));
            }
        }
    }
}
