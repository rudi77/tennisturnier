# ADR-0017 — Das Agentenframework: MAF statt eigener Schleife, Azure OpenAI statt Anthropic

**Status:** Accepted

## Kontext

Mit [ADR-0016](0016-neuanfang-ein-turnier-mit-freunden.md) ist der Agent die
Bedienoberfläche von MATCHDAY. Gebaut war er auf dem Anthropic-SDK, und die
Werkzeugschleife — Modell fragen, Werkzeuge ausführen, Ergebnisse zurückgeben,
bis das Modell antwortet — stand als eigene Schleife im Code. Die Begründung
damals: jeder Schritt soll als Ereignis an die Oberfläche gehen, und das kann
ein Framework nicht.

Zwei Dinge sprechen jetzt dagegen.

**Der Anbieter.** Das Modell soll aus Azure OpenAI kommen. Der Rest des
Betriebs liegt dort, die Abrechnung ist geregelt, und ein Schlüssel, der bei
einem weiteren Anbieter liegt, ist ein Schlüssel mehr. Das Anthropic-SDK spricht
naturgemäß nur mit Anthropic.

**Die Schleife.** Sie ist nicht die interessante Stelle. Runden zählen,
Werkzeugergebnisse einsammeln, Nachrichten in die Form des Anbieters übersetzen
— das ist Arbeit, die jeder macht und niemand braucht. Die interessante Stelle
sind die zwölf Werkzeuge und der Systemprompt, und die bleiben, egal wer die
Schleife dreht.

Die Annahme von ADR-0016 war zudem falsch: das **Microsoft Agent Framework**
(MAF) streamt die Schleife mit. Textstücke, Werkzeugaufrufe und
Werkzeugergebnisse kommen als Inhalte im Antwortstrom heraus, während sie
passieren.

## Entscheidung

**Der Agent läuft auf MAF (`Microsoft.Agents.AI`), das Modell kommt aus Azure
OpenAI.** Die Werkzeugschleife dreht das Framework
(`FunctionInvokingChatClient`), MATCHDAY liest sie mit und macht daraus die
Ereignisse für die Oberfläche.

### Was bleibt

Die zwölf Werkzeuge in `AgentTools` — Namen, Beschreibungen, JSON-Schemata — und
der Systemprompt bleiben Zeile für Zeile. Auch der Grundsatz aus ADR-0016 bleibt:
der Agent entscheidet nie fachlich. Auslosung, Satzprüfung und Tabelle kommen aus
der Domäne, und Werkzeuge und HTTP-API rufen dieselben `TournamentActions`.

Die Ereignisse an die Oberfläche sind dieselben vier: `text`, `tool`, `widget`,
`done`. Die Oberfläche hat sich dafür nicht geändert.

### Der Schnitt zum Framework

Drei Stellen, und keine mehr:

| Wo | Was |
| --- | --- |
| `ModelAccess` | Der Zugang zum Modell. Baut den `IChatClient` aus der Konfiguration und hängt die Werkzeugschleife davor. Steht der Zugang nicht, sagt er in einem Satz, was fehlt. |
| `ToolRun` | Die Werkzeuge einer Anfrage als `AIFunction`, mit dem Schema aus `AgentTools.Definitions`. Sie schreiben mit, was gelaufen ist. |
| `TournamentAgent` | Liest den Strom mit und übersetzt zwischen Speicherform und Framework. |

Die Werkzeuge nehmen ihre Eingabe weiterhin als `JsonElement` und lesen sie
selbst. Das Framework könnte Schemata aus typisierten Methodensignaturen
ableiten — dann stünde jedes Feld zweimal da, einmal als Parameter und einmal
als Beschreibung für das Modell. Ein Schema, eine Stelle.

### Der Zugang ist Konfiguration, kein Code

Vorgabe ist Azure OpenAI. `Agent__Provider=OpenAI` schaltet auf OpenAI direkt um
— für den Rechner, auf dem keine Azure-Ressource liegt. Mehr Anbieter gibt es
nicht: wer einen dritten braucht, schreibt zehn Zeilen in `ModelAccess`.

Ohne Schlüssel läuft alles außer dem Eingabefeld, wie bisher. Neu ist nur, dass
der Satz darüber benennt, was genau fehlt — Endpunkt, Deployment oder Schlüssel.

### Der Aufwand des Modells

`Agent__Effort` (`low`, `medium`, `high`, `max`) wird zu
`ChatOptions.Reasoning.Effort`. Ein leerer Wert schickt gar nichts mit — für
Modelle, die kein Reasoning kennen und die Angabe zurückweisen.

## Folgen

**Der Verlauf wird strenger geprüft.** Azure OpenAI weist eine Unterhaltung ab,
in der ein Werkzeugaufruf ohne Ergebnis steht — oder ein Ergebnis ohne Aufruf.
Beides kann entstehen: wenn die Runden aufgebraucht sind, und wenn sich das
Modell ein Werkzeug ausdenkt, das es nicht gibt. Darum gilt: Aufruf und Ergebnis
kommen zusammen in die Sitzung oder gar nicht. Anthropic war hier duldsamer, und
dieselbe Sitzung hätte dort weiter funktioniert.

**Text wird gesammelt, nicht gestückelt.** Das Modell streamt Wort für Wort, die
Oberfläche macht aus jedem `text`-Ereignis eine Sprechblase. Also sammelt der
Server den Text und gibt ihn als Ganzes heraus, sobald ein Werkzeugaufruf kommt
oder die Antwort endet. Echtes Streaming in die Oberfläche wäre ein zweiter
Schritt und eine Änderung an der Oberfläche.

**Gedanken überleben den Anbieterwechsel.** `TextReasoningContent` wird wie zuvor
der Thinking-Block gespeichert, samt geschütztem Teil, und unverändert
zurückgegeben.

**Sitzungen von vorher bleiben lesbar.** Werkzeugergebnisse lagen dort unter der
Rolle `user` — die Anthropic-Form. Beim Lesen entscheidet der Inhalt über die
Rolle, nicht das gespeicherte Wort, damit solche Zeilen weiterhin durchgehen.

**Die Werkzeugschleife lässt sich ohne Netz prüfen.** Ein `IChatClient` aus dem
Testprojekt liest ein Drehbuch vor. Dadurch ist erstmals geprüft, was bei
aufgebrauchten Runden, erfundenen Werkzeugen und einem Modellausfall mitten im
Lauf passiert.
