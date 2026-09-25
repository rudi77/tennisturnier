# ADR-0027 — Erst die Spieler, dann die Teams — und die Disziplin darf warten

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

**Die Disziplin lässt sich auch mit Namen auf der Liste wechseln**, bis zum
Start. Aus einem Einzel wird ein Doppel, dessen Spieler ohne Partner dastehen.
Aus einem Doppel wird ein Einzel, in dem die Teams in ihre Spieler zerfallen;
wer schon allein dastand, behält seinen Eintrag. Ergäben die Spieler mehr als
64 Teilnehmer, wird der Wechsel abgelehnt. Das ersetzt die Regel aus ADR-0020,
nach der nur mit leerer Liste gewechselt werden durfte: Sie zwang dazu, alle
Namen zu löschen, nur weil die Entscheidung fürs Doppel später fiel als das
Eintragen.

**Die Disziplin darf offen bleiben** (`Discipline.Open`). Ein Turnier lässt
sich anlegen, ohne Einzel oder Doppel zu wählen; im Formular und im Gespräch
ist „offen“ die Vorgabe. Eingetragen wird trotzdem schon — einzeln oder als
Paar. Ausgelost wird erst, wenn entschieden ist: Die Teilnehmerliste fragt
dann „Einzel oder Doppel?“, und der Agent fragt nach, statt zu raten. Wird ein
ausgelostes Turnier wieder auf offen gestellt, geht es zurück in die
Vorbereitung.

`Open` steht in der Aufzählung hinten. Turniere, die vor dieser Entscheidung
gespeichert wurden, tragen keinen oder einen der beiden alten Werte und lesen
sich unverändert; Schnittstelle und Domäne behalten Einzel als Vorgabe, nur
Formular und Agent schlagen „offen“ vor.

## Folgen

- Oberfläche und Agent zeigen Spieler ohne Partner getrennt von den Teams. Die
  Teilnehmerliste bietet „Teams auslosen“ an, sobald zwei ohne Partner
  dastehen. „Auslosen“ erscheint erst, wenn niemand mehr allein ist.
- Ein Einzel kennt kein „ohne Partner“. Dort ist jeder Name ein Teilnehmer.
