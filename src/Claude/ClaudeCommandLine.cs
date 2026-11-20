using System.Globalization;
using ClaudeBridge.Runs;

namespace ClaudeBridge.Claude;

/// <summary>
/// Baut die Argumente für "claude -p". Der Prompt selbst geht über stdin, nicht über die Kommandozeile.
/// </summary>
public static class ClaudeCommandLine
{
    public static IReadOnlyList<string> BuildArguments(ValidatedRun run)
    {
        var arguments = new List<string>
        {
            "-p",
            "--output-format", "json",
            "--permission-mode", "default",
            "--max-turns", run.MaxTurns.ToString(CultureInfo.InvariantCulture)
        };

        if (run.Tools.Count > 0)
            arguments.AddOption("--allowedTools", string.Join(",", run.Tools));

        if (run.ResumeSessionId is not null)
            arguments.AddOption("--resume", run.ResumeSessionId);

        if (run.Model is not null)
            arguments.AddOption("--model", run.Model);

        if (!string.IsNullOrWhiteSpace(run.AppendSystemPrompt))
            arguments.AddOption("--append-system-prompt", run.AppendSystemPrompt);

        return arguments;
    }

    private static void AddOption(this List<string> arguments, string name, string value)
    {
        arguments.Add(name);
        arguments.Add(value);
    }
}
