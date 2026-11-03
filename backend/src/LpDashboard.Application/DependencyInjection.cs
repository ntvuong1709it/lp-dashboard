using LpDashboard.Application.Pricing;
using LpDashboard.Application.Queries;
using LpDashboard.Application.Sync;
using LpDashboard.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LpDashboard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SyncCoordinator>();
        services.TryAddSingleton<IAlertNotifier, NullAlertNotifier>();
        services.AddScoped<SyncService>();
        services.AddScoped<PriceService>();
        services.AddScoped<PositionQueryService>();
        services.AddScoped<Export.ExportService>();
        return services;
    }
}
