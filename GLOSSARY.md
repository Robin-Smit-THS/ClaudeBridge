# ClaudeBridge

HTTP-Bridge, über die n8n das lokal installierte Claude-Code-CLI im Headless-Modus startet und dessen Ergebnis zurückbekommt.

## Language

**Anfrage**:
Ein einzelner HTTP-Aufruf eines Clients (z. B. n8n) an die Bridge, erkennbar an seiner Request-ID.
_Avoid_: Request (im Fließtext), Aufruf, Call

**Lauf**:
Ein einzelner Start des Claude-CLI (`claude -p`) für genau eine Anfrage an `/run`.
_Avoid_: Run (im Fließtext), Job, Ausführung

**Sitzung**:
Eine Claude-Session, die sich über mehrere Läufe erstreckt, verbunden über ihre Session-ID; jeder Folgelauf setzt sie per `resumeSessionId` fort.
_Avoid_: Session (im Fließtext), Auftrag, Konversation
