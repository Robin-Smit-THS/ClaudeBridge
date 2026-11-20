using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using ClaudeBridge.Configuration;

namespace ClaudeBridge.Runs;

/// <summary>
/// Prüft einen <see cref="RunRequest"/> gegen die Konfiguration und macht daraus einen <see cref="ValidatedRun"/>.
/// </summary>
public sealed partial class RunRequestValidator(BridgeOptions options)
{
    private const int MaxPromptLength = 100_000;

    public bool TryValidate(
        RunRequest request,
        [NotNullWhen(true)] out ValidatedRun? run,
        [NotNullWhen(false)] out object? error)
    {
        var tools = request.AllowedTools is { Length: > 0 } ? request.AllowedTools : options.DefaultTools;

        error = ValidatePrompt(request.Prompt)
             ?? ValidateWorkdir(request.Workdir)
             ?? ValidateTools(tools)
             ?? ValidateResumeSessionId(request.ResumeSessionId)
             ?? ValidateModel(request.Model);

        if (error is not null)
        {
            run = null;
            return false;
        }

        run = new ValidatedRun(
            Prompt: request.Prompt!,
            WorkdirAlias: request.Workdir!,
            WorkdirPath: options.Workdirs[request.Workdir!],
            Tools: tools,
            MaxTurns: Math.Clamp(request.MaxTurns ?? options.DefaultMaxTurns, 1, options.MaxMaxTurns),
            ResumeSessionId: request.ResumeSessionId,
            Model: request.Model,
            AppendSystemPrompt: request.AppendSystemPrompt);
        return true;
    }

    private static object? ValidatePrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return new { error = "prompt fehlt" };

        if (prompt.Length > MaxPromptLength)
            return new { error = "prompt zu lang (max. 100.000 Zeichen)" };

        return null;
    }

    private object? ValidateWorkdir(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias) || !options.Workdirs.TryGetValue(alias, out var path))
            return new { error = $"unbekanntes workdir '{alias}'", erlaubt = options.Workdirs.Keys };

        if (!Directory.Exists(path))
            return new { error = $"workdir '{alias}' existiert nicht auf dem Host" };

        return null;
    }

    private object? ValidateTools(string[] tools)
    {
        var forbiddenTools = tools
            .Where(tool => !options.AllowedTools.Contains(tool, StringComparer.Ordinal))
            .ToArray();

        return forbiddenTools.Length > 0
            ? new { error = "nicht freigegebene Tools", tools = forbiddenTools, erlaubt = options.AllowedTools }
            : null;
    }

    private static object? ValidateResumeSessionId(string? sessionId) =>
        sessionId is not null && !SessionIdPattern().IsMatch(sessionId)
            ? new { error = "ungültige resumeSessionId" }
            : null;

    private static object? ValidateModel(string? model) =>
        model is not null && !ModelNamePattern().IsMatch(model)
            ? new { error = "ungültiges model" }
            : null;

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex SessionIdPattern();

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex ModelNamePattern();
}
