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
| M6 | Auto-Scheduling | Spielplanvorschlag mit Diff und Bestätigung | ✅ |
| M7 | Turniertag-Queue | Betrieb ohne starres Zeitraster | ✅ |
| M8 | SwissFormat | Schweizer System ohne Wiederholungspaarungen | ✅ |

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

- **Der Solver optimiert nicht, er legt.** `HeuristicScheduleSolver` arbeitet
  eine Prioritätenliste nach kritischer Pfadtiefe ab und nimmt je Match den
  frühesten zulässigen Platz. Für Vereinsturniere reicht das, und es hat die
  Eigenschaft, auf die es ankommt: zu jeder Ansetzung lässt sich in einem Satz
  sagen, warum sie dort liegt. Eine anschließende lokale Suche — Tauschen von
  Zuweisungen gegen Leerlauf — steht noch aus; sie fügt sich hinter demselben
  Port ein, ebenso wie CP-SAT, falls es je größere Felder gibt.
- **Die Uhr ist die Serveruhr.** Aufrufen, Starten und Beenden setzen den
  Zeitpunkt aus `IClock`. Eine nachträgliche Korrektur — „das Match hat schon um
  14:20 geendet, wir sind nur nicht dazu gekommen" — gibt es noch nicht; sie
  wäre ein zusätzlicher Parameter an denselben Endpunkten.
- **Nicht bestätigte Ansetzungen bleiben stehen.** Eine Bestätigung übernimmt,
  was in ihr steht, und räumt nur die Ansetzungen gespielter Matches ab. Eine
  Teilbestätigung ist ausdrücklich keine Aufforderung, den Rest zu löschen —
  wer eine Ansetzung loswerden will, hebt sie über
  `DELETE /api/assignments/{id}` auf.
- **Das Schweizer System ist nur als erste Phase vorgesehen.** Es paart nach
  Punktestand und braucht dafür in der ersten Runde ein Feld, das feststeht. Als
  Endrunde bekäme es Gruppenplätze, hinter denen noch niemand steht — es könnte
  seine erste Runde nicht ansetzen, und das Turnier bliebe stehen. Die Definition
  weist das ab. Umgekehrt — Schweizer Vorrunde, K.-o.-Endrunde — geht.
- **Die Paarung sucht, sie optimiert nicht — und sie schaut nicht voraus.**
  Gefunden wird die erste Paarung, die keine Wiederholung enthält und der idealen
  Dutch-Paarung am nächsten kommt; ein Maximum-Weight-Matching über gewichtete
  Abweichungen wäre in Randfällen eine Spur besser verteilt. Gewichtiger ist,
  dass jede Runde nach dem Stand von jetzt gepaart wird: nahe der Obergrenze
  (etwa acht Spieler über sieben Runden) kann sich das Verfahren in eine Runde
  manövrieren, für die keine wiederholungsfreie Paarung mehr existiert. Dann wird
  eine ausgewiesene Wiederholung angesetzt statt abgebrochen — ein Abbruch hieße,
  dass sich das letzte Ergebnis der vorigen Runde nicht mehr eintragen lässt. Eine
  Rückverfolgung über Rundengrenzen hinweg würde das auflösen; sie steht aus, und
  bis zur Voreinstellung `ceil(log2(n))` tritt der Fall in keinem geprüften
  Verlauf auf.
- **Eine Korrektur des Schiedsrichters verwirft angesetzte Paarungen.** Er darf
  Ergebnisse eintragen und zurücknehmen; im Schweizer System zieht das die
  Neupaarung der Folgerunden nach sich, samt ihrer noch wartenden
  Platzzuweisungen. Das ist die Folge der Korrektur und kein zusätzliches Recht —
  was am Platz steht, bleibt unangetastet. Ob eine Korrektur mit dieser Tragweite
  der Turnierleitung vorbehalten sein sollte, ist eine Regelfrage der
  Ausschreibung und bislang nicht entschieden.
- **Die Öffnungszeit ist am Turniertag eine Auskunft, keine Schranke.** Der
  Solver setzt nur in freie Fenster an; die Warteschlange wandert aber mit jedem
  überzogenen Match nach hinten und irgendwann darüber hinaus. Sie dort
  abzuschneiden hieße, wartende Matches stillschweigend fallen zu lassen —
  stattdessen trägt jedes wartende Match `withinOpeningHours`, und die
  Turnierleitung verteilt um oder vertagt. Ein Vorschlag, *wie* umzuverteilen
  wäre, steht noch aus.
- **Weiche Wünsche sind bislang Tiebreaks, keine Zielfunktion.** Center Court
  fürs Finale und der bisherige Platz bei gleicher Zeit entscheiden zwischen
  sonst gleichwertigen Möglichkeiten. Leerlauf zu minimieren und Runden zu
  bündeln setzt eine Bewertung voraus — und damit die lokale Suche oben.

- **Buchholz in einer Gruppenphase.** Das Kriterium stammt aus dem Schweizer
  System, wo es seit M8 die tragende Unterscheidung ist: nach fünf Runden stehen
  regelmäßig ein halbes Dutzend Spieler auf demselben Punktestand. In einer
  vollständig ausgespielten Gruppe dagegen ist es exakt die Gesamtpunktzahl minus
  die eigene und damit eine Umkehrung der Tabelle. Es bleibt konfigurierbar,
  steht aber in keiner mitgelieferten Round-Robin-Vorlage.
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

- **Es gibt keinen Weg, Rollen zu vergeben.** `RoleAssignment` ist modelliert,
  der Query-Filter aus ADR-0004 arbeitet darauf, und die Tests setzen Rollen über
  `IUserDirectory.AssignAsync` — einen Endpunkt dafür gibt es nicht. Auf einer
  frischen Datenbank hat damit niemand eine Rolle, und weil das Anlegen des
  ersten Vereins `ManageClubs` verlangt, kommt eine neue Instanz ohne Eingriff in
  die SQLite-Datei nicht in Gang. Es fehlt beides: die Rollenverwaltung als
  Anwendungsfall und ein abgesicherter Weg für den ersten Systemadministrator.
  Beides ist eine Festlegung — wer darf wem welche Rolle geben, und woran
  erkennt die erste Instanz ihren Eigentümer.
- **Grenze der Deklarativität.** Ein genuin neuer Paarungsalgorithmus, den keines der
  vier Formate abbildet, braucht weiterhin eine neue `IPhaseFormat`-Implementierung
  und ein Deployment (siehe ADR-0001).
- **Trostrunde.** `SingleEliminationConsolationFormat` ist nicht eingeplant und fügt
  sich als fünftes `IPhaseFormat` ein.
- ~~**Spielerstammdaten.**~~ Entschieden in [ADR-0008](adr/0008-spielerstammdaten.md):
  global, mit Vereinszugehörigkeit als Beziehung. Der Preis — der Query-Filter
  greift bei Spielern nicht — ist dort benannt.
