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
| M9 | Turnier als Wurzel | Verein abgeschafft, Ort/Disziplin/Plätze am Turnier | ✅ |
| M10 | Öffentliche Selbstmeldung | Melden über einen Link, ohne Konto | ✅ (abgelöst von M12) |
| M11 | Jeder gegen jeden, kurze Sätze | Vorlage „Jeder gegen jeden", Satzformat am Turnier | ✅ |
| M12 | Turnier als Gruppe | Beitritt mit Konto, Einladungen, privat als Vorgabe | ✅ |
| M13 | Spielerprofil | Turnierübergreifende Historie, Bilanz, Kontaktgraph | ✅ |
| M14 | Turnierfeed | Chronik und Beiträge in der Gruppe, Kommentare, Push | ✅ |
| M15 | Verabredungen | Spielen zwischen den Turnieren, ohne Turnier | ✅ |

M0–M4 ergeben die erste vorführbare Version. M5–M8 bauen darauf auf, ohne die
bestehenden Verträge zu brechen.

M9 bricht sie: der Verein entfällt als Aggregat, und das Turnier tritt an seine
Stelle ([ADR-0009](adr/0009-turnier-als-wurzelaggregat.md)). Er war der einzige
Baustein, den die Wirklichkeit nicht hergab — reserviert wird außerhalb dieser
Anwendung, und was zugesagt ist, gilt für ein Turnier. M10 schließt die letzte
der vier Lücken, die den Umbau ausgelöst haben: bis dahin war „Meldung offen"
eine Behauptung, denn melden konnte nur die Turnierleitung
([ADR-0010](adr/0010-oeffentliche-selbstmeldung.md)).

M13 bis M15 sind der Schritt von der Turnierverwaltung zum Netzwerk. Sie sind
in dieser Reihenfolge gebaut, weil jeder auf dem vorigen steht: das Profil
beantwortet „wer ist das", der Feed „was ist los", der Kontaktgraph fällt aus
dem Profil ab, und die Verabredung braucht ihn, um zu wissen, wen sie fragen
darf.

Die tragende Entscheidung steht in [ADR-0013](adr/0013-spielerprofil-und-verbindungen.md):
**ein Profil zeigt, was der Fragende ohnehin sehen darf.** Gerechnet wird über
die Turniere im Query-Filter des Aufrufers, und damit ist die Zugriffsregel
keine zusätzliche Prüfung, sondern dieselbe wie überall. Der Preis ist eine
Zahl, die relativ zum Betrachter gilt — eine „wahre" Gesamtbilanz gäbe es nur
um den Preis, private Turniere über die Hintertür sichtbar zu machen.

M15 steht ausdrücklich neben ADR-0009 und nicht dagegen
([ADR-0015](adr/0015-verabredungen.md)): eine Verabredung ist kein Turnier mit
einem Match, sondern ein eigenes Aggregat ohne Phase, Draw und Ergebnis. Was sie
nicht hat, ist die Entscheidung — ein Spielstand ohne Schiedsrichter und ohne
Ausschreibung ist eine Behauptung, und weil hier keine Wertung gebaut wird,
kostet der Verzicht nichts.

M11 kommt aus dem Durchklicken: „Jeder gegen jeden" fehlte als eigene Vorlage —
die Liga daneben spielt Hin- und Rückrunde und ist damit doppelt so lang —, und
das Satzformat ließ sich nur über eine Vorlagenkopie einstellen. Beides betrifft
dasselbe: ein Vereinsturnier an einem Nachmittag muss in die zugesagten
Platzzeiten passen. Sätze bis vier und ein Champions-Tiebreak statt des dritten
sind die Stellschrauben dafür, und sie gehören dem Turnier
([ADR-0011](adr/0011-satzformat-am-turnier.md)).

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

- **Kein turnierübergreifendes Rating.** Es wäre der naheliegende nächste
  Schritt nach dem Profil und ist bewusst nicht gebaut. Es bräuchte eine Bilanz
  über *alle* Turniere, und die gibt es nach ADR-0013 nicht — jede Zahl im
  Profil gilt relativ zum Fragenden. Eine Wertungszahl, die für zwei Betrachter
  verschieden ausfällt, wäre keine. Sie käme entweder mit einer Projektion
  außerhalb des Query-Filters oder gar nicht; beides ist eine eigene
  Entscheidung.
- **Keine Benachrichtigungen.** Ein Feed, den man sieht, wenn man hinschaut,
  ist der erste Schritt; einer, der auf dem Telefon klingelt, hat eigene Kosten
  — Zustellwege, Abmeldung, Stille zur Nachtzeit. Bis dahin trägt der
  SignalR-Hinweis die Aktualisierung für den, der die Seite offen hat.
