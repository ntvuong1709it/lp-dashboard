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
        services.AddSingleton<LpDashboard.Application.Auth.LoginThrottle>();
        services.AddScoped<LpDashboard.Application.Auth.AuthService>();
        services.AddScoped<SyncService>();
        services.AddScoped<PriceService>();
        services.AddScoped<MarketTickerService>();
        services.AddScoped<PositionQueryService>();
        services.AddScoped<LpDashboard.Application.Pools.PoolExplorerService>();
        services.AddScoped<LpDashboard.Application.Borrow.ManualLoanService>();
        services.AddScoped<LpDashboard.Application.Borrow.LendingSyncService>();
        services.AddScoped<LpDashboard.Application.Borrow.BorrowQueryService>();
        return services;
    }
}
