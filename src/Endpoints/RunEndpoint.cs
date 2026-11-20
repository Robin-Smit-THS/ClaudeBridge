using System.Diagnostics;
using System.Text;
using ClaudeBridge.Runs;

namespace ClaudeBridge.Endpoints;

/// <summary>
/// POST /run – startet einen Claude-Code-Lauf und gibt dessen JSON-Ergebnis zurück.
/// </summary>
public static class RunEndpoint
{
    public const string Path = "/run";

    /// <summary>Nicht-standardisierter Statuscode (nginx) für "Client hat die Verbindung geschlossen".</summary>
    private const int StatusClientClosedRequest = 499;

    private const int MaxDiagnosticOutputLength = 4000;

    public static IEndpointRouteBuilder MapRunEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        RunRequest request,
        RunRequestValidator validator,
        ClaudeRunService runService,
        HttpContext context)
    {
        if (!validator.TryValidate(request, out var run, out var error))
            return Results.BadRequest(error);

        var outcome = await runService.ExecuteAsync(run, context.RequestAborted);
        return ToHttpResult(outcome);
    }

    private static IResult ToHttpResult(RunOutcome outcome) => outcome switch
    {
        // Claudes JSON wird unverändert durchgereicht
        RunOutcome.Completed completed =>
            Results.Content(completed.Json, "application/json", Encoding.UTF8),

        RunOutcome.InvalidOutput invalid => Results.Json(new
        {
            error = "Claude hat kein gültiges JSON geliefert",
            exitCode = invalid.ExitCode,
            // stdout bewusst nicht: kann Inhalte aus dem Workdir enthalten und landet sonst im Log
            stderr = Truncate(invalid.Stderr)
        }, statusCode: StatusCodes.Status502BadGateway),

        RunOutcome.TimedOut timedOut => Results.Json(
            new { error = $"Timeout nach {timedOut.TimeoutMinutes} min, Lauf abgebrochen" },
            statusCode: StatusCodes.Status504GatewayTimeout),

        RunOutcome.AbortedByClient => Results.Json(
            new { error = "Anfrage vom Client abgebrochen" },
            statusCode: StatusClientClosedRequest),

        _ => throw new UnreachableException($"Unbekanntes Ergebnis: {outcome.GetType().Name}")
    };

    private static string Truncate(string text) =>
        text.Length <= MaxDiagnosticOutputLength ? text : text[..MaxDiagnosticOutputLength] + " …";
}
