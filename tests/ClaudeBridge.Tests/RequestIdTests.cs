using System.Net;
using System.Net.Http.Json;

namespace ClaudeBridge.Tests;

/// <summary>Issue #2: Request-ID in jeder Log-Zeile einer Anfrage und im Antwort-Header X-Request-Id.</summary>
[Collection(BridgeCollection.Name)]
public sealed class RequestIdTests(BridgeFixture bridge)
{
    private const string RunSource = "ClaudeBridge.Runs.ClaudeRunService";
    private const string RequestLogSource = "ClaudeBridge.Hosting.RequestLoggingMiddleware";
    private const string AuthSource = "ClaudeBridge.Authentication.BearerTokenAuthenticationMiddleware";

    [Fact]
    public async Task AC1_lines_of_a_run_request_carry_its_request_id()
    {
        using var client = bridge.CreateClient();

        var response = await client.PostAsJsonAsync("/run", new { prompt = "hallo", workdir = BridgeFixture.WorkdirAlias });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var requestId = GetRequestId(response);
        await bridge.WaitForLogLineAsync(l => l.RequestId == requestId && l.SourceContext == RequestLogSource);
        var lines = bridge.ReadLogLines().Where(l => l.RequestId == requestId).ToList();
        Assert.Contains(lines, l => l.SourceContext == RunSource && l.Message.StartsWith("Run start:"));
        Assert.Contains(lines, l => l.SourceContext == RunSource && l.Message.StartsWith("Run end:"));
        Assert.Contains(lines, l => l.SourceContext == RequestLogSource && l.Message.StartsWith("POST /run -> 200"));
    }

    [Fact]
    public async Task AC1_rejected_authentication_lines_carry_the_request_id()
    {
        using var client = bridge.CreateClient(authorized: false);

        var response = await client.GetAsync($"/skills?workdir={BridgeFixture.WorkdirAlias}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var requestId = GetRequestId(response);
        await bridge.WaitForLogLineAsync(l => l.RequestId == requestId && l.SourceContext == RequestLogSource);
        var lines = bridge.ReadLogLines().Where(l => l.RequestId == requestId).ToList();
        Assert.Contains(lines, l => l.SourceContext == AuthSource && l.Message.StartsWith("Authentifizierung abgelehnt"));
        Assert.Contains(lines, l => l.SourceContext == RequestLogSource && l.Message.StartsWith("GET /skills -> 401"));
    }

    [Fact]
    public void AC2_lines_outside_requests_have_an_empty_request_id()
    {
        var startupLines = bridge.ReadLogLines().Where(l => l.SourceContext == "Microsoft.Hosting.Lifetime").ToList();

        Assert.NotEmpty(startupLines);
        Assert.All(startupLines, l => Assert.Equal("", l.RequestId));
    }

    [Fact]
    public async Task AC3_lines_of_two_concurrent_requests_are_told_apart_by_request_id()
    {
        using var client = bridge.CreateClient();

        var runTask = client.PostAsJsonAsync("/run",
            new { prompt = "lange", workdir = BridgeFixture.WorkdirAlias, model = "wait" });
        await Task.Delay(TimeSpan.FromSeconds(1)); // der Fake läuft im Modus "wait" etwa 3 Sekunden
        var skillsResponse = await client.GetAsync($"/skills?workdir={BridgeFixture.WorkdirAlias}");
        var runResponse = await runTask;

        var runId = GetRequestId(runResponse);
        var skillsId = GetRequestId(skillsResponse);
        Assert.NotEqual(runId, skillsId);
        await bridge.WaitForLogLineAsync(l => l.RequestId == runId && l.SourceContext == RequestLogSource);
        var lines = bridge.ReadLogLines().ToList();

        var runLines = lines.Where(l => l.RequestId == runId).ToList();
        Assert.Collection(runLines,
            l => Assert.StartsWith("Run start:", l.Message),
            l => Assert.StartsWith("Run end:", l.Message),
            l => Assert.StartsWith("POST /run -> 200", l.Message));
        var skillsLine = Assert.Single(lines, l => l.RequestId == skillsId);
        Assert.StartsWith("GET /skills -> 200", skillsLine.Message);

        // Die Zeile von /skills steht zwischen Run start und Run end: Die Anfragen liefen wirklich gleichzeitig.
        var skillsIndex = lines.IndexOf(skillsLine);
        Assert.True(lines.IndexOf(runLines[0]) < skillsIndex && skillsIndex < lines.IndexOf(runLines[1]),
            "Die /skills-Zeile sollte zwischen Run start und Run end stehen:\n" + string.Join("\n", lines));
    }

    [Theory]
    [InlineData("GET", "/health", null, HttpStatusCode.OK)]
    [InlineData("GET", "/skills?workdir=" + BridgeFixture.WorkdirAlias, null, HttpStatusCode.OK)]
    [InlineData("GET", "/skills?workdir=unbekannt", null, HttpStatusCode.BadRequest)]
    [InlineData("POST", "/run", "{ kaputt", HttpStatusCode.BadRequest)]
    [InlineData("POST", "/run", """{"prompt":"x","workdir":"testdir","model":"nojson"}""", HttpStatusCode.BadGateway)]
    public async Task AC4_every_response_carries_the_request_id_of_its_log_line(
        string method, string path, string? body, HttpStatusCode expectedStatus)
    {
        using var client = bridge.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        await AssertRequestLineCarriesHeaderIdAsync(response, expectedStatus);
    }

    [Fact]
    public async Task AC4_unauthorized_response_carries_the_request_id()
    {
        using var client = bridge.CreateClient(authorized: false);

        var response = await client.PostAsJsonAsync("/run", new { prompt = "x", workdir = BridgeFixture.WorkdirAlias });

        await AssertRequestLineCarriesHeaderIdAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC4_timed_out_run_carries_the_request_id()
    {
        using var client = bridge.CreateClient();

        var response = await client.PostAsJsonAsync("/run",
            new { prompt = "x", workdir = BridgeFixture.WorkdirAlias, model = "slow" });

        await AssertRequestLineCarriesHeaderIdAsync(response, HttpStatusCode.GatewayTimeout);
    }

    private async Task<string> AssertRequestLineCarriesHeaderIdAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var requestId = GetRequestId(response);
        var line = await bridge.WaitForLogLineAsync(l => l.RequestId == requestId && l.SourceContext == RequestLogSource);
        Assert.Contains($" -> {(int)expectedStatus} in ", line.Message);
        return requestId;
    }

    internal static string GetRequestId(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("X-Request-Id", out var values), "Header X-Request-Id fehlt");
        var requestId = Assert.Single(values);
        Assert.False(string.IsNullOrWhiteSpace(requestId));
        return requestId;
    }
}
