namespace ClaudeBridge.Configuration;

/// <summary>
/// Einstellungen aus dem Abschnitt "Bridge" der appsettings.json bzw. aus Umgebungsvariablen (Bridge__*).
/// </summary>
public sealed class BridgeOptions
{
    public const string SectionName = "Bridge";
    public const int MinimumTokenLength = 32;

    private static readonly string[] ReadOnlyTools = ["Read", "Grep", "Glob"];

    public string Token { get; init; } = "";
    public string? ClaudePath { get; init; }
    public Dictionary<string, string> Workdirs { get; set; } = new();
    public string[] AllowedTools { get; set; } = [];
    public string[] DefaultTools { get; set; } = [];
    public int DefaultMaxTurns { get; init; } = 15;
    public int MaxMaxTurns { get; init; } = 50;
    public int TimeoutMinutes { get; init; } = 15;
    public int MaxConcurrentRuns { get; init; } = 1;

    public static BridgeOptions Load(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<BridgeOptions>() ?? new BridgeOptions();
        options.ApplyDefaults();
        options.EnsureTokenIsStrongEnough();
        return options;
    }

    private void ApplyDefaults()
    {
        Workdirs = new Dictionary<string, string>(Workdirs, StringComparer.OrdinalIgnoreCase);

        if (AllowedTools.Length == 0)
            AllowedTools = ReadOnlyTools;

        if (DefaultTools.Length == 0)
            DefaultTools = ReadOnlyTools;
    }

    private void EnsureTokenIsStrongEnough()
    {
        if (string.IsNullOrWhiteSpace(Token) || Token.Length < MinimumTokenLength)
            throw new InvalidOperationException(
                $"Bridge:Token fehlt oder ist zu kurz (min. {MinimumTokenLength} Zeichen). " +
                "Per Umgebungsvariable 'Bridge__Token' oder in appsettings.json setzen.");
    }
}
