# Fahrplan

Die Reihenfolge folgt der Abhängigkeit der Bausteine, nicht ihrer Sichtbarkeit. Die
öffentliche Ansicht kommt bewusst früh (M4), weil sie das Datenmodell diszipliniert.

| | Milestone | Ergebnis | Status |
|---|---|---|---|
| M0 | Fundament | Solution, Fitnessfunktionen, ADRs, CI | ✅ |
| M1 | Club, Court, Verfügbarkeit, Auth | Verein und Plätze verwaltbar, Club-Scope dicht | ✅ |
| M2 | Tournament, Entry, Format | Turnier durchläuft seinen Zustandsautomaten | ✅ |
| M3 | KnockoutFormat | K.O.-Turnier von der Anmeldung bis zum Finale spielbar | ✅ |
| M4 | Öffentliche Ansicht | Live-Bracket mit ETag und SignalR-Push | ✅ |
| M5 | RoundRobin + Phasen | Gruppenphase mit anschließendem K.O. | ✅ |
| M6 | Auto-Scheduling | Spielplanvorschlag mit Diff und Bestätigung | ⬜ |
| M7 | Turniertag-Queue | Betrieb ohne starres Zeitraster | ⬜ |
| M8 | SwissFormat | Schweizer System ohne Wiederholungspaarungen | ⬜ |

M0–M4 ergeben die erste vorführbare Version. M5–M8 bauen darauf auf, ohne die
bestehenden Verträge zu brechen.

## Bewusst nicht gebaut

- **JSON-Schema für die Formatdefinition.** Ursprünglich als
  `docs/schemas/format-definition.schema.json` geplant. Die Prüfung liegt
  stattdessen in `FormatDefinition.Validate()` — sie läuft beim Speichern einer
  Vorlage und erneut beim Einfrieren in ein Turnier, prüft mehr als ein Schema
  ausdrücken könnte (etwa dass eine Qualifikation auf eine frühere Phase zeigt)
  und ist die Stelle, die tatsächlich greift. Ein zweites Schema daneben wäre
  eine zweite Wahrheit, die auseinanderläuft. Für Editorunterstützung beim
  Schreiben eigener Formate wäre es dennoch nützlich — dann aber erzeugt aus dem
  Code, nicht von Hand gepflegt.

## Bewusst offene Punkte

- **Buchholz in einer Gruppenphase.** Das Kriterium stammt aus dem Schweizer
  System. In einer vollständig ausgespielten Gruppe ist es exakt die
  Gesamtpunktzahl minus die eigene und damit eine Umkehrung der Tabelle. Es
  bleibt konfigurierbar, steht aber in keiner mitgelieferten
  Round-Robin-Vorlage; ab M8 trägt es dort etwas bei, wo nicht jeder gegen
  jeden spielt.
- **Kampfloser Sieg und Satzverhältnis.** Ein Nichtantreten zählt als Sieg,
  bringt aber weder Sätze noch Spiele. Im Satzverhältnis steht ein kampfloser
  Sieg damit schlechter da als ein erspielter. Welche Zahl dort stehen soll, ist
  eine Regelfrage der Ausschreibung — sobald eine Vorlage sie beantwortet, wird
  daraus ein Parameter.
- **Überkreuzung bei ungerader Gruppenzahl.** Bei drei oder fünf Gruppen geht die
  Zuordnung „Gruppensieger gegen Zweiten einer anderen Gruppe" für genau eine
  Gruppe nicht auf; ihr Zweiter tauscht deshalb den Platz mit dem folgenden. Ab
  drei Qualifikationsrängen kann in einem ungeraden Feld dennoch eine
  Wiederholung entstehen. Sie ganz auszuschließen hieße, die Überkreuzung an
  anderer Stelle aufzugeben — das wäre eine Verschlechterung, keine Lösung.

- **Grenze der Deklarativität.** Ein genuin neuer Paarungsalgorithmus, den keines der
  vier Formate abbildet, braucht weiterhin eine neue `IPhaseFormat`-Implementierung
  und ein Deployment (siehe ADR-0001).
- **Trostrunde.** `SingleEliminationConsolationFormat` ist nicht eingeplant und fügt
  sich als fünftes `IPhaseFormat` ein.
- ~~**Spielerstammdaten.**~~ Entschieden in [ADR-0008](adr/0008-spielerstammdaten.md):
  global, mit Vereinszugehörigkeit als Beziehung. Der Preis — der Query-Filter
  greift bei Spielern nicht — ist dort benannt.
