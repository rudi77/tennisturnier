# tennisturnier

Turnierplattform für Tennisvereine: Platzverwaltung, Turniere in verschiedenen Modi
(K.O., Gruppenphase + K.O., Liga, Schweizer System), automatischer und manuell
korrigierbarer Spielplan sowie eine öffentliche Live-Ansicht.

**Status:** im Aufbau. Der Fahrplan steht in [docs/roadmap.md](docs/roadmap.md).

## Schnellstart

```bash
dotnet restore
dotnet tool restore
dotnet build
dotnet test
```

Voraussetzung ist das .NET 10 SDK. In Claude-Code-Web-Sessions erledigt der
SessionStart-Hook (`.claude/hooks/setup.sh`) Installation und Restore automatisch.

### Anwendung starten

```bash
docker compose up -d keycloak          # lokaler Identity Provider mit Test-Realm
dotnet run --project src/TennisTurnier.Api
```

Die Datenbank ist eine SQLite-Datei, die beim Start angelegt und migriert wird.
Ohne Keycloak startet die Anwendung ebenfalls — dann sind nur die öffentlichen
Endpunkte erreichbar.

Ein Token für die Testbenutzer (`systemadmin`, `clubadmin`, `referee`;
Passwort jeweils gleich dem Benutzernamen):

```bash
curl -s -X POST http://localhost:8080/realms/tennisturnier/protocol/openid-connect/token \
  -d grant_type=password -d client_id=tennisturnier-api \
  -d username=systemadmin -d password=systemadmin | jq -r .access_token
```

Die Rollen selbst vergibt die Anwendung, nicht Keycloak (siehe ADR-0007) — ein
frisch angemeldeter Benutzer hat zunächst keine.

## Architektur

Ports & Adapters. Der fachliche Kern — Paarungserzeugung, Tabellen, Tiebreaker,
Satzvalidierung, Platzverfügbarkeit — liegt in `TennisTurnier.Domain` und ist ohne
Datenbank testbar.

```
src/TennisTurnier.Domain                        keine Projekt-, keine Paketreferenzen
src/TennisTurnier.Application                   → Domain (Ports + Anwendungsfälle)
src/TennisTurnier.Adapters.Persistence.Sqlite   → Application (EF Core)
src/TennisTurnier.Adapters.Identity.Oidc        → Application (Keycloak / Entra ID)
src/TennisTurnier.Adapters.Scheduling           → Application (Spielplan-Solver)
src/TennisTurnier.Api                           → alle (Composition Root, Minimal API)
```

Die Abhängigkeitsrichtung wird nicht per Konvention gepflegt, sondern in
`tests/TennisTurnier.Architecture.Tests` bei jedem Build geprüft.

Die tragenden Entscheidungen samt verworfener Alternativen stehen in
[docs/adr](docs/adr/README.md) — insbesondere:

- [ADR-0001](docs/adr/0001-turnierformate-als-phasen.md): Turnierformate sind
  komponierbare Phasen, kein Enum und kein Plugin-System.
- [ADR-0002](docs/adr/0002-scheduling-planungsraster-und-queue.md): Spielplan im
  Planungsmodus, Court-Queues am Turniertag — weil Matchdauern unbekannt sind.
- [ADR-0003](docs/adr/0003-getrenntes-read-modell.md): eigene Projektion für die
  öffentliche Ansicht.
- [ADR-0004](docs/adr/0004-club-scoped-autorisierung.md): Rollen sind an Verein oder
  Turnier gebunden, durchgesetzt per Query-Filter.
- [ADR-0008](docs/adr/0008-spielerstammdaten.md): Spieler existieren
  vereinsübergreifend — samt dem Preis, dass der Query-Filter bei ihnen nicht greift.

## Spielplan

Im Planungsmodus rechnet `POST /api/tournaments/{id}/schedule/proposal` einen
Vorschlag, ohne etwas zu verändern; erst `…/schedule/confirm` trägt ihn ein.
Diese Trennung ist Absicht (ADR-0002): ein Solverlauf, der den Plan still
überschreibt, ist der Grund, aus dem Turnierleitungen die Automatik abschalten.

