# MATCHDAY — ein Turnier mit Freunden

Ein Turnier anlegen, Freunde eintragen, auslosen, starten, Ergebnisse
eintragen, und alle sehen auf dem Handy, wie es steht — vor dem Start mit
einem Countdown auf Datum und Uhrzeit
([ADR-0024](../docs/adr/0024-countdown-und-ausdruecklicher-start.md)). **Einzel oder Doppel** — im Doppel ist
ein Teilnehmer ein Team, geschrieben `Anna / Tom`
([ADR-0020](../docs/adr/0020-einzel-und-doppel.md)). Geführt wird das Ganze im
Gespräch: unten ein Eingabefeld, wahlweise per Sprache, und der Agent legt an,
trägt ein, lost aus und zeigt jeweils das Widget, das gerade zählt. Warum es so
gebaut ist, steht in [ADR-0016](../docs/adr/0016-neuanfang-ein-turnier-mit-freunden.md).

Auch **Einzel oder Doppel** muss beim Anlegen nicht feststehen: Das Turnier
steht dann auf „offen“, die Namen lassen sich trotzdem eintragen, und vor dem
Auslosen fragt die Teilnehmerliste, was gespielt wird.

Im Doppel müssen die Paare nicht feststehen: Trag die Spieler einzeln ein, sie
stehen dann **ohne Partner** auf der Liste ([ADR-0027](../docs/adr/0027-erst-die-spieler-dann-die-teams.md)).
Die Teams kommen später: **Das Los bildet sie** aus allen ohne Partner, im
Gespräch („mach daraus zufällige Teams“) oder über den Knopf „Teams auslosen“.
Von Hand geht es mit „Anna / Tom“. Ausgelost wird erst, wenn jeder einen Partner
hat. Gemischt wird in der Domäne, wie bei der Auslosung selbst.

**Drei Wege, und jeder führt durch:** ganz im Gespräch, ganz über die Widgets
auf der Bühne, oder gemischt. Beide rufen dieselben Anwendungsfälle, und der
Live-Strom bringt jede Änderung sofort auf den anderen Weg. Der Agent **handelt
und erklärt**: Fragen zur App, zum Ablauf, zu den Modi und zu den Spielregeln
beantwortet er aus seinem Wissen, ohne ein Werkzeug zu rufen
([ADR-0021](../docs/adr/0021-der-agent-erklaert-und-die-widgets-koennen-alles.md)).

Keine Konten. Wer ein Turnier anlegt, bekommt einen **Verwalterlink** (`?a=…`,
geheim) und einen **Mitschau-Link** (`?t=…`, für alle).

## Starten

```bash
cd matchday
dotnet build && dotnet test
(cd web && npm ci && npm run build)          # baut nach src/Matchday.Server/wwwroot
AZURE_OPENAI_ENDPOINT=https://….openai.azure.com/ AZURE_OPENAI_API_KEY=… AZURE_OPENAI_DEPLOYMENT=gpt-5   dotnet run --project src/Matchday.Server
```

Dann `http://localhost:5080` öffnen. Ohne Modellzugang bleibt nur das
Eingabefeld stumm: Jeder Schritt — anlegen, Rahmen ändern, Teilnehmer eintragen,
auslosen und zurücknehmen, Ergebnisse, teilen, löschen — geht auch über die
Widgets, und die rufen die HTTP-API direkt. Der Satz über dem Eingabefeld sagt,
was genau fehlt.

Ohne Azure-Ressource geht es auch direkt über OpenAI:
`Agent__Provider=OpenAI OPENAI_API_KEY=sk-… OPENAI_MODEL=gpt-5`.

Entwicklung mit Hot Reload der Oberfläche: `cd web && npm run dev` (Port 5000,
leitet `/api` auf 5080 weiter).

## Aufbau

| Wo | Was |
| --- | --- |
| `src/Matchday.Domain` | Turnier, Teilnehmer (Einzel und Doppel), Matches, Satzvalidierung, K.o.-Baum, Kreisverfahren, Tabelle. Keine Pakete. |
| `src/Matchday.Server` | Minimal API, SQLite als Dokumentspeicher, Live-Stream per SSE, der Agent mit seinen vierzehn Werkzeugen, Auslieferung der Oberfläche. |
| `web` | Vite + React: das Gespräch mit Widgets, die Ergebnismaske, die Mitschau-Ansicht. Auf dem Telefon lässt sich das Gespräch zuklappen — dann gehört der Schirm den Widgets. |
| `tests` | Domänen- und Servertests, darunter die Werkzeuge des Agenten ohne Modell. |

Die Werkzeuge des Agenten und die HTTP-API rufen dieselben Anwendungsfälle
(`TournamentActions`). Der Agent entscheidet nie fachlich: Auslosung,
Satzprüfung und Tabelle kommen aus der Domäne. Was er über die Anwendung und
über Tennis weiß, steht in `Agent/Knowledge.cs` und hängt unter den
Anweisungen — ein Test hält fest, dass zu jedem Modus, jeder Disziplin und jeder
Variante des letzten Satzes dort etwas steht.

Die Werkzeugschleife dreht das **Microsoft Agent Framework**; MATCHDAY liest sie
mit und macht daraus die Ereignisse für die Oberfläche. Warum, steht in
[ADR-0017](../docs/adr/0017-agentenframework-maf-und-azure-openai.md). Der Schnitt
zum Framework liegt an drei Stellen: `ModelAccess` (der Zugang zum Modell),
`ToolRun` (die Werkzeuge einer Anfrage) und `TournamentAgent` (der Strom).

