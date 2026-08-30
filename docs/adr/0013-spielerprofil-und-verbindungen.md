# ADR-0013 — Das Spielerprofil zeigt, was der Fragende ohnehin sehen darf

**Status:** Accepted

## Kontext

[ADR-0008](0008-spielerstammdaten.md) hat entschieden, dass ein Spieler
turnierübergreifend existiert, und dabei ausdrücklich als Grund genannt, sonst
sei „jede Frage nach seiner Turnierhistorie unbeantwortbar". Gestellt wurde die
Frage bis hierher nirgends: es gibt keinen Ort, an dem jemand nachsieht, wer ein
Mitspieler ist und was er gespielt hat.

[ADR-0012](0012-mitgliedschaft-statt-selbstmeldung.md) richtet MATCHDAY auf eine
Gruppe aus. Eine Gruppe braucht Personen, die man ansehen kann — sonst bleibt
sie eine Liste von Namen. Das Profil ist der erste Baustein dafür, und die
Verbindungen zwischen Spielern (unten) fallen aus derselben Rechnung ab.

Die schwierige Frage ist nicht, was ein Profil enthält, sondern **wem gegenüber**.
Ein Spieler gehört keinem Turnier; auf ihn greift der Query-Filter aus
[ADR-0004](0004-club-scoped-autorisierung.md) nicht, und die drei Regeln, die
ADR-0008 an seine Stelle gesetzt hat, kennen nur zwei Stufen: Anzeigename für
jeden, Kontaktdaten für die Turnierleitung eines Turniers, für das er gemeldet
ist. Die Historie passt in keine davon.

## Betrachtete Optionen

**A — Die Historie ist öffentlich, wie der Anzeigename.** Verworfen. Sie nennt
Turniere, und Turniere sind seit ADR-0012 privat, solange sie niemand öffnet.
„Anna Müller hat am 14. März beim Clubturnier des TC Hinterbrühl gespielt" wäre
eine Aussage über ein privates Turnier, hergeleitet über einen Spieler, der
außerhalb des Filters liegt. Damit wäre der Filter über die Hintertür
umgangen — genau der Fehler, gegen den ADR-0004 den Filter überhaupt gesetzt
hat.

**B — Ein Sichtbarkeitsschalter am Spieler: „mein Profil ist öffentlich".**
Verworfen, nicht wegen der Idee, sondern wegen ihrer Reichweite. Der Schalter
gehört einer Person, die Daten dahinter gehören mehreren: wer sein Profil
öffnet, veröffentlicht damit die Turniere seiner Gegner mit. Ein Schalter, der
über fremde Daten entscheidet, ist der falsche Schalter — und ein Schalter je
Turnier gibt es bereits (`IsPublic`).

**C — Die Historie wird über die Turniere gerechnet, die der Fragende sieht.**
Gewählt.

## Entscheidung

### Das Profil ist eine Sicht, kein Datenbestand

`GET /api/players/{id}/profile` rechnet bei jedem Aufruf über die Turniere, die
im Query-Filter des Aufrufers liegen — dieselbe Menge, aus der auch seine
Turnierliste kommt. Zwei Personen bekommen deshalb zu demselben Spieler
verschiedene Bilanzen, und das ist die Aussage und nicht ihr Fehler: **das
Profil zeigt, was der Fragende ohnehin sehen darf, an einer Stelle
zusammengefasst.**

Daraus folgt die Zugriffsregel ohne eine einzige zusätzliche Prüfung: wer mit
einem Spieler kein sichtbares Turnier teilt, sieht eine leere Rechnung — und
bekommt deshalb 404 wie überall sonst, wo der Filter greift (ADR-0004: kein 403,
das die Existenz verrät). Das eigene Profil ist die Ausnahme, die keine ist: man
teilt mit sich selbst jedes eigene Turnier.

Der Preis ist eine Zahl, die wandert. Wer einem Turnier beitritt, sieht die
Bilanz seiner Mitspieler wachsen — rückwirkend, um Matches, die schon vorher
gespielt wurden. Eine „wahre" Gesamtbilanz gibt es in dieser Anwendung nicht,
und es wäre eine Täuschung, eine anzuzeigen, die aus Turnieren stammt, die der
Betrachter nicht sehen darf.

