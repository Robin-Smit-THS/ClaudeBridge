using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClaudeBridge.Tests;

/// <summary>
/// Startet die echte Bridge (Program, appsettings.json, Serilog) im Testserver. Statt des Claude-CLI läuft
/// ein Fake (claude.cmd), die Log-Datei landet in einem eigenen Temp-Ordner. Die Einstellungen kommen
/// über Umgebungsvariablen, weil Program sie schon vor builder.Build() liest.
/// </summary>
public sealed partial class BridgeFixture : IDisposable
{
    public const string Token = "test-token-0123456789abcdefghijklmnopqrstuv";
    public const string WorkdirAlias = "testdir";
    public const int TimeoutMinutes = 1;

    private static readonly string[] EnvironmentVariables =
    [
        "Bridge__Token", "Bridge__ClaudePath", "Bridge__TimeoutMinutes", "Bridge__MaxConcurrentRuns",
        $"Bridge__Workdirs__{WorkdirAlias}", "Serilog__WriteTo__1__Args__path"
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ClaudeBridge.Tests", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public BridgeFixture()
    {
        var workdir = Directory.CreateDirectory(Path.Combine(_root, "workdir")).FullName;
        LogDirectory = Path.Combine(_root, "logs");
        var claudePath = Path.Combine(_root, "claude.cmd");
        // cmd.exe springt in Batch-Dateien mit reinen LF-Zeilenenden nicht zuverlässig zu Labels
        File.WriteAllText(claudePath, FakeClaude.Script.ReplaceLineEndings("\r\n"));

        Environment.SetEnvironmentVariable("Bridge__Token", Token);
        Environment.SetEnvironmentVariable("Bridge__ClaudePath", claudePath);
        Environment.SetEnvironmentVariable("Bridge__TimeoutMinutes", TimeoutMinutes.ToString());
        Environment.SetEnvironmentVariable("Bridge__MaxConcurrentRuns", "4");
        Environment.SetEnvironmentVariable($"Bridge__Workdirs__{WorkdirAlias}", workdir);
        Environment.SetEnvironmentVariable("Serilog__WriteTo__1__Args__path", Path.Combine(LogDirectory, "bridge-.log"));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            // Program setzt das Content-Root selbst auf AppContext.BaseDirectory; der Testserver darf es nicht ändern.
            builder.UseContentRoot(AppContext.BaseDirectory));
        _ = _factory.Server;
    }

    public string LogDirectory { get; }

    public HttpClient CreateClient(bool authorized = true)
    {
        var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(TimeoutMinutes + 2);
        if (authorized)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    /// <summary>Alle Zeilen der Log-Dateien, die dem outputTemplate entsprechen, in Dateireihenfolge.</summary>
    public IReadOnlyList<LogLine> ReadLogLines()
    {
        if (!Directory.Exists(LogDirectory))
            return [];

        var lines = new List<LogLine>();
        foreach (var file in Directory.GetFiles(LogDirectory, "*.log").Order())
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } text)
            {
                var match = LogLinePattern().Match(text);
                if (match.Success)
                    lines.Add(new LogLine(match.Groups["level"].Value, match.Groups["id"].Value,
                        match.Groups["source"].Value, match.Groups["message"].Value));
            }
        }

        return lines;
    }

    /// <summary>Wartet, bis eine passende Zeile im Log steht: Die Request-Zeile entsteht erst nach der Antwort.</summary>
    public async Task<LogLine> WaitForLogLineAsync(Func<LogLine, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            var line = ReadLogLines().FirstOrDefault(predicate);
            if (line is not null)
                return line;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Erwartete Log-Zeile nicht gefunden. Log:\n" +
                    string.Join("\n", ReadLogLines()));
            await Task.Delay(50);
        }
    }

    public void Dispose()
    {
        _factory.Dispose();
        foreach (var name in EnvironmentVariables)
            Environment.SetEnvironmentVariable(name, null);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Log-Datei evtl. noch offen – Temp-Ordner bleibt dann liegen
        }
    }

    // Aufbau laut outputTemplate: {Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{RequestId}] {SourceContext}: {Message:lj}
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[(?<level>[A-Z]{3})\] \[(?<id>[^\]]*)\] (?<source>\S+): (?<message>.*)$")]
    private static partial Regex LogLinePattern();
}

public sealed record LogLine(string Level, string RequestId, string SourceContext, string Message)
{
    public override string ToString() => $"[{Level}] [{RequestId}] {SourceContext}: {Message}";
}

[CollectionDefinition(Name)]
public sealed class BridgeCollection : ICollectionFixture<BridgeFixture>
{
    public const string Name = "Bridge";
}
