using System.Diagnostics;
using ClaudeBridge.Claude;
using ClaudeBridge.Configuration;

namespace ClaudeBridge.Runs;

/// <summary>
/// Führt einen geprüften Lauf aus: Slot reservieren, Zeitlimit setzen, Claude starten, Ergebnis auswerten.
/// </summary>
public sealed class ClaudeRunService(
    BridgeOptions options,
    RunConcurrencyLimiter concurrencyLimiter,
    ClaudeProcessRunner processRunner,
    ILogger<ClaudeRunService> logger)
{
    public async Task<RunOutcome> ExecuteAsync(ValidatedRun run, CancellationToken requestAborted)
    {
        using var slot = await concurrencyLimiter.AcquireSlotAsync(requestAborted);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(options.TimeoutMinutes));
        using var timeoutOrAbort = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, requestAborted);

        var stopwatch = Stopwatch.StartNew();
        LogRunStart(run);

        var result = await processRunner.RunAsync(
            run.WorkdirPath,
            ClaudeCommandLine.BuildArguments(run),
            run.Prompt,
            timeoutOrAbort.Token);

        LogRunEnd(result, stopwatch.Elapsed, timeout.IsCancellationRequested, requestAborted.IsCancellationRequested);

        return ToOutcome(result, timedOut: timeout.IsCancellationRequested);
    }

    private RunOutcome ToOutcome(ClaudeProcessResult result, bool timedOut)
    {
        if (result.WasCancelled)
        {
            return timedOut
                ? new RunOutcome.TimedOut(options.TimeoutMinutes)
                : new RunOutcome.AbortedByClient();
        }

        var json = ClaudeOutputParser.ExtractJsonObject(result.Stdout);
        return json is not null
            ? new RunOutcome.Completed(json)
            : new RunOutcome.InvalidOutput(result.ExitCode, result.Stdout, result.Stderr);
    }

    private void LogRunStart(ValidatedRun run) =>
        logger.LogInformation(
            "Run start: workdir={Workdir} tools=[{Tools}] maxTurns={MaxTurns} promptLen={Len}",
            run.WorkdirAlias, string.Join(",", run.Tools), run.MaxTurns, run.Prompt.Length);

    private void LogRunEnd(ClaudeProcessResult result, TimeSpan duration, bool timedOut, bool aborted) =>
        logger.LogInformation(
            "Run end: exit={Exit} dauer={Ms}ms timeout={Timeout} abgebrochen={Aborted}",
            result.ExitCode, (long)duration.TotalMilliseconds, timedOut, aborted);
}
