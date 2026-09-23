# ADR-0022 — Live zählen, ein Eintragen-Link und ein Gespräch je Turnier

**Status:** Accepted

## Kontext

Beim Spielen am Platz sind vier Dinge aufgefallen:

1. **Es gab nur ganze Ergebnisse.** Eingetragen wurde, wenn das Match vorbei
   war — Satz für Satz auf einmal. Wer am Rand steht oder über den Mitschau-Link
   zuschaut, sah bis dahin nichts.
2. **Eintragen konnte nur die Turnierleitung.** Wer spielt, hat das Handy in der
   Tasche; wer den Verwalterlink weitergibt, gibt alles weiter — auch Löschen.
3. **Nach der Auslosung war der Rahmen fest.** Fiel danach auf, dass jemand
   fehlt oder der Modus nicht passt, half nur „Auslosung zurücknehmen" — obwohl
   noch kein Ball gespielt war.
4. **Es gab ein Gespräch je Browser.** Wer zwischen Turnieren wechselte, las im
   selben Verlauf, was zu einem ganz anderen Turnier gesagt worden war, und
   konnte es nicht loswerden.

## Entscheidung

### Live zählen: eine Folge von Ereignissen, abgespielt

Ein Match trägt die Folge dessen, was während des Spiels eingetragen wurde:
`Punkt für Seite 1/2` oder `Spiel für Seite 1/2`. Der Stand — Sätze, Spiele,
Punkte, Tiebreak — wird **nie gespeichert, sondern jedes Mal aus der Folge
abgespielt** (`LiveScoring.Replay`). Rückgängig heißt damit, das letzte Ereignis
wegzulassen; ein falsch stehen gebliebener Zwischenstand ist nicht möglich.

Die Zählregeln folgen dem Satzformat des Turniers: Tiebreak bei `n:n`,
Match-Tiebreak bis 10 statt des letzten Satzes, oder durchspielen ohne
Tiebreak. Ist das Match entschieden, trägt die Domäne das Ergebnis selbst ein
— über denselben Weg wie ein von Hand eingetragenes, also mit Nachrücken im
Bracket und Tabelle. Die Folge bleibt stehen, damit auch der entscheidende
Punkt noch zurückgenommen werden kann, solange das Folgematch kein Ergebnis hat.

Ein ganzes Ergebnis von Hand ersetzt das Mitzählen; auf ein solches Match wird
erst nach dem Löschen wieder live gezählt.

### Der Eintragen-Link

Neben Mitschau- und Verwalterlink gibt es einen dritten: `?s=…`. Wer ihn hat,
darf live zählen und Ergebnisse eintragen oder zurücknehmen, sonst nichts.

Das Token ist **aus dem Verwaltertoken abgeleitet** (SHA-256, nicht umkehrbar)
und nicht gespeichert. Damit rotiert es mit dem Verwalterlink, ohne dass es
einen eigenen Weg dafür bräuchte, und aus ihm lässt sich der Verwalterlink nicht
zurückrechnen. Gefunden wird ein Turnier über eine Spalte `scorer_token`, die
beim Schreiben mitgeführt und für bestehende Zeilen beim Start nachgetragen
wird.

Antworten auf Eintragungen enthalten das Verwaltertoken nur, wenn der Aufrufer
ohnehin verwalten darf; sonst kommt die Sicht ohne es.

### „Begonnen" statt „ausgelost"

> Ersetzt durch [ADR-0024](0024-countdown-und-ausdruecklicher-start.md): Fest wird der Rahmen jetzt mit dem ausdrücklichen Start, nicht mit dem ersten Punkt.

Fest wird der Rahmen, sobald ein Match (kein Freilos) einen Punkt oder ein
Ergebnis hat — nicht mit der Auslosung. Bis dahin lassen sich Teilnehmer,
Modus und Format ändern. Ändert sich Liste oder Modus eines ausgelosten
Turniers, **lost die Domäne neu aus**; das Format ändert keine Paarung und löst
deshalb kein neues Los aus. Reichen die Teilnehmer nicht mehr zum Auslosen,
bleibt es bei der Vorbereitung.

### Ein Gespräch je Turnier

Ein Gespräch gehört dem ersten Turnier, um das es darin geht, und dabei bleibt
es (`sessions.tournament_id`). Ein allgemeines Gespräch — noch ohne Turnier —
findet so zu dem Turnier, das darin angelegt wird. Kommt im Gespräch ein
anderes Turnier ins Spiel, wechselt die Oberfläche in dessen Gespräch. Wird das
Turnier gelöscht, gehen die Gespräche darüber mit; wird es aus einem Gespräch
heraus gelöscht, wird dieses wieder allgemein.

Ein Gespräch lässt sich löschen, das Turnier bleibt davon unberührt.

## Folgen

- Der Mitschau-Link zeigt den laufenden Stand ohne Neuladen: Jedes Ereignis ist
  eine Änderung am Turnier und läuft über denselben Live-Kanal.
- Wer den Verwalterlink rotiert, macht auch alle ausgegebenen Eintragen-Links
  ungültig. Das ist gewollt: Beide gehören zur selben Vertrauensfrage.
- Die Folge der Ereignisse wächst mit jedem Punkt eines Matches — ein paar
  hundert Einträge im JSON des Turniers, was für ein Dokument je Turnier
  (ADR-0016) unerheblich ist.
- Aufschlag und Seitenwechsel erfasst MATCHDAY weiterhin nicht.
