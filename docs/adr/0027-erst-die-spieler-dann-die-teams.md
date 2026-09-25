# ADR-0027 — Erst die Spieler, dann die Teams

**Status:** Accepted — ändert ADR-0020 an einer Stelle

## Kontext

Nach ADR-0020 ist im Doppel jeder Teilnehmer ein Team. Ein Name allein wurde
abgewiesen. Einzelne Spieler ließen sich nur auf einem Weg eintragen: alle auf
einmal, und dabei sofort zu Teams gewürfelt.

So läuft ein Doppelturnier unter Freunden aber nicht ab. Wer es anlegt, weiß,
wer mitspielt. Wer mit wem spielt, entscheidet sich oft erst am Platz: Jemand
sagt ab, zwei wollen unbedingt zusammen, der Rest wird gelost. Bis dahin gab es
keinen Ort, an dem die Spieler stehen konnten.

## Entscheidung

**Im Doppel dürfen Spieler allein auf der Liste stehen, bis ausgelost wird.**

- Ein Eintrag im Doppel ist ein Team (`Anna / Tom`) oder ein Spieler allein.
  Der steht „ohne Partner“ auf der Liste (`Tournament.Unpaired`). Drei Spieler
  in einem Eintrag bleiben ein Fehler.
- **Von Hand paaren:** `Anna / Tom` eintragen, wenn beide oder einer von beiden
  schon ohne Partner dastehen. Dann werden sie zum Team, an der Stelle des
  ersten in der Liste. Ein eigener Befehl zum Paaren ist dafür nicht nötig.
- **Zufällig paaren:** „Teams auslosen“ (`AddRandomTeams`) paart alle, die ohne
  Partner dastehen, samt den Spielern, die dabei noch genannt werden. Ohne
  Namen paart es nur die schon Eingetragenen. Zusammen muss die Zahl gerade
  sein.
- **Ausgelost wird erst, wenn jeder einen Partner hat.** Sonst nennt die
  Meldung, wer noch allein dasteht, und wie er einen Partner bekommt. Kommt nach
  der Auslosung jemand ohne Partner dazu, lost die Anwendung nicht neu aus,
  sondern geht zurück in die Vorbereitung.

Unverändert bleibt: Die Disziplin lässt sich nur mit leerer Liste wechseln.
Ein Doppel wird als Doppel angelegt. Nur die Paare dürfen später kommen.

## Folgen

- Oberfläche und Agent zeigen Spieler ohne Partner getrennt von den Teams. Die
  Teilnehmerliste bietet „Teams auslosen“ an, sobald zwei ohne Partner
  dastehen. „Auslosen“ erscheint erst, wenn niemand mehr allein ist.
- Ein Einzel kennt kein „ohne Partner“. Dort ist jeder Name ein Teilnehmer.
