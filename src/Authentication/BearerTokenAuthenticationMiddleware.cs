using System.Security.Cryptography;
using System.Text;
using ClaudeBridge.Configuration;
using ClaudeBridge.Endpoints;

namespace ClaudeBridge.Authentication;

/// <summary>
/// Lässt nur Anfragen mit gültigem "Authorization: Bearer &lt;Token&gt;" durch – ausgenommen der Health-Check.
/// </summary>
public sealed class BearerTokenAuthenticationMiddleware(RequestDelegate next, BridgeOptions options)
{
    private const string BearerPrefix = "Bearer ";
    private readonly byte[] _expectedToken = Encoding.UTF8.GetBytes(options.Token);

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsAnonymousPath(context.Request.Path) || HasValidBearerToken(context.Request))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
    }

    private static bool IsAnonymousPath(PathString path) =>
        path.StartsWithSegments(HealthEndpoint.Path);

    private bool HasValidBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.Ordinal))
            return false;

        var presentedToken = Encoding.UTF8.GetBytes(header[BearerPrefix.Length..].Trim());
        return CryptographicOperations.FixedTimeEquals(presentedToken, _expectedToken);
    }
}
