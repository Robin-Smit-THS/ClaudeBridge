// ClaudeBridge – kleine HTTP-Bridge, über die n8n (Docker) das lokale Claude-Code-CLI
// im Headless-Modus (claude -p) aufrufen kann.
//
// Endpunkte:
//   GET  /health   -> ohne Auth, nur Lebenszeichen
//   POST /run      -> Bearer-Token nötig, startet einen Claude-Code-Lauf (optional mit "skill") und gibt dessen JSON zurück
//   GET  /skills   -> Bearer-Token nötig, listet die Skills eines Workdirs (?workdir=<alias>)
//
// Logging: Serilog, konfiguriert im Abschnitt "Serilog" der appsettings.json (Konsole + Log-Dateien).
using System.Diagnostics.CodeAnalysis;
using ClaudeBridge.Authentication;
using ClaudeBridge.Claude;
using ClaudeBridge.Configuration;
using ClaudeBridge.Endpoints;
using ClaudeBridge.Hosting;
using Serilog;

// Vorläufiger Logger, damit auch Fehler beim Start (Token, claude-Pfad, Konfiguration) in der Log-Datei landen.
// Ohne lesbare Logging-Konfiguration startet die Bridge nicht: Sie liefe sonst ganz ohne Log.
if (!TryCreateBootstrapLogger(args, out var bootstrapLogger))
    return 1;

Log.Logger = bootstrapLogger;

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

// Liest dieselben Serilog-Einstellungen wie der endgültige Logger (appsettings.json,
// appsettings.{Environment}.json, Umgebungsvariablen, Kommandozeile). Ist die Konfiguration nicht lesbar
// oder der Abschnitt "Serilog" ungültig, gibt es keine Log-Datei: Dann nur Konsole und Abbruch.
static bool TryCreateBootstrapLogger(string[] args, [NotNullWhen(true)] out Serilog.ILogger? logger)
{
    logger = null;
    IConfiguration configuration;

    try
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environments.Production;

        configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
    }
    catch (Exception ex)
    {
        LogFatalToConsole(ex, "appsettings.json konnte nicht gelesen werden, Bridge wird beendet");
        return false;
    }

    try
    {
        logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateBootstrapLogger();
        return true;
    }
    catch (Exception ex)
    {
        LogFatalToConsole(ex, "Abschnitt 'Serilog' der Konfiguration ist ungültig, Bridge wird beendet");
        return false;
    }
}

static void LogFatalToConsole(Exception exception, string message)
{
    using var console = new LoggerConfiguration().WriteTo.Console().CreateLogger();
    console.Fatal(exception, message);
}