### Gerechnet wird über die Meldungen, nicht über eine neue Tabelle

Die Spielerliste eines Teilnehmers steht als Text in einer Spalte und ist nicht
durchsuchbar — [ADR-0006](0006-sqlite-als-startdatenbank.md) verbietet das
serverseitige Durchsuchen zusammengesetzter Werte, und `IsEnteredInTournamentAsync`
löst dasselbe Problem bereits so: erst die Meldungen der infrage kommenden
Turniere holen, dann im Speicher zuordnen.

Der Weg trägt hier aus demselben Grund, aus dem er die Zugriffsregel trägt: die
infrage kommenden Turniere sind die des Aufrufers, und das sind wenige. Eine
Verknüpfungstabelle `ParticipantPlayers` wäre die Alternative; sie bliebe nötig,
sobald jemand die Bilanz über *alle* Turniere sehen soll — und genau das ist
oben verworfen. Sie zu bauen, hieße eine Abfrage zu ermöglichen, die niemand
stellen darf.

### Was der Spieler selbst hinzufügt, gehört ihm

`Player.Profile` trägt zwei Angaben, die niemand berechnen kann: einen kurzen
Text über sich und den Heimatverein. Ändern darf sie ausschließlich das Konto,
mit dem der Spieler verbunden ist (`Player.UserAccountId`) — nicht die
Turnierleitung, die ihn eingelesen hat. Wer aus einer hochgeladenen Liste kommt,
hat kein Konto und damit kein Profil zum Pflegen; er hat trotzdem eine Historie,
und die steht da.

Sie liegen als eigener Werttyp neben `PlayerContact` und nicht in ihm: Kontakt
ist das, was nie nach außen darf, Profil das, was ausdrücklich dafür geschrieben
wurde. In einem Typ vereint, wäre die eine Regel beim Abbilden auf ein DTO nicht
mehr von der anderen zu unterscheiden.

### Verbindungen entstehen aus gespielten Matches, nicht aus Anfragen

`GET /api/me/connections` liefert die Spieler, mit denen der Aufrufer gespielt
hat — als Partner im Doppel oder als Gegner —, mit Zählern und dem Datum des
letzten gemeinsamen Matches. Es gibt keine Freundschaftsanfrage und keine
Bestätigung.

Das ist eine Entscheidung und keine Auslassung. Eine Anfrage wäre ein zweiter
Beziehungsbegriff neben dem, der ohnehin entsteht, mit eigenem Zustand, eigener
Ablehnung und eigener Oberfläche — und mit der Eigenschaft, am Anfang leer zu
sein. Der Graph aus gespielten Matches ist am ersten Tag gefüllt, in dem
Augenblick, in dem das erste Ergebnis eingetragen wird. Er wird über dieselbe
Rechnung gebildet wie das Profil und erbt damit dessen Sichtbarkeitsregel.

## Konsequenzen

- Ein Profil ist nie „falsch", aber es ist auch nie absolut. Jede Zahl darin
  gilt relativ zum Fragenden, und die Oberfläche sagt das, statt es zu
  verschweigen.
- Der Aufwand einer Profilabfrage wächst mit der Zahl der Turniere des
  Aufrufers, nicht mit der Größe der Datenbank. Für eine Vereinsanwendung ist
  das die richtige Richtung; ab einer Größenordnung, in der jemand hundert
  Turniere sieht, wäre eine Projektion nach [ADR-0003](0003-getrenntes-read-modell.md)
  der nächste Schritt — sie fügt sich hinter demselben Anwendungsfall ein.
- Ein Spieler ohne Konto hat ein Profil, das nur aus seiner Historie besteht.
  Sobald er beitritt und `LinkAccount` greift, gehört ihm auch der Text darin —
  ohne dass etwas zu übertragen wäre.
- Was hier nicht entschieden wird: eine turnierübergreifende Wertungszahl. Sie
  bräuchte eine Bilanz über alle Turniere, und die gibt es nach dieser
  Entscheidung nicht.
