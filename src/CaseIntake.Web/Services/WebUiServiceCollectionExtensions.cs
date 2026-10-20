using CaseIntake.Web.Services;

namespace CaseIntake.Web;

public static class WebUiServiceCollectionExtensions
{
    /// <summary>
    /// The E5 UI services, all circuit-scoped: the polite status announcer, the shell state (title, breadcrumb, back
    /// target, focus-after-navigation memory), the JS interop module and the dashboard aggregation.
    /// </summary>
    public static IServiceCollection AddWebUi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<DashboardOptions>().BindConfiguration(DashboardOptions.SectionName)
            .Validate(o => o.NewCasesDays >= 1 && o.MaxCasesScanned >= 100 && o.TrendMonths is >= 1 and <= 36 && o.RecentActivityDays >= 1,
                "Dashboard settings are out of range.")
            .ValidateOnStart();
        services.AddScoped<StatusAnnouncer>();
        services.AddScoped<ShellState>();
        services.AddScoped<PageActivity>();
        services.AddSingleton<CaseActivitySignal>();
        services.AddScoped<UiInterop>();
        services.AddScoped<CaseDashboardService>();
        return services;
    }
}
