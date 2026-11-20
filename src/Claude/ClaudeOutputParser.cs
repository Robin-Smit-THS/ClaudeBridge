using System.Text.Json;

namespace ClaudeBridge.Claude;

/// <summary>
/// "claude -p --output-format json" liefert genau ein JSON-Objekt. Falls davor noch andere Ausgaben
/// stehen (z. B. Warnungen), wird das letzte gültige JSON-Objekt auf einer eigenen Zeile verwendet.
/// </summary>
public static class ClaudeOutputParser
{
    public static string? ExtractJsonObject(string stdout) =>
        GetCandidatesMostLikelyFirst(stdout).FirstOrDefault(IsJsonObject);

    private static IEnumerable<string> GetCandidatesMostLikelyFirst(string stdout)
    {
        yield return stdout.Trim();

        var linesFromLastToFirst = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Reverse();

        foreach (var line in linesFromLastToFirst)
            yield return line;
    }

    private static bool IsJsonObject(string candidate)
    {
        if (!candidate.StartsWith('{'))
            return false;

        try
        {
            using var _ = JsonDocument.Parse(candidate);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
