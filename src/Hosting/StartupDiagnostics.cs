using ClaudeBridge.Claude;
using ClaudeBridge.Configuration;

namespace ClaudeBridge.Hosting;

/// <summary>
/// Schreibt beim Start die wichtigsten Einstellungen ins Log, damit Fehlkonfigurationen sofort auffallen.
/// </summary>
public static class StartupDiagnostics
{
    public static void LogConfiguration(ILogger logger, BridgeOptions options, ClaudeExecutable executable)
    {
        logger.LogInformation("Claude CLI: {Path}", executable.FilePath);

        foreach (var (alias, path) in options.Workdirs)
        {
            if (Directory.Exists(path))
                logger.LogInformation("Workdir '{Alias}' -> {Path}", alias, path);
            else
                logger.LogWarning("Workdir '{Alias}' -> {Path} existiert NICHT", alias, path);
        }
    }
}
