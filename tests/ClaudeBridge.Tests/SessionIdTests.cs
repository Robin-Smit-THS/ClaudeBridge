using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClaudeBridge.Tests;

/// <summary>Issue #2: Session-ID in den Zeilen "Run start" und "Run end".</summary>
[Collection(BridgeCollection.Name)]
public sealed class SessionIdTests(BridgeFixture bridge)
{
    [Fact]
    public async Task AC5_new_run_logs_a_dash_as_resumed_session()
    {
        var (_, lines) = await RunAsync(new { prompt = "neu", workdir = BridgeFixture.WorkdirAlias });

        Assert.Contains(" resumeSession=- ", RunStart(lines).Message);
    }

    [Fact]
    public async Task AC5_follow_up_run_logs_the_resumed_session_id()
    {
        var sessionId = NewSessionId();

        var (_, lines) = await RunAsync(new { prompt = "weiter", workdir = BridgeFixture.WorkdirAlias, resumeSessionId = sessionId });

        Assert.Contains($" resumeSession={sessionId} ", RunStart(lines).Message);
    }

    [Fact]
    public async Task AC6_run_end_logs_the_session_id_from_claudes_answer()
    {
        var sessionId = NewSessionId();

        var (response, lines) = await RunAsync(new { prompt = "x", workdir = BridgeFixture.WorkdirAlias, model = sessionId });

        Assert.Equal(sessionId, await ReadSessionIdAsync(response));
        Assert.EndsWith($" session={sessionId}", RunEnd(lines).Message);
    }

    [Theory]
    [InlineData("nojson", HttpStatusCode.BadGateway)]
    [InlineData("nosession", HttpStatusCode.OK)]
    public async Task AC6_run_end_logs_a_dash_when_claude_returns_no_session_id(string mode, HttpStatusCode expectedStatus)
    {
        var (response, lines) = await RunAsync(new { prompt = "x", workdir = BridgeFixture.WorkdirAlias, model = mode });

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.EndsWith(" session=-", RunEnd(lines).Message);
    }

    [Fact]
    public async Task AC6_run_end_logs_a_dash_when_the_run_times_out()
    {
        var (response, lines) = await RunAsync(new { prompt = "x", workdir = BridgeFixture.WorkdirAlias, model = "slow" });

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.EndsWith(" session=-", RunEnd(lines).Message);
    }

    [Fact]
    public async Task AC6_run_end_logs_a_dash_when_the_client_aborts()
    {
        using var client = bridge.CreateClient();
        using var abort = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var logPosition = bridge.ReadLogLines().Count;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PostAsJsonAsync("/run",
            new { prompt = "x", workdir = BridgeFixture.WorkdirAlias, model = "slow" }, abort.Token));

        // Ohne Antwort gibt es keinen Header: Die Anfrage über die 499-Zeile finden, die nach dem Start entstand.
        var requestLine = await bridge.WaitForLogLineAsync(
            l => l.Message.StartsWith("POST /run -> 499"), TimeSpan.FromSeconds(30), afterLineCount: logPosition);
        var runEnd = Assert.Single(bridge.ReadLogLines(), l => l.RequestId == requestLine.RequestId && l.Message.StartsWith("Run end:"));
        Assert.Contains("abgebrochen=true", runEnd.Message);
        Assert.EndsWith(" session=-", runEnd.Message);
    }

    [Fact]
    public async Task AC7_all_runs_of_a_session_are_found_by_its_session_id()
    {
        var sessionId = NewSessionId();

        var (start, _) = await RunAsync(new { prompt = "start", workdir = BridgeFixture.WorkdirAlias, model = sessionId });
        var (followUp, _) = await RunAsync(new { prompt = "antwort", workdir = BridgeFixture.WorkdirAlias, resumeSessionId = sessionId });

        var found = bridge.ReadLogLines().Where(l => l.Message.Contains(sessionId)).ToList();
        var startId = RequestIdTests.GetRequestId(start);
        var followUpId = RequestIdTests.GetRequestId(followUp);
        Assert.Equal([startId, followUpId], found.Select(l => l.RequestId).Distinct());
        Assert.Contains(found, l => l.RequestId == startId && l.Message.StartsWith("Run end:"));
        Assert.Contains(found, l => l.RequestId == followUpId && l.Message.StartsWith("Run start:"));
        Assert.Contains(found, l => l.RequestId == followUpId && l.Message.StartsWith("Run end:"));
    }

    private async Task<(HttpResponseMessage Response, IReadOnlyList<LogLine> Lines)> RunAsync(object body)
    {
        using var client = bridge.CreateClient();
        var response = await client.PostAsJsonAsync("/run", body);
        var requestId = RequestIdTests.GetRequestId(response);
        await bridge.WaitForLogLineAsync(l => l.RequestId == requestId && l.Message.StartsWith("POST /run"));
        return (response, bridge.ReadLogLines().Where(l => l.RequestId == requestId).ToList());
    }

    private static LogLine RunStart(IEnumerable<LogLine> lines) => Assert.Single(lines, l => l.Message.StartsWith("Run start:"));
    private static LogLine RunEnd(IEnumerable<LogLine> lines) => Assert.Single(lines, l => l.Message.StartsWith("Run end:"));

    private static string NewSessionId() => "sid-" + Guid.NewGuid().ToString("N");

    private static async Task<string?> ReadSessionIdAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("session_id").GetString();
    }
}
