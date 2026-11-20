namespace ClaudeBridge.Endpoints;

/// <summary>
/// GET /health – Lebenszeichen ohne Authentifizierung.
/// </summary>
public static class HealthEndpoint
{
    public const string Path = "/health";

    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, () => Results.Ok(new { status = "ok" }));
        return endpoints;
    }
}
