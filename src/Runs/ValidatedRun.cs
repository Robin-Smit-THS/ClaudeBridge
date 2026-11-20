namespace ClaudeBridge.Runs;

/// <summary>
/// Geprüfter und mit Standardwerten aufgefüllter Lauf – kann direkt an das Claude-CLI übergeben werden.
/// </summary>
public sealed record ValidatedRun(
    string Prompt,
    string WorkdirAlias,
    string WorkdirPath,
    IReadOnlyList<string> Tools,
    int MaxTurns,
    string? ResumeSessionId,
    string? Model,
    string? AppendSystemPrompt);
