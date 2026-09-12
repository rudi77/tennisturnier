# ADR-0020 — Einzel und Doppel: ein Teilnehmer ist eine Aufstellung

**Status:** Accepted

## Kontext

ADR-0016 sagt: „**Teilnehmer** als Namen. Kein Spielerstamm, keine Kontaktdaten,
keine Setzliste, keine Warteliste." Im selben Text steht der Beispielsatz, mit
dem ein Turnier entstehen soll: „Leg ein **Doppelturnier** am Samstag an, Rudi,
Max, Anna und Tom spielen mit."

Beides zusammen geht bisher nicht. Ein Doppel ließ sich nur dadurch andeuten,
dass jemand „Anna und Tom" als *einen* Namen einträgt. Das trägt, solange
niemand etwas davon wissen muss — und es trägt nicht mehr, sobald es das soll:

- Ein Ergebnis eintragen heißt, das Match über zwei Namen zu finden. „Anna hat
  gewonnen" findet dann nichts, weil der Teilnehmer „Anna und Tom" heißt.
- Niemand prüft, ob die Einträge zueinander passen. Ein Turnier aus „Anna und
  Tom", „Rudi" und „Max" ist halb Doppel, halb Einzel, und erst beim Spielen
  fällt es auf.
- Der Agent kann nicht wissen, was gemeint ist. Sagt jemand „Doppel", hat er
  kein Feld, in das er das schreiben könnte.

Der alte Baum hatte dafür eine `Discipline` (Einzel, Doppel, Mixed) und einen
Teambildungsdienst mit Partnerstammdaten, E-Mail-Einladungen und einer
Paarungsregel. Das ist der Umfang, den ADR-0016 bewusst abgelegt hat.

## Entscheidung

**Das Turnier trägt eine Disziplin, und ein Teilnehmer trägt seine Aufstellung.**

`Discipline` kennt zwei Werte: `Singles` und `Doubles`. Sie steht am Turnier,
gilt für das ganze Turnier und lässt sich — wie Modus und Satzformat — nur in der
Vorbereitung ändern; anders als diese zusätzlich nur, solange die
Teilnehmerliste leer ist. Ein Einzelname ist kein Team, und ein Team ist kein
Einzelname: Was auf der Liste steht, ließe sich beim Wechsel nicht umdeuten,
also wird der Wechsel abgelehnt, statt zu raten.

Ein `Participant` bleibt, was er war — eine Id und ein Name —, und bekommt ein
freiwilliges Feld `Players`. Im Einzel steht dort nichts und der Name ist der
einzige Spieler. Im Doppel stehen dort die beiden Spieler, und der Name ist ihre
Schreibweise: `Anna / Tom`. Damit gilt weiter, was ADR-0016 festgelegt hat: Ein
Teilnehmer ist ein Name. Es gibt keinen Spielerstamm, keine Partnerverwaltung,
kein Mixed, keine Einladung — zwei Namen in einem Feld.

Dass das Feld freiwillig ist, ist keine Bequemlichkeit, sondern der Weg an einer
Wanderung vorbei: Ein Turnier ist eine JSON-Zeile (ADR-0016), und die Zeilen, die
heute in der Datenbank liegen, kennen weder `discipline` noch `players`. Fehlen
beide, ist es ein Einzel, und der Name ist der Spieler — genau das, was es war.

### Eine Aufstellung schreibt man, wie man spricht

`Anna / Tom`, `Anna und Tom`, `Anna & Tom`, `Anna + Tom` — alles dasselbe Team,
und es steht danach überall als `Anna / Tom`. Das Komma bleibt frei: Damit
trennt die Oberfläche mehrere Teilnehmer, und `Anna / Tom, Rudi / Max` sind
deshalb zwei Teams und nicht vier Spieler.

Gefunden wird ein Team auf drei Weisen: als Paar (`Anna / Tom`), als Paar in
anderer Reihenfolge oder Schreibweise (`Tom/Anna`) und über einen einzelnen
Spieler (`Anna`). Das ist der Grund, warum die Spieler überhaupt einzeln
gespeichert werden und nicht nur der zusammengesetzte Name: „Anna hat gegen Rudi
6:4 gewonnen" soll im Doppel dasselbe Match finden wie im Einzel.

Geprüft wird, was sich prüfen lässt: Im Einzel ist ein Eintrag mit zwei Spielern
ein Fehler, im Doppel einer mit einem. Ein Spieler steht in höchstens einem
Team — wer in zwei stünde, müsste gegen sich selbst spielen. Und ein Doppel
braucht zwei verschiedene Spieler.

### Was sich nicht ändert

Auslosung, Bracket, Kreisverfahren, Tabelle, Platzierung, Satzprüfung,
Freilose: alles unberührt. Sie rechnen mit Teilnehmern, und ein Team ist ein
Teilnehmer. Ein Doppelturnier ist damit kein zweiter Turniertyp, sondern
dasselbe Turnier mit anderen Namen darin — das ist der eigentliche Gewinn
dieser Entscheidung.

## Konsequenzen

- 64 Teilnehmer heißen im Doppel 64 Teams, also bis zu 128 Spieler. Das ist
  keine neue Grenze, nur eine andere Lesart derselben.
- Wer die Disziplin wechseln will, nachdem er Namen eingetragen hat, muss die
  Liste leeren. Das ist unbequem und richtig: Die Alternative wäre, Teams aus
  Einzelnamen zu erfinden.
- Ein Spielername, der in zwei Teams gehört, geht nicht. Namensgleichheit unter
  Freunden löst man wie bisher: mit einem Zusatz im Namen.
- Mixed gibt es nicht. Ein Doppel mit gemischten Paaren ist ein Doppel; wer die
  Regel braucht, schreibt sie in den Turniernamen. Käme Mixed als Wert hinzu,
  verhielte es sich wie Doubles — das wäre dann ein eigenes ADR.

## Verworfene Optionen

**Ein Team als eigenes Aggregat mit Spielerstamm.** Das ist der alte Baum:
`TeamFormationService`, Partner-E-Mails, Teamnamen, eine Paarungsregel. Er
konnte mehr und wurde weniger benutzt. ADR-0016 hat ihn nicht aus Versehen
gelöscht.

**Das Doppel nur als Schreibweise im Namen.** Der Zustand von vorher: billig,
aber es gibt keine Prüfung, kein Finden über einen Spieler und keine Antwort auf
„ist das hier ein Doppel?". Genau daran scheitert es heute.

**Die Disziplin aus den Einträgen ableiten** — wer zwei Namen einträgt, spielt
Doppel. Dann entscheidet der erste Eintrag, was für ein Turnier es wird, und der
zweite widerspricht ihm. Dieselbe Begründung steht schon in `Discipline` des
alten Baums.
