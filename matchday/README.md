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
ANTHROPIC_API_KEY=sk-ant-… dotnet run --project src/Matchday.Server
```

Dann `http://localhost:5080` öffnen. Ohne `ANTHROPIC_API_KEY` läuft alles
außer dem Eingabefeld: die Widgets rufen die HTTP-API direkt.

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

## Betrieb

```bash
docker build -t matchday .
docker run --rm -p 8080:8080 -v matchday-daten:/data -e ANTHROPIC_API_KEY=sk-ant-… matchday
```

| Variable | Bedeutung |
| --- | --- |
| `ANTHROPIC_API_KEY` | Der Modellschlüssel. Fehlt er, bleibt das Eingabefeld stumm, alles andere läuft. |
| `Agent__Model` | Vorgabe `claude-opus-5`. |
| `Agent__Effort` | `low`, `medium` (Vorgabe), `high`. |
| `ConnectionStrings__Default` | Vorgabe im Bild `Data Source=/data/matchday.db`. Ohne Datenträger ist die Datenbank nach jedem Neustart leer. |

Auf Railway: Dienst aus diesem Repository mit **Root Directory** `matchday`,
einen Datenträger auf `/data`, `ANTHROPIC_API_KEY` setzen. Den Rest sagt
`railway.json`.
