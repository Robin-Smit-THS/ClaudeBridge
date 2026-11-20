namespace ClaudeBridge.Claude;

/// <summary>
/// Pfad zum lokal installierten Claude-Code-CLI.
/// </summary>
public sealed record ClaudeExecutable(string FilePath)
{
    /// <summary>
    /// Die npm-Installation liefert claude.cmd – das muss über cmd.exe gestartet werden.
    /// </summary>
    public bool RequiresCommandShell =>
        FilePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);

    public static ClaudeExecutable Locate(string? configuredPath)
    {
        var filePath = FindFilePath(configuredPath)
            ?? throw new InvalidOperationException(
                "claude wurde nicht gefunden. Bridge:ClaudePath in appsettings.json auf den vollen Pfad setzen " +
                "(z. B. C:\\Users\\<du>\\.local\\bin\\claude.exe).");

        return new ClaudeExecutable(filePath);
    }

    private static string? FindFilePath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return File.Exists(configuredPath) ? configuredPath : null;

        var searchDirectories = GetSearchDirectories().ToList();

        return GetExecutableNames()
            .SelectMany(name => searchDirectories.Select(directory => Path.Combine(directory, name)))
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> GetSearchDirectories()
    {
        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var userLocalBin = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");

        return pathDirectories.Append(userLocalBin);
    }

    private static string[] GetExecutableNames() =>
        OperatingSystem.IsWindows() ? ["claude.exe", "claude.cmd"] : ["claude"];
}
