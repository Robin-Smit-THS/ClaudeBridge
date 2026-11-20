using ClaudeBridge.Configuration;
using ClaudeBridge.Skills;

namespace ClaudeBridge.Endpoints;

/// <summary>
/// GET /skills?workdir=&lt;alias&gt; – listet die Skills aus &lt;workdir&gt;/.claude/skills
/// mitsamt den Tools und Limits, mit denen die Bridge sie ausführen würde.
/// </summary>
public static class SkillsEndpoint
{
    public const string Path = "/skills";

    public static IEndpointRouteBuilder MapSkillsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, Handle);
        return endpoints;
    }

    private static IResult Handle(string? workdir, BridgeOptions options, SkillCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(workdir) || !options.Workdirs.TryGetValue(workdir, out var path))
            return Results.BadRequest(new { error = $"unbekanntes workdir '{workdir}'", erlaubt = options.Workdirs.Keys });

        var skills = catalog.List(path).Select(skill =>
        {
            var skillOptions = options.GetSkillOptions(skill.Name);
            return new
            {
                name = skill.Name,
                description = skill.Description,
                argumentHint = skill.ArgumentHint,
                aufrufbar = skill.UserInvocable,
                konfiguriert = skillOptions is not null,
                tools = skillOptions?.Tools is { Length: > 0 } tools ? tools : options.DefaultTools,
                maxTurns = skillOptions?.MaxTurns ?? options.DefaultMaxTurns,
                timeoutMinutes = skillOptions?.TimeoutMinutes ?? options.TimeoutMinutes
            };
        });

        return Results.Ok(new { workdir, skills });
    }
}
