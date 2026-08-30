# ADR-0014 — Das Turnier redet: ein Feed statt einer stillen Chronik

**Status:** Accepted

## Kontext

[ADR-0012](0012-mitgliedschaft-statt-selbstmeldung.md) hat das Turnier zur
Gruppe erklärt und sich dabei ausdrücklich auf die WhatsApp-Gruppe berufen: wer
dazugehört, sieht den ganzen Verlauf. Gebaut wurde davon die Zugehörigkeit — die
Sichtbarkeit von Draw, Spielplan und Ergebnissen.

Was fehlt, ist das Reden. Eine WhatsApp-Gruppe ist keine Gruppe, weil alle
denselben Spielplan sehen, sondern weil jemand hineinschreiben kann: „Platz 3
ist nass", „ich komme zwanzig Minuten später", „gratuliere". Genau dafür
verlassen die Vereine diese Anwendung wieder und machen daneben eine
WhatsApp-Gruppe auf — und dort steht dann der Spielplan als Foto.

Die zweite Hälfte ist die Chronik. Ergebnisse werden eingetragen, der Draw wird
veröffentlicht, der Spielplan bestätigt — und niemand erfährt es, außer er
schaut nach. Ein Turnier hat einen Verlauf; er steht bislang nur verstreut in
den Zuständen der Aggregate und nirgends als Erzählung.

## Betrachtete Optionen

**A — Nur eine Chronik, ohne Beiträge.** Verworfen. Sie beantwortet „was ist
passiert" und nicht „was ist los". Die Turnierleitung, die mitteilen will, dass
sich alles um eine Stunde verschiebt, hätte weiterhin keinen Ort dafür — und
griffe wieder zum Telefon.

**B — Nur Beiträge, ohne Chronik.** Verworfen aus dem umgekehrten Grund: ein
leerer Kasten mit „Schreib etwas" ist die zuverlässigste Art, dass niemand
schreibt. Die Ereignisse füllen ihn vom ersten Tag an, und ein Kommentar unter
einem Ergebnis ist ein niedrigerer Einstieg als ein Beitrag ins Leere.

**C — Beides in einem Strom, Kommentare an jedem Eintrag.** Gewählt.

## Entscheidung

### Ein Eintrag ist ein Text, kein Verweis

`TournamentPost` trägt seinen fertigen Text. Auch der eines Ereignisses: „Anna
Müller schlägt Lena Berger 6:4 6:2" wird beim Eintragen des Ergebnisses
geschrieben und danach nicht mehr angefasst.

Das ist die wichtigste Entscheidung hier, und sie ist bewusst die unelegantere.
Ein Eintrag, der zur Anzeigezeit aus dem Match gerendert würde, wäre
normalisiert und immer aktuell — und genau deshalb falsch: ein Feed ist ein
Protokoll. Wird ein Ergebnis später korrigiert, soll die alte Zeile stehen
bleiben und eine neue darunter kommen. Eine Chronik, die sich rückwirkend
ändert, ist keine.

Es folgt daraus auch, dass ein gelöschtes Turnier seine Einträge mitnimmt
(Kaskade) und dass ein Eintrag ohne sein Turnier keinen Sinn hat.

### Wer dazugehört, darf schreiben

Die Rechtematrix bekommt `WriteInFeed`, und das Mitglied bekommt es — damit
trägt es zwei Rechte statt einem. ADR-0012 hat „genau ein Recht" geschrieben;
diese Entscheidung nimmt das zurück, und zwar an der Stelle, an der die
Begründung von damals hinführt: eine Gruppe, in der nur einer reden darf, ist
so wenig eine Gruppe wie eine, in der niemand sieht, wer dabei ist.

Systemeinträge haben keinen Verfasser (`AuthorUserId` ist leer). Sie einem
Benutzer zuzuschreiben — dem Schiedsrichter, der das Ergebnis eintippt — wäre
eine Behauptung über eine Handlung, die dem Turnier gehört und nicht ihm.

### Löschen ja, Bearbeiten nein

Der Verfasser darf seinen Beitrag zurücknehmen, die Turnierleitung jeden — das
ist Moderation und in einer Vereinsgruppe gelegentlich nötig. Ein
Systemeintrag lässt sich nicht löschen: er ist die Chronik, und wer sie ändern
darf, hat keine.

Bearbeiten gibt es nicht. Ein Beitrag, der sich ändern lässt, verlangt einen
Bearbeitungsvermerk, sonst ist die Antwort darunter plötzlich eine Antwort auf
etwas anderes. Zurücknehmen und neu schreiben leistet dasselbe und ist ehrlich.

### Der Push trägt kein Wort

Der bestehende SignalR-Hub ist ohne Anmeldung erreichbar (ADR-0003) — er trägt
die Id eines Turniers und einen ETag, also nichts, was nicht ohnehin öffentlich
wäre. Der Feed ist das Gegenteil: er ist die Innenansicht der Gruppe.

Deshalb geht über den Hub weiterhin nur ein Hinweis: „an diesem Turnier hat sich
im Feed etwas getan". Wer daraufhin abholt, tut das über den angemeldeten
Endpunkt, und dort entscheidet der Query-Filter. Ein zweiter, angemeldeter Hub
wäre die Alternative gewesen; er hätte zwei Verbindungen, zwei
Wiederanlaufregeln und zwei Stellen bedeutet, an denen ein Autorisierungsfehler
entstehen kann — für einen Gewinn, der in einem gesparten HTTP-Aufruf besteht.

## Konsequenzen

- Der Feed wächst unbegrenzt. Für ein Vereinsturnier ist das ohne Belang; die
  Abfrage liefert die jüngsten Einträge mit einem Zeitstempel als Cursor, damit
  ein späteres Nachladen keine neue Schnittstelle braucht.
- Ein Ereignis wird in derselben Arbeitseinheit geschrieben wie das, was es
  meldet. Scheitert das Eintragen des Ergebnisses, entsteht auch kein Eintrag —
  das ist die Eigenschaft, die eine nachgelagerte Warteschlange nicht hätte.
- Was hier nicht entschieden wird: Benachrichtigungen. Ein Feed, den man sieht,
  wenn man hinschaut, ist der erste Schritt; einer, der auf dem Telefon klingelt,
  ist eine eigene Entscheidung mit eigenen Kosten.
