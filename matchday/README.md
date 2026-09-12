# MATCHDAY — ein Turnier mit Freunden

Ein Turnier anlegen, Freunde eintragen, auslosen, Ergebnisse eintragen, und
alle sehen auf dem Handy, wie es steht. Geführt wird das Ganze im Gespräch:
unten ein Eingabefeld, wahlweise per Sprache, und der Agent legt an, trägt
ein, lost aus und zeigt jeweils das Widget, das gerade zählt. Warum es so
gebaut ist, steht in [ADR-0016](../docs/adr/0016-neuanfang-ein-turnier-mit-freunden.md).

Keine Konten. Wer ein Turnier anlegt, bekommt einen **Verwalterlink** (`?a=…`,
geheim) und einen **Mitschau-Link** (`?t=…`, für alle).

## Starten

```bash
cd matchday
dotnet build && dotnet test
(cd web && npm ci && npm run build)          # baut nach src/Matchday.Server/wwwroot
AZURE_OPENAI_ENDPOINT=https://….openai.azure.com/ AZURE_OPENAI_API_KEY=… AZURE_OPENAI_DEPLOYMENT=gpt-5   dotnet run --project src/Matchday.Server
```

Dann `http://localhost:5080` öffnen. Ohne Modellzugang läuft alles außer dem
Eingabefeld: die Widgets rufen die HTTP-API direkt. Der Satz über dem
Eingabefeld sagt dann, was genau fehlt.

Ohne Azure-Ressource geht es auch direkt über OpenAI:
`Agent__Provider=OpenAI OPENAI_API_KEY=sk-… OPENAI_MODEL=gpt-5`.

Entwicklung mit Hot Reload der Oberfläche: `cd web && npm run dev` (Port 5000,
leitet `/api` auf 5080 weiter).

## Aufbau

| Wo | Was |
| --- | --- |
| `src/Matchday.Domain` | Turnier, Teilnehmer, Matches, Satzvalidierung, K.o.-Baum, Kreisverfahren, Tabelle. Keine Pakete. |
| `src/Matchday.Server` | Minimal API, SQLite als Dokumentspeicher, Live-Stream per SSE, der Agent mit seinen zwölf Werkzeugen, Auslieferung der Oberfläche. |
| `web` | Vite + React: das Gespräch mit Widgets, die Ergebnismaske, die Mitschau-Ansicht. |
| `tests` | Domänen- und Servertests, darunter die Werkzeuge des Agenten ohne Modell. |

Die Werkzeuge des Agenten und die HTTP-API rufen dieselben Anwendungsfälle
(`TournamentActions`). Der Agent entscheidet nie fachlich: Auslosung,
Satzprüfung und Tabelle kommen aus der Domäne.

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
| `ConnectionStrings__Default` | Vorgabe im Bild `Data Source=/data/matchday.db`. Ohne Datenträger ist die Datenbank nach jedem Neustart leer. |

Auf Railway: der Dienst dieses Repositories baut MATCHDAY. **Root Directory
bleibt die Wurzel** — das `railway.json` dort nennt `matchday/Dockerfile` und
`/api/health`. Zu tun bleibt: einen Datenträger auf `/data` hängen und die drei
`AZURE_OPENAI_*`-Variablen setzen.
