// ClaudeBridge – kleine HTTP-Bridge, über die n8n (Docker) das lokale Claude-Code-CLI
// im Headless-Modus (claude -p) aufrufen kann.
//
// Endpunkte:
//   GET  /health   -> ohne Auth, nur Lebenszeichen
//   POST /run      -> Bearer-Token nötig, startet einen Claude-Code-Lauf und gibt dessen JSON zurück

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // appsettings.json immer neben der .exe suchen (wichtig bei Start über Aufgabenplanung)
    ContentRootPath = AppContext.BaseDirectory
});

var opts = builder.Configuration.GetSection("Bridge").Get<BridgeOptions>() ?? new BridgeOptions();
opts.Normalize();

// ---- Validierung beim Start --------------------------------------------------------------
if (string.IsNullOrWhiteSpace(opts.Token) || opts.Token.Length < 32)
    throw new InvalidOperationException(
        "Bridge:Token fehlt oder ist zu kurz (min. 32 Zeichen). " +
        "Per Umgebungsvariable 'Bridge__Token' oder in appsettings.json setzen.");

var claudePath = ClaudeLocator.Resolve(opts.ClaudePath)
    ?? throw new InvalidOperationException(
        "claude wurde nicht gefunden. Bridge:ClaudePath in appsettings.json auf den vollen Pfad setzen " +
        "(z. B. C:\\Users\\<du>\\.local\\bin\\claude.exe).");

var app = builder.Build();
var log = app.Logger;

log.LogInformation("Claude CLI: {Path}", claudePath);
foreach (var (alias, path) in opts.Workdirs)
{
    if (Directory.Exists(path)) log.LogInformation("Workdir '{Alias}' -> {Path}", alias, path);
    else log.LogWarning("Workdir '{Alias}' -> {Path} existiert NICHT", alias, path);
}

var gate = new SemaphoreSlim(Math.Max(1, opts.MaxConcurrentRuns));

// ---- Auth-Middleware (alles außer /health) ----------------------------------------------
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    const string prefix = "Bearer ";
    var header = ctx.Request.Headers.Authorization.ToString();
    if (!header.StartsWith(prefix, StringComparison.Ordinal) ||
        !TokenEquals(header[prefix.Length..].Trim(), opts.Token))
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { error = "unauthorized" });
        return;
    }

    await next();
});

// ---- Endpunkte ---------------------------------------------------------------------------
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/run", async (RunRequest req, HttpContext ctx) =>
{
    // -- Eingaben prüfen
    if (string.IsNullOrWhiteSpace(req.Prompt))
        return Results.BadRequest(new { error = "prompt fehlt" });
    if (req.Prompt.Length > 100_000)
        return Results.BadRequest(new { error = "prompt zu lang (max. 100.000 Zeichen)" });

    if (string.IsNullOrWhiteSpace(req.Workdir) || !opts.Workdirs.TryGetValue(req.Workdir, out var workdir))
        return Results.BadRequest(new { error = $"unbekanntes workdir '{req.Workdir}'", erlaubt = opts.Workdirs.Keys });
    if (!Directory.Exists(workdir))
        return Results.BadRequest(new { error = $"workdir '{req.Workdir}' existiert nicht auf dem Host" });

    var tools = req.AllowedTools is { Length: > 0 } ? req.AllowedTools : opts.DefaultTools;
    var forbidden = tools.Where(t => !opts.AllowedTools.Contains(t, StringComparer.Ordinal)).ToArray();
    if (forbidden.Length > 0)
        return Results.BadRequest(new { error = "nicht freigegebene Tools", tools = forbidden, erlaubt = opts.AllowedTools });

    var maxTurns = Math.Clamp(req.MaxTurns ?? opts.DefaultMaxTurns, 1, opts.MaxMaxTurns);

    if (req.ResumeSessionId is not null && !Regex.IsMatch(req.ResumeSessionId, "^[A-Za-z0-9-]{8,64}$"))
        return Results.BadRequest(new { error = "ungültige resumeSessionId" });
    if (req.Model is not null && !Regex.IsMatch(req.Model, "^[A-Za-z0-9._:-]{1,64}$"))
        return Results.BadRequest(new { error = "ungültiges model" });

    // -- CLI-Argumente (Prompt geht über stdin, nicht über die Kommandozeile)
    var cliArgs = new List<string>
    {
        "-p",
        "--output-format", "json",
        "--permission-mode", "default",
        "--max-turns", maxTurns.ToString()
    };
    if (tools.Length > 0) { cliArgs.Add("--allowedTools"); cliArgs.Add(string.Join(",", tools)); }
    if (req.ResumeSessionId is not null) { cliArgs.Add("--resume"); cliArgs.Add(req.ResumeSessionId); }
    if (req.Model is not null) { cliArgs.Add("--model"); cliArgs.Add(req.Model); }
    if (!string.IsNullOrWhiteSpace(req.AppendSystemPrompt)) { cliArgs.Add("--append-system-prompt"); cliArgs.Add(req.AppendSystemPrompt); }

    // -- Nur N Läufe gleichzeitig
    await gate.WaitAsync(ctx.RequestAborted);
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(opts.TimeoutMinutes));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, ctx.RequestAborted);

        var sw = Stopwatch.StartNew();
        log.LogInformation("Run start: workdir={Workdir} tools=[{Tools}] maxTurns={MaxTurns} promptLen={Len}",
            req.Workdir, string.Join(",", tools), maxTurns, req.Prompt.Length);

        var result = await ClaudeRunner.RunAsync(claudePath, workdir, cliArgs, req.Prompt, linked.Token);

        log.LogInformation("Run end: exit={Exit} dauer={Ms}ms timeout={Timeout} abgebrochen={Aborted}",
            result.ExitCode, sw.ElapsedMilliseconds, timeout.IsCancellationRequested, ctx.RequestAborted.IsCancellationRequested);

        if (result.Cancelled)
        {
            return timeout.IsCancellationRequested
                ? Results.Json(new { error = $"Timeout nach {opts.TimeoutMinutes} min, Lauf abgebrochen" }, statusCode: 504)
                : Results.Json(new { error = "Anfrage vom Client abgebrochen" }, statusCode: 499);
        }

        // claude -p --output-format json liefert genau ein JSON-Objekt -> unverändert durchreichen
        var json = ExtractJson(result.Stdout);
        if (json is not null)
            return Results.Content(json, "application/json", Encoding.UTF8);

        return Results.Json(new
        {
            error = "Claude hat kein gültiges JSON geliefert",
            exitCode = result.ExitCode,
            stderr = Truncate(result.Stderr, 4000),
            stdout = Truncate(result.Stdout, 4000)
        }, statusCode: 502);
    }
    finally
    {
        gate.Release();
    }
});

