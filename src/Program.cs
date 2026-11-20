// ClaudeBridge – kleine HTTP-Bridge, über die n8n (Docker) das lokale Claude-Code-CLI
// im Headless-Modus (claude -p) aufrufen kann.
//
// Endpunkte:
//   GET  /health   -> ohne Auth, nur Lebenszeichen
//   POST /run      -> Bearer-Token nötig, startet einen Claude-Code-Lauf und gibt dessen JSON zurück
using ClaudeBridge.Authentication;
using ClaudeBridge.Claude;
using ClaudeBridge.Configuration;
using ClaudeBridge.Endpoints;
using ClaudeBridge.Hosting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

var bridgeOptions = BridgeOptions.Load(builder.Configuration);
var claudeExecutable = ClaudeExecutable.Locate(bridgeOptions.ClaudePath);

builder.Services.AddClaudeBridge(bridgeOptions, claudeExecutable);

var app = builder.Build();

StartupDiagnostics.LogConfiguration(app.Logger, bridgeOptions, claudeExecutable);

app.UseMiddleware<BearerTokenAuthenticationMiddleware>();
app.MapHealthEndpoint();
app.MapRunEndpoint();

app.Run();
