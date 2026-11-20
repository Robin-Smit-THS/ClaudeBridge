namespace ClaudeBridge.Tests;

/// <summary>
/// Ersatz für das Claude-CLI. Das Verhalten steuert das Feld "model" der Anfrage (--model):
/// <list type="bullet">
/// <item><c>nojson</c>: keine JSON-Ausgabe (Bridge antwortet 502)</item>
/// <item><c>nosession</c>: gültiges JSON ohne session_id</item>
/// <item><c>badsession</c>: session_id mit Zeilenumbruch (<see cref="ForgedSessionId"/>)</item>
/// <item><c>slow</c>: läuft länger als das Zeitlimit (Timeout bzw. Abbruch)</item>
/// <item><c>wait</c>: läuft etwa 3 Sekunden</item>
/// <item><c>sid-…</c>: liefert diesen Wert als session_id</item>
/// </list>
/// Bei --resume liefert der Fake die fortgesetzte Session-ID zurück, wie Claude.
/// </summary>
public static class FakeClaude
{
    public const string DefaultSessionId = "fake-default-session";

    /// <summary>Session-ID mit Zeilenumbruch (JSON-Escape \n), die eine gefälschte zweite Log-Zeile erzeugen würde.</summary>
    public const string ForgedSessionId = @"x\n2026-01-01 00:00:00.000 +01:00 [INF] [] Forged: gefaelschte Zeile";

    public const string Script = $$"""
        @echo off
        set SID=
        set MODE=ok
        :loop
        if "%~1"=="" goto done
        if "%~1"=="--resume" set SID=%~2
        if "%~1"=="--model" set MODE=%~2
        shift
        goto loop
        :done
        more > nul
        if "%MODE%"=="nojson" (
          echo kein json
          exit /b 3
        )
        if "%MODE%"=="nosession" (
          echo {"type":"result","result":"ok"}
          exit /b 0
        )
        if "%MODE%"=="badsession" (
          echo {"type":"result","result":"ok","session_id":"{{ForgedSessionId}}"}
          exit /b 0
        )
        if "%MODE%"=="slow" ping -n 200 127.0.0.1 > nul
        if "%MODE%"=="wait" ping -n 4 127.0.0.1 > nul
        if "%MODE:~0,4%"=="sid-" set SID=%MODE%
        if "%SID%"=="" set SID={{DefaultSessionId}}
        echo {"type":"result","result":"ok","session_id":"%SID%"}
        exit /b 0
        """;
}
