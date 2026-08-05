# Architekturentscheidungen

Jede Datei hält eine Entscheidung fest: den Kontext, die verworfenen Optionen, die
gewählte Lösung und die Konsequenzen — auch die unangenehmen. Der Wert liegt in den
verworfenen Optionen: sie verhindern, dass dieselbe Diskussion in einem halben Jahr
noch einmal von vorn beginnt.

| Nr. | Titel | Status |
|---|---|---|
| [0001](0001-turnierformate-als-phasen.md) | Turnierformate als komponierbare Phasen | Accepted |
| [0002](0002-scheduling-planungsraster-und-queue.md) | Scheduling: Planungsraster und Turniertag-Queue | Accepted |
| [0003](0003-getrenntes-read-modell.md) | Getrenntes Read-Modell für die öffentliche Ansicht | Accepted |
| [0004](0004-club-scoped-autorisierung.md) | Autorisierung ist club-scoped | Superseded by 0009 |
| [0005](0005-hexagonale-architektur.md) | Hexagonale Architektur mit erzwungenen Fitnessfunktionen | Accepted |
| [0006](0006-sqlite-als-startdatenbank.md) | SQLite als Startdatenbank, PostgreSQL als Zielbild | Accepted |
| [0007](0007-externer-identity-provider.md) | Externer Identity Provider, Rollen bleiben in der Anwendung | Accepted |
| [0008](0008-spielerstammdaten.md) | Spieler existieren vereinsübergreifend | Superseded by 0009 |
| [0009](0009-turnier-als-wurzelaggregat.md) | Das Turnier ist die Wurzel, der Verein entfällt | Accepted |
| [0010](0010-oeffentliche-selbstmeldung.md) | Öffentliche Selbstmeldung über einen Token-Link | Accepted |

## Status

`Proposed` → zur Diskussion gestellt. `Accepted` → gilt und wird umgesetzt.
`Superseded by NNNN` → durch eine spätere Entscheidung ersetzt; der Text bleibt
stehen, damit die Historie lesbar bleibt.
