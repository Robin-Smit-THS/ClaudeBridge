namespace ClaudeBridge.Runs;

/// <summary>
/// Mögliche Ergebnisse eines Claude-Laufs.
/// </summary>
public abstract record RunOutcome
{
    private RunOutcome() { }

    /// <summary>Claude hat ein gültiges JSON-Objekt geliefert.</summary>
    public sealed record Completed(string Json) : RunOutcome;

    /// <summary>Der Prozess ist beendet, hat aber kein gültiges JSON geliefert.</summary>
    public sealed record InvalidOutput(int ExitCode, string Stdout, string Stderr) : RunOutcome;

    /// <summary>Der Lauf hat das konfigurierte Zeitlimit überschritten und wurde beendet.</summary>
    public sealed record TimedOut(int TimeoutMinutes) : RunOutcome;

    /// <summary>Der Client hat die HTTP-Anfrage abgebrochen.</summary>
    public sealed record AbortedByClient : RunOutcome;
}
