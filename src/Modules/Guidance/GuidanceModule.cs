using Cracra.Modules.Guidance.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Guidance;

public static class GuidanceModule
{
    public static IServiceCollection AddGuidanceModule(this IServiceCollection services)
    {
        services.AddScoped<IShellNavigationService, ShellNavigationService>();
        services.AddScoped<IObligationService, ObligationService>();
        services.AddScoped<INextActionService, NextActionService>();

        return services;
    }
}
