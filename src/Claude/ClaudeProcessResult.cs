namespace ClaudeBridge.Claude;

public sealed record ClaudeProcessResult(int ExitCode, string Stdout, string Stderr, bool WasCancelled)
{
    public static ClaudeProcessResult Cancelled { get; } = new(-1, "", "", WasCancelled: true);
}
