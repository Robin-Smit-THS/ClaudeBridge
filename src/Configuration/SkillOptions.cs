namespace ClaudeBridge.Configuration;

/// <summary>
/// Freigaben für einen bestimmten Skill (Abschnitt "Bridge:Skills:&lt;name&gt;").
/// Skills ohne Eintrag laufen mit den normalen Einstellungen (DefaultTools, DefaultMaxTurns, TimeoutMinutes).
/// </summary>
public sealed class SkillOptions
{
    /// <summary>
    /// Tools, die dieser Skill bekommt. Sie sind Standard für Läufe mit diesem Skill und dürfen
    /// dort zusätzlich zu den globalen AllowedTools angefordert werden.
    /// </summary>
    public string[] Tools { get; set; } = [];

    /// <summary>Standard und Obergrenze für maxTurns bei diesem Skill (statt DefaultMaxTurns/MaxMaxTurns).</summary>
    public int? MaxTurns { get; init; }

    /// <summary>Zeitlimit für Läufe mit diesem Skill (statt TimeoutMinutes).</summary>
    public int? TimeoutMinutes { get; init; }
}
