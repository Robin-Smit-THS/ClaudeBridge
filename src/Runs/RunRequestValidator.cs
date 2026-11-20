using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using ClaudeBridge.Configuration;
using ClaudeBridge.Skills;

namespace ClaudeBridge.Runs;

/// <summary>
/// Prüft einen <see cref="RunRequest"/> gegen die Konfiguration und macht daraus einen <see cref="ValidatedRun"/>.
/// </summary>
public sealed partial class RunRequestValidator(BridgeOptions options, SkillCatalog skillCatalog)
{
    private const int MaxPromptLength = 100_000;

    public bool TryValidate(
        RunRequest request,
        [NotNullWhen(true)] out ValidatedRun? run,
        [NotNullWhen(false)] out object? error)
    {
        run = null;
        var skillOptions = options.GetSkillOptions(request.Skill);
        var tools = SelectTools(request, skillOptions);

        error = ValidateWorkdir(request.Workdir)
             ?? ValidateSkill(request.Skill, request.Workdir!)
             ?? ValidatePrompt(request)
             ?? ValidateTools(tools, skillOptions)
             ?? ValidateResumeSessionId(request.ResumeSessionId)
             ?? ValidateModel(request.Model);

        if (error is not null)
            return false;

        var maxTurnsLimit = skillOptions?.MaxTurns ?? options.MaxMaxTurns;
        var defaultMaxTurns = skillOptions?.MaxTurns ?? options.DefaultMaxTurns;

        run = new ValidatedRun(
            Prompt: BuildPrompt(request),
            WorkdirAlias: request.Workdir!,
            WorkdirPath: options.Workdirs[request.Workdir!],
            Tools: tools,
            MaxTurns: Math.Clamp(request.MaxTurns ?? defaultMaxTurns, 1, maxTurnsLimit),
            ResumeSessionId: request.ResumeSessionId,
            Model: request.Model,
            AppendSystemPrompt: request.AppendSystemPrompt,
            Skill: request.Skill,
            TimeoutMinutes: skillOptions?.TimeoutMinutes ?? options.TimeoutMinutes);
        return true;
    }

    /// <summary>
    /// Explizit angeforderte Tools haben Vorrang, sonst die Tools des Skills, sonst die globalen DefaultTools.
    /// </summary>
    private string[] SelectTools(RunRequest request, SkillOptions? skillOptions)
    {
        if (request.AllowedTools is { Length: > 0 })
            return request.AllowedTools;

        return skillOptions?.Tools is { Length: > 0 } skillTools ? skillTools : options.DefaultTools;
    }

    /// <summary>
    /// Neuer Skill-Lauf: "/skill argumente" – Claude Code expandiert das wie im interaktiven Modus.
    /// Folgeanfrage (resumeSessionId): nur die Antwort, damit der Skill nicht von vorn startet.
    /// </summary>
    private static string BuildPrompt(RunRequest request) =>
        request.Skill is not null && request.ResumeSessionId is null
            ? $"/{request.Skill} {request.Prompt}".TrimEnd()
            : request.Prompt!;

    private static object? ValidatePrompt(RunRequest request)
    {
        // Bei einem neuen Skill-Lauf sind Argumente optional; sonst (auch bei resume) ist ein Prompt nötig.
        var promptRequired = request.Skill is null || request.ResumeSessionId is not null;
        if (promptRequired && string.IsNullOrWhiteSpace(request.Prompt))
            return new { error = "prompt fehlt" };

        if (request.Prompt is { Length: > MaxPromptLength })
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

    private object? ValidateSkill(string? skill, string workdirAlias)
    {
        if (skill is null)
            return null;

        var workdirPath = options.Workdirs[workdirAlias];
        var found = skillCatalog.Find(workdirPath, skill);

        if (found is null)
        {
            return new
            {
                error = $"unbekannter skill '{skill}' in workdir '{workdirAlias}'",
                verfuegbar = skillCatalog.List(workdirPath).Where(s => s.UserInvocable).Select(s => s.Name)
            };
        }

        return found.UserInvocable
            ? null
            : new { error = $"skill '{skill}' ist nicht direkt aufrufbar (user-invocable: false)" };
    }

    private object? ValidateTools(string[] tools, SkillOptions? skillOptions)
    {
        var permitted = options.AllowedTools.Concat(skillOptions?.Tools ?? []).ToHashSet(StringComparer.Ordinal);
        var forbiddenTools = tools.Where(tool => !permitted.Contains(tool)).ToArray();

        return forbiddenTools.Length > 0
            ? new { error = "nicht freigegebene Tools", tools = forbiddenTools, erlaubt = permitted }
            : null;
    }

    /// <summary>Format einer Claude-Session-ID; gilt für resumeSessionId und für die session_id in Claudes Antwort.</summary>
    public static bool IsValidSessionId(string sessionId) => SessionIdPattern().IsMatch(sessionId);

    private static object? ValidateResumeSessionId(string? sessionId) =>
        sessionId is not null && !IsValidSessionId(sessionId)
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
