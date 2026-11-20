using System.Diagnostics;
using System.Text.Json;
using ClaudeBridge.Claude;

namespace ClaudeBridge.Runs;

/// <summary>
/// Führt einen geprüften Lauf aus: Slot reservieren, Zeitlimit setzen, Claude starten, Ergebnis auswerten.
/// </summary>
public sealed class ClaudeRunService(
    RunConcurrencyLimiter concurrencyLimiter,
    ClaudeProcessRunner processRunner,
    ILogger<ClaudeRunService> logger)
{
    public async Task<RunOutcome> ExecuteAsync(ValidatedRun run, CancellationToken requestAborted)
    {
        using var slot = await concurrencyLimiter.AcquireSlotAsync(requestAborted);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(run.TimeoutMinutes));
        using var timeoutOrAbort = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, requestAborted);

        var stopwatch = Stopwatch.StartNew();
        LogRunStart(run);

        var result = await processRunner.RunAsync(
            run.WorkdirPath,
            ClaudeCommandLine.BuildArguments(run),
            run.Prompt,
            timeoutOrAbort.Token);

        var outcome = ToOutcome(result, run.TimeoutMinutes, timedOut: timeout.IsCancellationRequested);
        LogRunEnd(result, stopwatch.Elapsed, timeout.IsCancellationRequested, requestAborted.IsCancellationRequested,
            ReadSessionId(outcome));

        return outcome;
    }

    /// <summary>Session-ID aus Claudes Antwort (<c>session_id</c>) oder null, wenn Claude keine geliefert hat.</summary>
    private static string? ReadSessionId(RunOutcome outcome)
    {
        if (outcome is not RunOutcome.Completed completed)
            return null;

        using var json = JsonDocument.Parse(completed.Json);
        return json.RootElement.TryGetProperty("session_id", out var sessionId) && sessionId.ValueKind == JsonValueKind.String
            ? sessionId.GetString()
            : null;
    }

    private static RunOutcome ToOutcome(ClaudeProcessResult result, int timeoutMinutes, bool timedOut)
    {
        if (result.WasCancelled)
        {
            return timedOut
                ? new RunOutcome.TimedOut(timeoutMinutes)
                : new RunOutcome.AbortedByClient();
        }

        var json = ClaudeOutputParser.ExtractJsonObject(result.Stdout);
        return json is not null
            ? new RunOutcome.Completed(json)
            : new RunOutcome.InvalidOutput(result.ExitCode, result.Stderr);
    }

    private void LogRunStart(ValidatedRun run) =>
        logger.LogInformation(
            "Run start: workdir={Workdir} skill={Skill} resumeSession={ResumeSession} tools=[{Tools}] maxTurns={MaxTurns} timeout={Timeout}min promptLen={Len}",
            run.WorkdirAlias, run.Skill ?? "-", run.ResumeSessionId ?? "-", string.Join(",", run.Tools),
            run.MaxTurns, run.TimeoutMinutes, run.Prompt.Length);

    private void LogRunEnd(ClaudeProcessResult result, TimeSpan duration, bool timedOut, bool aborted, string? sessionId) =>
        logger.LogInformation(
            "Run end: exit={Exit} dauer={Ms}ms timeout={Timeout} abgebrochen={Aborted} session={SessionId}",
            result.ExitCode, (long)duration.TotalMilliseconds, timedOut, aborted, sessionId ?? "-");
}
