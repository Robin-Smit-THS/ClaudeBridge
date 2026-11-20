using System.Security.Cryptography;
using System.Text;
using ClaudeBridge.Configuration;
using ClaudeBridge.Endpoints;

namespace ClaudeBridge.Authentication;

/// <summary>
/// Lässt nur Anfragen mit gültigem "Authorization: Bearer &lt;Token&gt;" durch – ausgenommen der Health-Check.
/// </summary>
public sealed class BearerTokenAuthenticationMiddleware(
    RequestDelegate next,
    BridgeOptions options,
    ILogger<BearerTokenAuthenticationMiddleware> logger)
{
    private const string BearerPrefix = "Bearer ";
    private readonly byte[] _expectedToken = Encoding.UTF8.GetBytes(options.Token);

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsAnonymousPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        var rejection = CheckBearerToken(context.Request);
        if (rejection is null)
        {
            await next(context);
            return;
        }

        // Grund nur ins Log, nicht in die Antwort – Angreifer sollen nichts über den Token erfahren.
        logger.LogWarning("Authentifizierung abgelehnt: {Reason}", rejection);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
    }

    private static bool IsAnonymousPath(PathString path) =>
        path.StartsWithSegments(HealthEndpoint.Path);

    /// <summary>Gibt null zurück, wenn der Token gültig ist, sonst den Ablehnungsgrund.</summary>
    private string? CheckBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
            return "Authorization-Header fehlt";

        if (!header.StartsWith(BearerPrefix, StringComparison.Ordinal))
            return "Authorization-Header ist kein 'Bearer <Token>'";

        var presentedToken = Encoding.UTF8.GetBytes(header[BearerPrefix.Length..].Trim());
        return CryptographicOperations.FixedTimeEquals(presentedToken, _expectedToken)
            ? null
            : "Token stimmt nicht";
    }
}
