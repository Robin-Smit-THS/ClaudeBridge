using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ClaudeBridge.Claude;

/// <summary>
/// Startet das Claude-CLI als Kindprozess, übergibt den Prompt per stdin und sammelt stdout/stderr ein.
/// </summary>
public sealed class ClaudeProcessRunner(ClaudeExecutable executable)
{
    public async Task<ClaudeProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string prompt,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateStartInfo(workingDirectory, arguments) };
        process.Start();

        // Ausgaben sofort lesen, damit der Prozess nicht an vollen Pipe-Puffern hängen bleibt
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await WritePromptAsync(process, prompt);

        if (!await WaitForExitOrKillAsync(process, cancellationToken))
            return ClaudeProcessResult.Cancelled;

        return new ClaudeProcessResult(process.ExitCode, await stdoutTask, await stderrTask, WasCancelled: false);
    }

    private ProcessStartInfo CreateStartInfo(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (executable.RequiresCommandShell)
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(executable.FilePath);
        }
        else
        {
            startInfo.FileName = executable.FilePath;
        }

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private static async Task WritePromptAsync(Process process, string prompt)
    {
        try
        {
            await process.StandardInput.WriteAsync(prompt);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Prozess hat stdin bereits geschlossen – die eigentliche Fehlermeldung kommt über stderr
        }
    }

    /// <returns><c>true</c>, wenn der Prozess regulär beendet wurde; <c>false</c>, wenn er abgebrochen wurde.</returns>
    private static async Task<bool> WaitForExitOrKillAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            return false;
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // Prozess ist bereits beendet
        }
    }
}