app.Run();

// ---- Hilfsfunktionen ---------------------------------------------------------------------
static bool TokenEquals(string a, string b) =>
    CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + " …";

static string? ExtractJson(string stdout)
{
    var candidates = new[] { stdout.Trim() }
        .Concat(stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse());
    foreach (var c in candidates)
    {
        if (!c.StartsWith('{')) continue;
        try { using var doc = JsonDocument.Parse(c); return c; }
        catch (JsonException) { /* nächster Kandidat */ }
    }
    return null;
}

// ---- Typen -------------------------------------------------------------------------------
public sealed record RunRequest(
    string? Prompt,
    string? Workdir,
    string[]? AllowedTools,
    int? MaxTurns,
    string? ResumeSessionId,
    string? Model,
    string? AppendSystemPrompt);

public sealed class BridgeOptions
{
    public string Token { get; set; } = "";
    public string? ClaudePath { get; set; }
    public Dictionary<string, string> Workdirs { get; set; } = new();
    public string[] AllowedTools { get; set; } = [];
    public string[] DefaultTools { get; set; } = [];
    public int DefaultMaxTurns { get; set; } = 15;
    public int MaxMaxTurns { get; set; } = 50;
    public int TimeoutMinutes { get; set; } = 15;
    public int MaxConcurrentRuns { get; set; } = 1;

    public void Normalize()
    {
        Workdirs = new Dictionary<string, string>(Workdirs, StringComparer.OrdinalIgnoreCase);
        if (AllowedTools.Length == 0) AllowedTools = ["Read", "Grep", "Glob"];
        if (DefaultTools.Length == 0) DefaultTools = ["Read", "Grep", "Glob"];
    }
}

public sealed record RunResult(int ExitCode, string Stdout, string Stderr, bool Cancelled);

public static class ClaudeLocator
{
    public static string? Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? configured : null;

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));

        string[] names = OperatingSystem.IsWindows() ? ["claude.exe", "claude.cmd"] : ["claude"];
        foreach (var name in names)
            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }
}

public static class ClaudeRunner
{
    public static async Task<RunResult> RunAsync(
        string claudePath, string workdir, IReadOnlyList<string> args, string prompt, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workdir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // npm-Installation liefert claude.cmd -> muss über cmd.exe gestartet werden
        if (claudePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(claudePath);
        }
        else
        {
            psi.FileName = claudePath;
        }
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        try
        {
            await proc.StandardInput.WriteAsync(prompt);
            proc.StandardInput.Close();
        }
        catch (IOException) { /* Prozess hat stdin schon geschlossen – Fehler kommt über stderr */ }

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* bereits beendet */ }
            return new RunResult(-1, "", "", Cancelled: true);
        }

        return new RunResult(proc.ExitCode, await stdoutTask, await stderrTask, Cancelled: false);
    }
}