Der Vorschlag nennt zu jeder Ansetzung, was sie bindet — „frühestmöglich nach
dem Vorspiel, das um 14:30 endet, zuzüglich 30 Minuten Pause" — und dazu einen
Diff: wie viele Ansetzungen bleiben, entstehen, sich verschieben. Von Hand
gesetzte und festgenagelte Zuweisungen gehen als harte Vorgabe in den nächsten
Lauf, und was zulässig bleiben kann, bleibt stehen: eine Verschiebung von Hand
bewegt nur das, was im Baum daran hängt oder ihr im Weg liegt.

Geprüft wird der Vorschlag vom selben `ScheduleValidator`, der auch eine
Verschiebung von Hand beurteilt. Ein Solver, der seine eigenen Ergebnisse für
zulässig erklärt, prüft nichts.

## Turniertag

`GET /api/tournaments/{id}/courts` zeigt je Platz, was gerade läuft und wer
wartet. Am Platz wird über `POST /api/assignments/{id}/call|start|finish|suspend`
gearbeitet — das darf auch der Schiedsrichter, er steht dort. Disponiert wird
getrennt davon: die Reihenfolge einer Warteschlange über
`POST /api/tournaments/{id}/courts/{courtId}/queue`, eine Zusage über
`…/assignments/{id}/promise`, die Fortsetzung einer unterbrochenen Partie über
`…/resume`. Diese drei verschieben alles dahinter und gehören deshalb der
Turnierleitung, nicht der Ergebniseingabe.

Der ganze Tagesbetrieb setzt den Turniertagmodus voraus — auch das Umstellen und
das Zusagen, die im Planungsmodus nur den gerechneten Spielplan zerstören
würden, ohne inhaltlich etwas zu ändern.

