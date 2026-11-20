using ClaudeBridge.Claude;
using ClaudeBridge.Configuration;
using ClaudeBridge.Runs;

namespace ClaudeBridge.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClaudeBridge(
        this IServiceCollection services,
        BridgeOptions options,
        ClaudeExecutable executable)
    {
        services.AddSingleton(options);
        services.AddSingleton(executable);
        services.AddSingleton<ClaudeProcessRunner>();
        services.AddSingleton<RunConcurrencyLimiter>();
        services.AddSingleton<RunRequestValidator>();
        services.AddSingleton<ClaudeRunService>();
        return services;
    }
}
