using System.Text.RegularExpressions;

namespace ClaudeBridge.Skills;

/// <summary>
/// Ein Skill aus &lt;workdir&gt;/.claude/skills/&lt;name&gt;/SKILL.md.
/// </summary>
/// <param name="Name">Verzeichnisname des Skills – unter diesem Namen wird er als /name aufgerufen.</param>
/// <param name="UserInvocable">false, wenn die SKILL.md "user-invocable: false" setzt; dann ist kein /name-Aufruf möglich.</param>
public sealed record SkillInfo(string Name, string? Description, string? ArgumentHint, bool UserInvocable);

/// <summary>
/// Findet die Skills eines Arbeitsverzeichnisses. Liest bei jedem Aufruf neu von der Platte,
/// damit neue oder geänderte Skills ohne Neustart der Bridge verfügbar sind.
/// </summary>
public sealed partial class SkillCatalog
{
    private const string SkillsDirectory = ".claude/skills";
    private const string SkillFileName = "SKILL.md";

    /// <summary>Erlaubte Skill-Namen – verhindert u. a. Pfadangaben wie "../x".</summary>
    public static bool IsValidName(string name) => SkillNamePattern().IsMatch(name);

    public IReadOnlyList<SkillInfo> List(string workdirPath)
    {
        var skillsRoot = Path.Combine(workdirPath, SkillsDirectory);
        if (!Directory.Exists(skillsRoot))
            return [];

        return Directory.EnumerateDirectories(skillsRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null && IsValidName(name))
            .Select(name => Find(workdirPath, name!))
            .OfType<SkillInfo>()
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public SkillInfo? Find(string workdirPath, string name)
    {
        if (!IsValidName(name))
            return null;

        var skillFile = Path.Combine(workdirPath, SkillsDirectory, name, SkillFileName);
        if (!File.Exists(skillFile))
            return null;

        var frontmatter = ReadFrontmatter(skillFile);
        return new SkillInfo(
            Name: name,
            Description: frontmatter.GetValueOrDefault("description"),
            ArgumentHint: frontmatter.GetValueOrDefault("argument-hint"),
            UserInvocable: !string.Equals(frontmatter.GetValueOrDefault("user-invocable"), "false",
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Liest die einfachen "key: value"-Paare des YAML-Frontmatters (zwischen den "---"-Zeilen).
    /// Mehrzeilige Werte (">" / "|") werden zu einer Zeile zusammengefügt. Kein vollständiger YAML-Parser,
    /// reicht aber für die Felder, die die Bridge braucht.
    /// </summary>
    private static Dictionary<string, string> ReadFrontmatter(string skillFile)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadLines(skillFile).ToList();
        if (lines.Count == 0 || lines[0].Trim() != "---")
            return values;

        string? blockKey = null;
        var blockLines = new List<string>();

        void FlushBlock()
        {
            if (blockKey is not null)
                values[blockKey] = string.Join(" ", blockLines);
            blockKey = null;
            blockLines.Clear();
        }

        foreach (var line in lines.Skip(1))
        {
            if (line.Trim() == "---")
                break;

            if (blockKey is not null && line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                blockLines.Add(line.Trim());
                continue;
            }

            FlushBlock();

            var match = KeyValuePattern().Match(line);
            if (!match.Success)
                continue;

            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Trim();

            if (value is ">" or "|" or ">-" or "|-")
                blockKey = key;
            else
                values[key] = Unquote(value);
        }

        FlushBlock();
        return values;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
            ? value[1..^1]
            : value;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")]
    private static partial Regex SkillNamePattern();

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_-]+):\s*(?<value>.*)$")]
    private static partial Regex KeyValuePattern();
}
