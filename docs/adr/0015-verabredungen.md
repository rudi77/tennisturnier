# ADR-0015 — Eine Verabredung ist kein Turnier mit einem Match

**Status:** Accepted

## Kontext

[ADR-0013](0013-spielerprofil-und-verbindungen.md) hat den Kontaktgraphen
gebaut: wer mit wem gespielt hat, steht da. [ADR-0014](0014-turnierfeed.md) hat
dem Turnier eine Stimme gegeben. Beides bleibt an das Turnier gebunden — und
damit an zwei Wochenenden im Jahr.

Der überwiegende Teil des Tennisspielens findet daneben statt: „wer spielt
Samstag?". Genau dafür gibt es die WhatsApp-Gruppe, in der auch der Spielplan
als Foto steht, und genau dort liegt der Grund, warum eine Turnierverwaltung
kein soziales Netz wird — sie ist zwei Wochenenden im Jahr offen.

Das Problem: [ADR-0009](0009-turnier-als-wurzelaggregat.md) hat das Turnier zum
Wurzelaggregat gemacht. Ein Match gehört einer Phase, eine Phase einem Turnier.
Ein Match ohne Turnier gibt es nicht — und das ist keine Nachlässigkeit,
sondern der Kern der Entscheidung.

## Betrachtete Optionen

**A — Ein Match ohne Turnier zulassen.** Verworfen, und zwar deutlich. Es
zerbräche ADR-0009 an seiner tragenden Stelle: `Match.TournamentId` und
`PhaseId` sind Pflicht, der Query-Filter hängt vollständig daran, und die
öffentliche Projektion wird je Turnier gebaut. Ein Match ohne Turnier hätte
keine Sichtbarkeit, keine Projektion und keinen Ort, an dem sein Format stünde.

**B — Ein „Turnier" mit einem Match als Verabredung.** Verlockend, weil nichts
Neues zu bauen wäre. Verworfen: die Turnierliste des Benutzers füllte sich mit
Einträgen, die keine Turniere sind, jede Verabredung brauchte Format, Draw und
Zustandsautomat, und die Frage „wie viele Turniere hast du gespielt" wäre nicht
mehr zu beantworten. Ein Modell, das eine Sache als eine andere ausgibt, rächt
sich an jeder Auswertung.

**C — Ein eigenes Aggregat neben dem Turnier, ohne Ergebnis.** Gewählt.

## Entscheidung

### `PlayDate` steht neben `Tournament`, nicht darin

Ein eigenes Wurzelaggregat auf derselben Ebene. Es kennt kein Turnier, keine
Phase, kein Match — und ADR-0009 bleibt unangetastet: dort steht, was die Wurzel
*eines Turniers* ist, nicht, dass es nur eine Sorte Wurzel gäbe.

### Eine Verabredung hat kein Ergebnis

Das ist die Entscheidung, die alles Weitere einfach macht. Es gibt keinen
Spielstand, keinen Sieger, keine Sätze — und damit auch keine Frage, ob so ein
Match in die Bilanz eines Profils zählt oder in den Kontaktgraphen.

Der Grund ist nicht Bequemlichkeit. Ein Ergebnis ohne Schiedsrichter und ohne
Ausschreibung ist eine Behauptung: es gibt niemanden, der es bestätigt, und
niemanden, der eine Korrektur verantwortet. In einem Turnier trägt es ein, wer
`EnterResults` hat, und ADR-0002 hält fest, wer wann was ändern darf. Für eine
Samstagsrunde gibt es diese Ordnung nicht — und ein Ergebnis, das jeder über
sich selbst eintragen kann, wäre für eine Wertung wertlos. Weil hier ohnehin
keine Wertung gebaut wird, kostet der Verzicht nichts.

### Sichtbar ist sie für den Gastgeber und die Eingeladenen

Der Query-Filter aus [ADR-0004](0004-club-scoped-autorisierung.md) hängt an den
Turnieren des Aufrufers; eine Verabredung hat keines. Sie bekommt deshalb ihren
eigenen Filter, und er ist die knappste denkbare Regel: **wer sie ausgerichtet
hat oder eingeladen ist, sieht sie — sonst niemand.**

Ausdrücklich verworfen: „offen für alle meine Mitspieler". Der Kontaktgraph wird
gerechnet und nicht gespeichert (ADR-0013); als Bedingung in einem Query-Filter
müsste er bei jeder Abfrage entstehen. Wichtiger: eine offene Einladung an
achtzig Leute ist keine Verabredung, sondern ein Aushang — und der hätte
eigene Fragen zu beantworten, angefangen bei der, wer ihn wieder abnimmt.

Eingeladen wird aus dem Kontaktgraphen. Das ist die Verbindung zwischen den
beiden Entscheidungen und der Grund, warum diese hier zuletzt kommt: ohne
Kontakte gäbe es hier eine Suche über alle Benutzer, und die will niemand
haben, der sich einmal überlegt hat, was sie preisgibt.

### Wer kein Konto hat, wird nicht eingeladen

Ein Spieler ohne Konto kann nicht zusagen — er sieht die Einladung nicht und
bekommt sie auf keinem Weg. Die Einladung wird deshalb abgewiesen, statt still
ins Leere zu gehen. Die Kontaktliste sagt es vorher: sie führt bei jedem, ob er
sich einladen lässt.

### Der Zustand wird gerechnet, nicht gepflegt

Gespeichert wird genau eine Sache: ob abgesagt wurde. Ob genug zugesagt haben,
ergibt sich aus den Antworten und der Disziplin — Einzel braucht zwei, Doppel
vier —, und ob sie vorbei ist, aus der Uhr. Drei Zustände zu pflegen hieße, drei
Gelegenheiten zu haben, sie falsch zu setzen.

## Konsequenzen

- Eine Verabredung, die zustande kommt, hinterlässt keine Spur in Profil oder
  Kontaktgraph. Wer regelmäßig mit jemandem spielt, ohne je gemeinsam an einem
  Turnier teilzunehmen, steht nicht in dessen Kontakten. Das ist der Preis
  dafür, keine unbelegten Ergebnisse zu führen — und er wäre nur mit einer
  Bestätigung durch beide Seiten zu vermeiden, also mit einem zweiten
  Zustandsautomaten.
- Der Feed einer Verabredung fehlt. Kommentieren ließe sich über dasselbe
  Muster wie in ADR-0014 nachrüsten; bis dahin trägt sie eine Notiz des
  Gastgebers, und das ist für „Samstag 9 Uhr, Platz 2" genug.
- Absagen ist endgültig. Eine wiederbelebte Verabredung wäre eine, auf die
  jemand mit „hatte ich doch abgesagt" reagiert; eine neue zu erstellen kostet
  vier Felder.
