using System.Diagnostics;
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

        LogRunEnd(result, stopwatch.Elapsed, timeout.IsCancellationRequested, requestAborted.IsCancellationRequested);

        return ToOutcome(result, run.TimeoutMinutes, timedOut: timeout.IsCancellationRequested);
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
            "Run start: workdir={Workdir} skill={Skill} resume={Resume} tools=[{Tools}] maxTurns={MaxTurns} timeout={Timeout}min promptLen={Len}",
            run.WorkdirAlias, run.Skill ?? "-", run.ResumeSessionId is not null, string.Join(",", run.Tools),
            run.MaxTurns, run.TimeoutMinutes, run.Prompt.Length);

    private void LogRunEnd(ClaudeProcessResult result, TimeSpan duration, bool timedOut, bool aborted) =>
        logger.LogInformation(
            "Run end: exit={Exit} dauer={Ms}ms timeout={Timeout} abgebrochen={Aborted}",
            result.ExitCode, (long)duration.TotalMilliseconds, timedOut, aborted);
}
