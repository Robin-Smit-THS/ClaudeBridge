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

        foreach (var (name, skill) in options.Skills)
        {
            logger.LogInformation(
                "Skill '{Name}': tools=[{Tools}] maxTurns={MaxTurns} timeout={Timeout}min",
                name, string.Join(",", skill.Tools), skill.MaxTurns ?? options.DefaultMaxTurns,
                skill.TimeoutMinutes ?? options.TimeoutMinutes);
        }
    }
}