Die harte Randbedingung ist, dass die Matchdauer unbekannt ist. Deshalb ist die
**Reihenfolge** auf dem Platz die Aussage, nicht die Uhrzeit: die Schätzungen
der Wartenden werden nachgezogen, sobald tatsächlich etwas passiert, und die
Warteschlange nummeriert sich lückenlos neu — „Sie sind der Dritte auf Platz 2"
ist eine Auskunft, keine Sortierhilfe. Eine Zusage („nicht vor 14 Uhr") wird
dabei nie unterlaufen, auch wenn der Platz früher frei wird.

Weil jedes überzogene Match die Warteschlange nach hinten schiebt, steht das
Finale irgendwann rechnerisch um halb zwei nachts. Das ist keine Fehlfunktion,
sondern eine Auskunft, die die Turnierleitung braucht: jedes wartende Match
trägt `withinOpeningHours`, sobald seine Schätzung nicht mehr in die
Öffnungszeiten des Platzes passt.

Aufgerufen wird nur, wer feststeht. Eingeplant ist der ganze Baum, lange bevor
die Teilnehmer bekannt sind — am Platz wird aber kein Platzhalter ausgerufen.
Umgekehrt wird nicht jedes Match am Platz aufgerufen: ein Nichtantreten wird
eingetragen, ohne dass jemand hingeht, und gibt den Platz sofort frei.

Eine Unterbrechung lässt die Zuweisung als Historie stehen; die Fortsetzung
kann auf einem anderen Platz stattfinden und ist dann eine eigene Zuweisung.
Erst beide zusammen erzählen, was an diesem Tag passiert ist — genau deshalb
ist die Platzzuweisung eine eigene Entität (ADR-0002). Die alte Zuweisung wird
dabei ausdrücklich abgeschlossen: bliebe sie unterbrochen, ließe sie sich ein
zweites Mal fortsetzen, und dieselbe Partie liefe auf zwei Plätzen.

Das Ergebnis wird getrennt eingetragen: der Platz ist frei, sobald die Spieler
ihn verlassen, und nicht erst, wenn jemand Zeit hatte, den Zettel auszufüllen.

## Öffentliche Ansicht

`GET /public/tournaments/{id}` liefert ohne Anmeldung Bracket, Tabellen und die
aktuelle Platzbelegung — mit `ETag` und `Cache-Control`; ein zweiter Abruf mit
`If-None-Match` bekommt 304. Wer live zusehen will, abonniert im SignalR-Hub
`/hubs/tournament` sein Turnier und wird bei jeder inhaltlichen Änderung
benachrichtigt. Der Push trägt nur Turnier-Id und ETag: geholt wird die Ansicht
über denselben Endpunkt, den auch Polling benutzt.

Die Antwort kommt aus einer eigenen Projektion, nicht aus dem Schreibmodell
(ADR-0003). Sie ist die einzige Tabelle ohne Query-Filter, und genau deshalb
entscheidet allein
`TennisTurnier.Application.PublicView.TournamentViewBuilder`, was öffentlich
wird. Keine Kontaktdaten, keine Geburtsdaten, keine internen Notizen zu
Platzsperren und keine Ids von Personen. Ein Test in
`TennisTurnier.Api.Tests` prüft die ausgelieferte Antwort gegen eine
Verbotsliste — sonst rutscht das erste zusätzliche Feld unbemerkt hinaus.

Vor der Auslosung gibt es keine öffentliche Ansicht, und eine zurückgenommene
Auslosung lässt sie wieder verschwinden.

## Turnierformate

Ein Turniermodus ist eine geordnete Folge von Phasen. „Gruppenphase mit
anschließendem K.o." ist deshalb kein eigenes Format, sondern eine Komposition
aus einer Round-Robin- und einer K.-o.-Phase. Ein eigener Modus entsteht als neue
Vorlage — neue Phasenfolge, neue Parameter, kein Deployment.

Umgesetzt sind K.-o.-System, Round Robin und das Schweizer System.
Mitgeliefert sind `ko-single`, `group-then-ko`, `liga-round-robin` und `swiss`.
Sie lassen sich nicht ändern, aber kopieren; die Kopie gehört dem Verein und ist
frei bearbeitbar.

Beim Auslosen wird die Definition in das Turnier kopiert und eingefroren. Wer die
Vorlage danach nachschärft, verändert damit kein laufendes Turnier.

### Von der Gruppe in die Endrunde

Beim Auslosen entstehen alle Phasen — auch die Endrunde, für die noch niemand
qualifiziert ist. Ihre Startplätze sind zunächst Gruppenplätze („Erster der
Gruppe A"), und genau daraus steht das Bracket, während die Gruppen noch laufen.
Ist eine Gruppenphase durch, werden die Plätze besetzt: derselbe Mechanismus wie
der Übergang vom Viertel- ins Halbfinale, kein Sonderfall.

Die Setzung der Qualifikanten ist so gewählt, dass ein Gruppensieger im ersten
K.-o.-Match auf den Zweiten einer *anderen* Gruppe trifft — sonst spielten zwei,
die gerade erst gegeneinander angetreten sind, sofort wieder gegeneinander.

Punktgleichheit löst eine geordnete Kette auf: direkter Vergleich, Satz-,
Spielverhältnis, Buchholz, Los. Die Reihenfolge kommt aus der Phasendefinition,
nicht aus dem Code — sie ist eine Festlegung der Ausschreibung. Der direkte
Vergleich zählt dabei nur die Begegnungen der Punktgleichen untereinander; bei
einem Dreier-Ringschluss entscheidet das nächste Kriterium.

### Das Schweizer System

Alle spielen jede Runde, gepaart wird nach Punktestand. Das ist das einzige
Format, dessen Draw beim Auslosen unvollständig ist: nur die erste Runde steht.
Jede weitere entsteht, sobald die vorige gespielt ist — sie hängt davon ab, wie
sie ausgegangen ist, und ein Draw, der sie vorab zeigte, zeigte eine Erfindung.

Gepaart wird nach dem Dutch-System: die Tabelle zerfällt in Punktgruppen, jede
Punktgruppe in obere und untere Hälfte, gepaart wird über Kreuz. Bleibt in einer
Punktgruppe jemand übrig, steigt er in die nächste ab — von unten, denn wer in
seiner Gruppe hinten steht, soll nicht die leichtere Aufgabe bekommen.

Darüber steht die Bedingung, dass sich zwei Spieler nicht zweimal begegnen. Sie
lässt sich nicht durch Sortieren erfüllen, sondern nur suchend: gefunden wird
die Paarung, die der idealen am nächsten kommt und keine Wiederholung enthält.
Geht das innerhalb der Punktgruppen nicht auf, gilt die Regel vor der Konvention
und es wird über das ganze Feld gesucht.

Gepaart wird nach dem Stand von jetzt, ohne Vorausschau. Bei sehr vielen Runden
kann sich das Verfahren damit selbst in eine Runde manövrieren, für die es keine
wiederholungsfreie Paarung mehr gibt. Dann wird wiederholt — und die Paarung
trägt es im Namen („Runde 6 · Wiederholung"). Abzubrechen wäre die schlechtere
Antwort: es hieße, dass sich das letzte Ergebnis der vorigen Runde nicht mehr
eintragen lässt und das Turnier ohne Vor- und Rückweg steht.

Bei ungerader Teilnehmerzahl setzt jede Runde einer aus — der Letzte der
Tabelle, der noch kein Freilos hatte, und höchstens einmal pro Turnier. Das
Freilos zählt wie ein Sieg: sonst fiele zurück, wer nichts dafür kann.
Entsprechend sind bei geradem Feld höchstens *n-1* Runden möglich und bei
ungeradem *n*; mehr weist die Auslosung ab. Das ist eine Grenze der
Möglichkeit, keine Empfehlung — je näher die Rundenzahl ihr kommt, desto
wahrscheinlicher wird eine Wiederholung.

Die Rundenzahl kommt aus der Definition, ohne Angabe `ceil(log2(n))` — so viele
Runden, wie ein K.-o.-Baum desselben Feldes hätte. Die Tabelle entscheidet
Punktgleichheit zuerst über Buchholz, die Summe der Punkte aller Gegner: nach
fünf Runden stehen regelmäßig ein halbes Dutzend Spieler auf demselben
Punktestand, und ohne dieses Kriterium wäre die Tabelle weitgehend aussagelos.

Wird ein Ergebnis korrigiert, werden alle daraus entstandenen Runden
zurückgenommen und neu gepaart — mit ihnen weiterzuspielen hieße, Paarungen zu
verwenden, die niemand mehr herleiten kann. Ist eine dieser Runden schon
gespielt oder steht eine ihrer Partien am Platz, wird die Korrektur abgewiesen:
diese Kette muss von hinten aufgerollt werden.

Das gilt für beide Wege, ein Ergebnis zu ändern. Eine Korrektur durch
Überschreiben (`PUT`) wird deshalb als das ausgeführt, was sie ist: erst
zurücknehmen, dann neu eintragen. Sonst verhielten sich die beiden Wege
unterschiedlich, und nur einer von ihnen zöge die Folgen nach.

## Der Turnierbaum

Beim Auslosen entsteht der vollständige Baum — auch die späteren Runden, deren
Teilnehmer noch niemand kennt. Möglich macht das ein Summentyp: eine Seite eines
Matches ist entweder eine Meldung, „Sieger aus Match X", „Verlierer aus Match X",
„Zweiter der Gruppe B", ein Freilos oder schlicht offen.

Daraus folgt zweierlei. Die öffentliche Ansicht kann das Bracket zeigen, bevor
ein Ball gespielt ist. Und der Übergang von der Gruppenphase in die Endrunde ist
derselbe Mechanismus wie der vom Viertel- ins Halbfinale: eine Referenz wird
aufgelöst, sobald ihr Vorgänger entschieden ist.

Ein Ergebnis wird deshalb nicht nur eingetragen, sondern weitergereicht. Eine
Korrektur geht denselben Weg zurück — allerdings nur, solange das Folgematch
noch nicht gespielt ist. Sonst stünde in der nächsten Runde jemand, der laut
korrigiertem Ergebnis nie hätte antreten dürfen; diese Kette muss von hinten
aufgerollt werden.

Ergebnistypen gibt es von Anfang an fünf: reguläres Ende, Aufgabe, Nichtantreten,
Disqualifikation und Freilos. Bei einer Aufgabe wird der abgebrochene Satz
getrennt von den gespielten geführt — seine Spiele zählen für das
Spielverhältnis, der Satz selbst für niemanden.
