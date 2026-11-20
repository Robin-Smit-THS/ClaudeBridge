namespace ClaudeBridge.Runs;

/// <summary>
/// Request-Body von POST /run, so wie er vom Client (n8n) kommt – noch ungeprüft.
/// Mit <c>Skill</c> wird der Lauf als "/skill prompt" gestartet; <c>Prompt</c> sind dann die Argumente des Skills.
/// </summary>
public sealed record RunRequest(
    string? Prompt,
    string? Workdir,
    string[]? AllowedTools,
    int? MaxTurns,
    string? ResumeSessionId,
    string? Model,
    string? AppendSystemPrompt,
    string? Skill = null);
