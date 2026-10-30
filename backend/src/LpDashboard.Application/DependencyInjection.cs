using LpDashboard.Application.Pricing;
using LpDashboard.Application.Queries;
using LpDashboard.Application.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace LpDashboard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SyncCoordinator>();
        services.AddScoped<SyncService>();
        services.AddScoped<PriceService>();
        services.AddScoped<PositionQueryService>();
        return services;
    }
}
