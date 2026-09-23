# ADR-0024 — Ein Countdown, und der Start ist ein eigener Schritt

**Status:** Accepted — ersetzt den Abschnitt „Begonnen statt ausgelost“ aus ADR-0022

## Kontext

Wer den Mitschau-Link vor dem Turnier öffnet, sieht einen Baum ohne Ergebnisse
und nichts, was sagt, wann es losgeht. Gewünscht ist ein Countdown bis zum
Start — auf der Turnierkarte, am Eintragen-Link und vor allem beim Mitschauen.

Dafür fehlten zwei Dinge:

1. **Eine Uhrzeit.** Das Turnier kannte nur ein Datum.
2. **Ein Start.** Nach ADR-0022 begann ein Turnier mit dem ersten gezählten
   Punkt. Ein Countdown, der bei null steht, bis irgendwer irgendwo einen Punkt
   einträgt, zählt auf nichts hin — und ein Turnier, das „begonnen“ hat, weil
   jemand versehentlich einen Punkt getippt hat, friert seinen Rahmen ein, ohne
   dass es jemand beschlossen hätte.

## Entscheidung

**Das Turnier trägt eine Startzeit, und die Turnierleitung startet es
ausdrücklich.**

- `StartTime` ist eine Uhrzeit ohne Datum und ohne Zeitzone, neben dem
  bestehenden `Date`. Der Countdown rechnet Datum und Uhrzeit in der Zeit des
  Geräts, das ihn zeigt.
- `Start()` geht nur nach der Auslosung und nur einmal; es setzt `StartedAt`.
  Das dürfen nur Verwalterlink und Eigentümer, nicht der Eintragen-Link.
- **Erst nach dem Start** lässt sich live zählen oder ein Ergebnis eintragen.
  Freilose stehen weiterhin mit der Auslosung fest; sie sind kein Spiel.
- **Mit dem Start** steht der Rahmen fest: Teilnehmer, Modus, Disziplin,
  Format. Das ersetzt die Regel „fest ab dem ersten Punkt“. Ein Punkt, der
  wieder zurückgenommen wird, öffnet den Rahmen nicht mehr. Wer ihn ändern
  will, nimmt die Auslosung zurück; damit ist auch der Start zurück.
- Der Countdown zählt Stunden, Minuten und Sekunden, darüber hinaus Tage.
  Ohne Uhrzeit zählt er nur Tage („Morgen geht es los“). Ist die Zeit um,
  bleibt „Gleich geht's los“ stehen, bis jemand startet. Mit dem Start
  verschwindet er auf allen Geräten, denn der Start läuft über denselben
  Live-Kanal wie jede andere Änderung.
- Der Agent bekommt das Werkzeug `start_tournament` und nimmt die Uhrzeit bei
  `create_tournament` und `update_tournament` als `HH:mm` entgegen.

### Verworfen

- **Ein Zeitpunkt mit Zeitzone** (`DateTimeOffset StartsAt`). Richtig für
  Zuschauer auf anderen Kontinenten, aber das Anlegen im Gespräch müsste dann
  wissen, in welcher Zone der Benutzer spricht, und das Formular bräuchte eine
  Zonenwahl. Unter Freunden an einem Platz ist die Ortszeit die Zeit aller.
  Wenn das einmal nicht mehr stimmt, ist es eine zusätzliche Spalte, kein
  Umbau.
- **Start durch den ersten Punkt beibehalten und nur den Countdown ergänzen.**
  Dann endet der Countdown in einem Zustand, der nichts bedeutet, und die
  Turnierleitung hat keinen Moment, in dem sie sagt: jetzt.
- **Ein eigener Zustand `Drawn` zwischen `Setup` und `Running`.** Sauberer im
  Modell, aber jede Stelle, die heute „ausgelost“ als „nicht Setup“ liest —
  Oberfläche, Agent, Tests —, müsste umlernen. `StartedAt` sagt dasselbe
  mit einem Feld.

## Folgen

- Bestehende Turniere, in denen schon gespielt wurde, gelten beim Lesen als
  gestartet, mit dem Anlegezeitpunkt als Näherung. Sonst ließe sich dort
  nach dem Update nichts mehr eintragen.
- Wer vergisst zu starten, kann am Platz nicht zählen. Der Eintragen-Link
  sagt das: „gezählt wird, sobald die Turnierleitung das Turnier startet“.
  Der Preis ist gewollt, denn genau dieser Moment ist der Sinn des Schritts.
- Die Uhrzeit ist Ortszeit. Ein Zuschauer in einer anderen Zeitzone sieht
  einen Countdown, der um die Differenz daneben liegt.
