namespace ClaudeBridge.Runs;

/// <summary>
/// Request-Body von POST /run, so wie er vom Client (n8n) kommt – noch ungeprüft.
/// </summary>
public sealed record RunRequest(
    string? Prompt,
    string? Workdir,
    string[]? AllowedTools,
    int? MaxTurns,
    string? ResumeSessionId,
    string? Model,
    string? AppendSystemPrompt);