- **Eine Verabredung hat keinen Feed.** Kommentieren ließe sich über dasselbe
  Muster wie in ADR-0014 nachrüsten; bis dahin trägt sie eine Notiz des
  Gastgebers, und das ist für „Samstag 9 Uhr, Platz 2" genug.
- **Eine Verabredung hinterlässt keine Spur im Kontaktgraphen.** Wer regelmäßig
  mit jemandem spielt, ohne je gemeinsam an einem Turnier teilzunehmen, steht
  nicht in dessen Kontakten. Das ist der Preis dafür, keine unbelegten
  Ergebnisse zu führen — vermeidbar nur mit einer Bestätigung durch beide
  Seiten, also mit einem zweiten Zustandsautomaten.

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

- ~~**Es gibt keinen Weg, Rollen zu vergeben.**~~ Geschlossen in M9/M10. Die
  erste Rolle kommt aus dem Selbstservice: wer sich anmeldet, wird
  `Organizer`, und wer ein Turnier anlegt, wird dessen Turnierleiter — in
  derselben Arbeitseinheit. Alles Weitere geht über `RoleService` und
  `/api/tournaments/{t}/roles`. Zwei Sperren tragen ihn: eine globale Rolle
  lässt sich dort **nicht** vergeben (sonst machte sich ein Turnierleiter über
  ein zweites Konto zum Systemadministrator), und die letzte
  Turnierleiter-Zuweisung ist nicht entfernbar (sonst sähe niemand mehr das
  Turnier, und es gäbe keinen Weg zurück).

- **Der Partner im Doppel ist unverifiziert.** Wer beitritt, hat seit
  [ADR-0012](adr/0012-mitgliedschaft-statt-selbstmeldung.md) ein Konto — die
  Adresse kommt vom Aussteller und nicht aus einem Formular. Offen bleibt der
  Partner: er wird namentlich genannt und weiß davon zunächst nichts. Ein
  Paar-Zustandsautomat (Partner bestätigt seine Teilnahme) wäre die saubere
  Lösung; er kostet mehr, als er am Vereinsturnier einbringt, und ist deshalb
  nicht gebaut.
- **Für Kontaktdaten gibt es keine Aufbewahrungsfrist.** Was über die
  Selbstmeldung hereinkommt, bleibt unbegrenzt stehen. Das ist bei
  personenbezogenen Daten von Menschen ohne Konto der Punkt, an dem eine Regel
  fehlt — nicht ein Feature. `TournamentEntry.Origin` und `RegisteredAt` sind
  die Felder, an denen eine Löschregel ansetzen wird; sie stehen deshalb schon
  jetzt da. Offen ist die Frist selbst und die Frage, was mit einem Spieler
  geschieht, der in einem abgeschlossenen Turnier in der Tabelle steht.
- ~~**Ein noch nicht angemeldeter Benutzer lässt sich nicht berufen.**~~
  Geschlossen mit [ADR-0012](adr/0012-mitgliedschaft-statt-selbstmeldung.md):
  eine Berufung an eine unbekannte Adresse wird zur `Invitation` und beim
  ersten Login eingelöst. Was offen bleibt: **zugestellt wird sie nicht.** Es
  gibt keinen Mail-Adapter, die Turnierleitung teilt den Beitrittslink selbst.
- **Ein Mitglied sieht keine Meldungsliste.** Es sieht die Gruppe, den Draw,
  den Spielplan und die Ergebnisse — nicht aber, wer sich mit welcher Adresse
  gemeldet hat und wer auf der Warteliste steht. Das ist die Innenansicht aus
  [ADR-0003](adr/0003-getrenntes-read-modell.md) und bleibt bei der
  Turnierleitung. Wer im Feld steht, geht aus dem Draw hervor.
- **Aus einem Turnier austreten kann nur die Turnierleitung veranlassen.** Wer
  eine Gruppe verlassen will, muss fragen. Ein Selbst-Austritt ist eine Zeile
  im `RoleService` und eine Frage mehr in der Oberfläche (und die Meldung
  auch?) — er ist bewusst nicht Teil von ADR-0012.
- **Grenze der Deklarativität.** Ein genuin neuer Paarungsalgorithmus, den keines der
  vier Formate abbildet, braucht weiterhin eine neue `IPhaseFormat`-Implementierung
  und ein Deployment (siehe ADR-0001).
- **Trostrunde.** `SingleEliminationConsolationFormat` ist nicht eingeplant und fügt
  sich als fünftes `IPhaseFormat` ein.
- ~~**Spielerstammdaten.**~~ Entschieden in [ADR-0008](adr/0008-spielerstammdaten.md):
  global, mit Vereinszugehörigkeit als Beziehung. Der Preis — der Query-Filter
  greift bei Spielern nicht — ist dort benannt.