## Abdeckung

```powershell
./scripts/coverage.ps1
```

## Betrieb

Gebaut wird aus der Wurzel des Repositories — der Bauplan liegt hier, der
Kontext eine Ebene darüber:

```bash
docker build -f matchday/Dockerfile -t matchday ..
docker run --rm -p 8080:8080 -v matchday-daten:/data   -e AZURE_OPENAI_ENDPOINT=https://….openai.azure.com/   -e AZURE_OPENAI_API_KEY=…   -e AZURE_OPENAI_DEPLOYMENT=gpt-5   matchday
```

| Variable | Bedeutung |
| --- | --- |
| `AZURE_OPENAI_ENDPOINT` | Die Adresse der Azure-OpenAI-Ressource. |
| `AZURE_OPENAI_DEPLOYMENT` | Der Name des Deployments — **ohne ihn läuft der Agent nicht**. Eine Vorgabe gibt es bewusst nicht: Deployment-Namen sind frei gewählt, und ein geratener endet in einem 404 beim ersten Satz. Gleichwertig: `Agent__Model`, das in der Kette davor steht. |
| `AZURE_OPENAI_API_KEY` | Der Schlüssel. Fehlt er, bleibt das Eingabefeld stumm, alles andere läuft. |
| `Agent__UseAzureCredential` | `true` nimmt statt des Schlüssels die Anmeldung der Umgebung — Managed Identity im Betrieb, `az login` auf dem Rechner. |
| `Agent__Provider` | `AzureOpenAI` (Vorgabe) oder `OpenAI`. Bei `OpenAI` zählen `OPENAI_API_KEY` und `OPENAI_MODEL`. |
| `Agent__Effort` | `low`, `medium` (Vorgabe), `high`, `max`. Leer schickt gar nichts mit — für Modelle ohne Reasoning. |
| `Agent__MaxTokens` | Vorgabe 4096. |
| `Agent__MaxToolRounds` | Wie oft der Agent in einer Antwort Werkzeuge rufen darf. Vorgabe 12. |
| `Auth__Required` | Vorgabe `false`: kein Konto, Verwalter- und Mitschau-Links wie in ADR-0016. Auf `true` verlangt jeder besitzergebundene Aufruf eine Google-Anmeldung — **auf einer öffentlich erreichbaren Instanz gehört er dorthin** (ADR-0019). |
| `Auth__GoogleClientId` | Die Client-Id aus der Google Cloud Console, zugleich die Audience der Token. Bei `Auth__Required=true` Pflicht: Fehlt sie, bricht der Start ab, statt jeden still abzuweisen. |
| `Auth__AllowedEmails` | Wer herein darf: E-Mail-Adressen, getrennt durch Komma. Leer heißt jedes Google-Konto. Ein anderes Konto bekommt nach der Anmeldung „nicht freigegeben“ zu sehen ([ADR-0023](../docs/adr/0023-freigabeliste.md)). |
| `Auth__KeysPath` | Wo die Schlüssel des Sitzungs-Cookies liegen. Im Bild `/data/keys`, neben der Datenbank — sonst ist nach jedem Deploy jede Anmeldung ungültig ([ADR-0025](../docs/adr/0025-sitzung-statt-google-token.md)). |
| `Backup__Path`, `Backup__Keep` | Einmal am Tag eine Kopie der Datenbank dorthin, die letzten `Keep` (Vorgabe 7) bleiben. Im Bild `/data/backups`; leer heißt keine Sicherung. Die Kopien liegen auf demselben Datenträger — gegen ein gelöschtes Turnier helfen sie, gegen einen verlorenen Datenträger nicht. |
| `ConnectionStrings__Default` | Vorgabe im Bild `Data Source=/data/matchday.db`. Ohne Datenträger ist die Datenbank nach jedem Neustart leer. |

### Die Anmeldung einrichten

1. In der [Google Cloud Console](https://console.cloud.google.com/apis/credentials)
   eine **OAuth-Client-ID** vom Typ *Webanwendung* anlegen.
2. Als **autorisierten JavaScript-Ursprung** die Adresse der Instanz eintragen,
   ohne Pfad und ohne Schrägstrich am Ende — für die Entwicklung zusätzlich
   `http://localhost:5080`. Ein Ursprung, der dort nicht steht, bekommt von
   Google keinen Knopf, sondern eine Fehlermeldung in der Konsole.
   Weiterleitungs-URIs braucht es nicht: Die Oberfläche holt das Id-Token über
   Google Identity Services, nicht über einen Umweg auf den Server.
3. `Auth__GoogleClientId` auf die Client-Id setzen und `Auth__Required=true`.
4. `Auth__AllowedEmails` auf die Adressen setzen, die herein dürfen. Ohne sie
   kommt jedes Google-Konto herein — und jedes Gespräch geht auf die eigene
   Modellrechnung.

Zuschauer bleiben davon unberührt — der Mitschau-Link verlangt keine Anmeldung.

Auf Railway: der Dienst dieses Repositories baut MATCHDAY. **Root Directory
bleibt die Wurzel** — das `railway.json` dort nennt `matchday/Dockerfile` und
`/api/health`. Zu tun bleibt: einen Datenträger auf `/data` hängen und die drei
`AZURE_OPENAI_*`-Variablen setzen.

Zwei Umgebungen, zwei Branches ([ADR-0029](../docs/adr/0029-erst-staging-dann-production.md)):
**Staging** baut `main`, **Production** baut `production`. Live geht ein Stand
erst, wenn er auf Staging lief:

```bash
git push origin main:production
```
