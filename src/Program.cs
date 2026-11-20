// ClaudeBridge – kleine HTTP-Bridge, über die n8n (Docker) das lokale Claude-Code-CLI
// im Headless-Modus (claude -p) aufrufen kann.
//
// Endpunkte:
//   GET  /health   -> ohne Auth, nur Lebenszeichen
//   POST /run      -> Bearer-Token nötig, startet einen Claude-Code-Lauf (optional mit "skill") und gibt dessen JSON zurück
//   GET  /skills   -> Bearer-Token nötig, listet die Skills eines Workdirs (?workdir=<alias>)
//
// Logging: Serilog, konfiguriert im Abschnitt "Serilog" der appsettings.json (Konsole + Log-Dateien).
using ClaudeBridge.Authentication;
using ClaudeBridge.Claude;
using ClaudeBridge.Configuration;
using ClaudeBridge.Endpoints;
using ClaudeBridge.Hosting;
using Serilog;

// Vorläufiger Logger, damit auch Fehler beim Start (Token, claude-Pfad, Konfiguration) in der Log-Datei landen.
Log.Logger = CreateBootstrapLogger(args);

try
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services));

    var bridgeOptions = BridgeOptions.Load(builder.Configuration);
    var claudeExecutable = ClaudeExecutable.Locate(bridgeOptions.ClaudePath);

    builder.Services.AddClaudeBridge(bridgeOptions, claudeExecutable);

    var app = builder.Build();

    StartupDiagnostics.LogConfiguration(
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StartupDiagnostics)),
        bridgeOptions,
        claudeExecutable);

    app.UseMiddleware<RequestLoggingMiddleware>();
    app.UseMiddleware<BearerTokenAuthenticationMiddleware>();
    app.MapHealthEndpoint();
    app.MapRunEndpoint();
    app.MapSkillsEndpoint();

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.ForContext<Program>().Fatal(ex, "Bridge konnte nicht starten");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

// Liest dieselben Serilog-Einstellungen wie der endgültige Logger (appsettings.json, Umgebungsvariablen,
// Kommandozeile). Ist appsettings.json selbst nicht lesbar, bleibt nur die Konsole.
static Serilog.ILogger CreateBootstrapLogger(string[] args)
{
    try
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        return new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateBootstrapLogger();
    }
    catch (Exception ex)
    {
        var fallback = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();
        fallback.Fatal(ex, "appsettings.json konnte nicht gelesen werden");
        return fallback;
    }
}
