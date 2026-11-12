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
        services.AddScoped<LpDashboard.Application.Pools.EntryPlanService>();
        services.AddScoped<LpDashboard.Application.Pools.WalletBalanceService>();
        services.AddScoped<LpDashboard.Application.Borrow.ManualLoanService>();
        services.AddScoped<LpDashboard.Application.Borrow.LendingSyncService>();
        services.AddScoped<LpDashboard.Application.Borrow.BorrowQueryService>();
        services.AddSingleton<LpDashboard.Application.Risk.RiskCheckGate>();
        services.AddScoped<LpDashboard.Application.Risk.RiskFactCollector>();
        services.AddScoped<LpDashboard.Application.Risk.RiskService>();
        services.AddScoped<LpDashboard.Application.Risk.RiskAlertService>();
        services.AddScoped<LpDashboard.Application.Risk.RiskMonitorService>();
        return services;
    }
}
