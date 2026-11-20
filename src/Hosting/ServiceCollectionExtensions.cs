using ClaudeBridge.Claude;
using ClaudeBridge.Configuration;
using ClaudeBridge.Runs;
using ClaudeBridge.Skills;

namespace ClaudeBridge.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClaudeBridge(
        this IServiceCollection services,
        BridgeOptions options,
        ClaudeExecutable executable)
    {
        // Ungültige Request-Bodies (z. B. kaputtes JSON) werfen eine Exception statt einer leeren 400,
        // damit RequestLoggingMiddleware die Begründung zurückgeben und loggen kann.
        services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

        services.AddSingleton(options);
        services.AddSingleton(executable);
        services.AddSingleton<ClaudeProcessRunner>();
        services.AddSingleton<RunConcurrencyLimiter>();
        services.AddSingleton<SkillCatalog>();
        services.AddSingleton<RunRequestValidator>();
        services.AddSingleton<ClaudeRunService>();
        return services;
    }
}
