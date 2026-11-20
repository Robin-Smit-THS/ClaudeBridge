using System.Diagnostics;
using System.Text;

namespace ClaudeBridge.Hosting;

/// <summary>
/// Protokolliert jede Anfrage mit Methode, Pfad, Statuscode, Dauer und Absender-IP.
/// Bei Fehlern (Status ab 400) wird zusätzlich der Antwort-Body als Begründung geloggt
/// (gekürzt). Request-Header (insbesondere Authorization) und Request-Bodies werden nie geloggt.
///
/// Fängt außerdem <see cref="BadHttpRequestException"/> ab (z. B. ungültiges JSON im Body)
/// und beantwortet sie mit 400 und einer lesbaren Fehlermeldung statt eines leeren Bodys.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    private const int MaxLoggedBodyBytes = 2000;

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var originalBody = context.Response.Body;
        var capture = new ResponseCaptureStream(originalBody, MaxLoggedBodyBytes);
        context.Response.Body = capture;

        try
        {
            await next(context);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new { error = DescribeBadRequest(ex) });
        }
        finally
        {
            context.Response.Body = originalBody;
            Log(context, stopwatch.ElapsedMilliseconds, capture);
        }
    }

    private void Log(HttpContext context, long elapsedMs, ResponseCaptureStream capture)
    {
        var request = context.Request;
        var statusCode = context.RequestAborted.IsCancellationRequested ? 499 : context.Response.StatusCode;
        var remoteIp = context.Connection.RemoteIpAddress;

        if (statusCode < 400)
        {
            logger.LogInformation(
                "{Method} {Path} -> {StatusCode} in {ElapsedMs} ms (from {RemoteIp})",
                request.Method, request.Path.Value, statusCode, elapsedMs, remoteIp);
            return;
        }

        var reason = capture.GetCapturedText();
        logger.LogWarning(
            "{Method} {Path} -> {StatusCode} in {ElapsedMs} ms (from {RemoteIp}) Grund: {Reason}",
            request.Method, request.Path.Value, statusCode, elapsedMs, remoteIp,
            string.IsNullOrWhiteSpace(reason) ? "(kein Antwort-Body)" : reason);
    }

    /// <summary>Hauptmeldung plus Detail der inneren Ausnahme (z. B. Position des JSON-Fehlers).</summary>
    private static string DescribeBadRequest(BadHttpRequestException ex) =>
        ex.InnerException is { } inner ? $"{ex.Message} {inner.Message}" : ex.Message;

    /// <summary>
    /// Reicht alle Schreibvorgänge an den eigentlichen Response-Stream durch und merkt sich
    /// dabei die ersten <c>limit</c> Bytes – große Antworten werden also nicht gepuffert.
    /// </summary>
    private sealed class ResponseCaptureStream(Stream inner, int limit) : Stream
    {
        private readonly MemoryStream _captured = new();
        private bool _truncated;

        public string GetCapturedText()
        {
            var text = Encoding.UTF8.GetString(_captured.GetBuffer(), 0, (int)_captured.Length);
            return _truncated ? text + " …" : text;
        }

        private void Capture(ReadOnlySpan<byte> buffer)
        {
            var remaining = limit - (int)_captured.Length;
            if (buffer.Length > remaining)
                _truncated = true;
            if (remaining > 0)
                _captured.Write(buffer[..Math.Min(remaining, buffer.Length)]);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Capture(buffer);
            inner.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Capture(buffer.AsSpan(offset, count));
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Capture(buffer.Span);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
