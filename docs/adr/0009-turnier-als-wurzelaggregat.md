# ADR-0009 — Das Turnier ist die Wurzel, der Verein entfällt

**Status:** Accepted

**Ersetzt:** [ADR-0004](0004-club-scoped-autorisierung.md)

## Kontext

Die Anwendung zwang einen Weg auf, den es in der Wirklichkeit nicht gibt: erst
einen Verein anlegen, dann darin ein Turnier, dann die Spieler von Hand
eintippen. Wer ein Turnier veranstaltet, denkt anders. Er hat eine Anlage, dort
ein paar reservierte Plätze, eine Turnierart im Kopf — und will die Anmeldung
freigeben.

Der Verein stand dabei an drei Stellen im Weg:

1. **Als Pflichtschritt vor dem ersten Turnier.** Anlegen durfte ihn nur ein
   `SystemAdmin`. Auf einer frischen Datenbank hatte niemand diese Rolle, und
   damit kam eine neue Instanz ohne Eingriff in die SQLite-Datei nicht in Gang.
2. **Als Eigentümer der Plätze.** Der Solver nahm einfach alle aktiven Plätze des
   Vereins. Eine Auswahl je Turnier gab es nicht — die Oberfläche gab das offen
   zu.
3. **Als Sichtbarkeitsschlüssel.** Wer zwei Vereine verwaltete, sah alles, was
   dort je stattgefunden hatte.

Dabei ist der Verein für nichts davon der richtige Träger. Reserviert wird
außerhalb dieser Anwendung — der Veranstalter ruft an. Was er zugesagt bekommt,
gilt für dieses eine Turnier.

## Betrachtete Optionen

**A — Verein bleibt, wird aber optional.** Ein Turnier ohne Verein wäre erlaubt,
mit Verein ginge weiter alles wie bisher. Verworfen: zwei Platzquellen sind zwei
Verfügbarkeitswahrheiten. Jede Frage nach „steht dieser Platz zur Verfügung"
hätte zwei Antworten, und die Stelle, die sie zusammenführt, gäbe es nirgends.

**B — Verein bleibt als reine Stammdatenverwaltung neben dem Turnier.**
Verworfen: dann pflegt ihn niemand, weil er für nichts gebraucht wird — und was
niemand pflegt, wird falsch.

**C — Der Verein entfällt ersatzlos, das Turnier trägt alles selbst.** Gewählt.

## Entscheidung

`Tournament` ist das Wurzelaggregat. Es trägt:

- `Venue` — Name, Adresse, Ort, Zeitzone. Ein Wertobjekt in den Spalten des
  Turniers, keine eigene Tabelle: ein Ort ohne Turnier hat keine Bedeutung, und
  zwei Turniere an derselben Anlage teilen sich nichts.
- `Discipline` — Einzel, Doppel, Mixed. Sie steht in der Ausschreibung und
  entscheidet beim Melden, ob ein Partner dazugehört. Vorher ergab sie sich nur
  daraus, was jemand als Teilnehmer anlegte.
- `TournamentCourt` samt `CourtWindow` — die Plätze und die Zeiten, zu denen sie
  dem Turnier gehören.
- `RegistrationLink` — siehe [ADR-0010](0010-oeffentliche-selbstmeldung.md).

**Platzzeiten sind absolute Fenster, kein Wochentagsraster.** „Platz 3 am
16. Mai von 9 bis 18" ist genau das, was am Telefon vereinbart wurde. Ein
Wochentagsraster mit Gültigkeitszeitraum waren Vereinsstammdaten; mit dem Verein
entfällt auch die Sperre, denn im Turnierkontext legt man ein Fenster schlicht
nicht an.

### Rollen

| Rolle | Scope | |
|---|---|---|
| `SystemAdmin` | Global | bleibt |
| `Organizer` | Global | **neu** — darf Turniere anlegen, mehr nicht |
| `TournamentDirector` | Tournament | bleibt |
| `Referee` | Tournament | bleibt |
| `ClubAdmin`, `Player` | — | entfallen |

`Organizer` bekommt jeder angemeldete Benutzer beim ersten Request
(`OrganizerBootstrap`, abschaltbar über `Security:SelfServiceOrganizers`). Die
Rolle ist global und trotzdem harmlos: ihr einziges Recht ist
`CreateTournament`, und was daraus entsteht, gehört seinem Anleger allein.

Sie wird **ausdrücklich vergeben** und nicht aus `IsAuthenticated` abgeleitet.
Eine echte Zuweisung ist abfragbar, entziehbar und abschaltbar; eine unsichtbare
Regel im Code wäre nichts davon.

**Wer anlegt, wird Turnierleiter — in einer Arbeitseinheit.** Dafür gibt es
`IRoleAssignmentRepository`, einen Port ohne eigenes Speichern. Ginge die
Zuweisung über `IUserDirectory.AssignAsync`, das selbst speichert, könnte das
Turnier entstehen und die Rolle nicht — und das Turnier wäre für seinen Anleger
im nächsten Augenblick unauffindbar.

### Query-Filter

Er wird **strenger**, nicht schwächer. Es gibt genau einen Weg zu einem Turnier:
eine Rolle an genau diesem Turnier.

```
Tournament       : SiehtAlles || SichtbareTurniere.Contains(t.Id)
TournamentCourt  : SiehtAlles || SichtbareTurniere.Contains(c.TournamentId)
CourtWindow      : SiehtAlles || SichtbareTurniere.Contains(w.TournamentId)
TournamentEntry  : SiehtAlles || SichtbareTurniere.Contains(e.TournamentId)
FormatTemplate   : SiehtAlles || OwnerUserId == null || OwnerUserId == Aufrufer
```

Plätze, Fenster und Meldungen prüfen einstufig gegen die sichtbaren Turniere und
tragen dafür alle eine eigene `TournamentId`, auch wo sie über eine Navigation
erreichbar wären. Eine Filterkette über zwei Ebenen wäre in EF Core
fehleranfällig bis langsam — und der Filter soll gerade nicht davon abhängen,
auf welchem Weg jemand abfragt.

**Was aus ADR-0004 gilt weiter:** der Query-Filter ist die Sicherheitsgrenze,
die Endpunkt-Prüfung die zweite Verteidigungslinie, und ein Zugriff außerhalb
des Scopes wird als 404 beantwortet, nicht als 403. Ersetzt ist nur der
Schlüssel: Turnier statt Verein.

## Konsequenzen

**Kein datenerhaltender Pfad.** Die sechs Migrationen wurden gelöscht und durch
eine Baseline ersetzt. Eine bestehende Datei aus der Zeit davor lässt sich nicht
migrieren; sie wird gelöscht (siehe README).

**Vergessene Platzzeiten werden laut.** Bisher lieferte ein Verein ohne
Öffnungszeiten einen leeren Spielplanvorschlag mit lauter „nicht angesetzt".
Künftig ist das der Normalfall eines frisch angelegten Turniers, und
`RequireCourtTimesRecorded()` weist den Vorschlag ausdrücklich ab.

Abweichend vom ursprünglichen Entwurf greift die Prüfung **nur beim
Spielplanvorschlag, nicht beim Auslosen**: Meldeschluss und Platzbuchung sind
zwei Vorgänge, und wer zuerst auslost und dann beim Verein anruft, geht keinen
falschen Weg. Blockiert wird dort, wo die leere Antwort sonst unerklärlich wäre.

**Formatvorlagen gehören ihrem Anleger.** Sie gehörten dem Verein; ohne
Eigentümer wären sie für niemanden mehr auffindbar. Die deterministischen Ids
der mitgelieferten Vorlagen bleiben unverändert.

**Ein Ort wird je Turnier neu eingegeben.** Wer dreimal im Jahr auf derselben
Anlage ausschreibt, tippt sie dreimal. Das ist der Preis dafür, dass niemand
eine Anlage pflegen muss, bevor er ein Turnier ausschreiben kann — und die
richtige Richtung für den Fall, der zählt: das erste Turnier eines neuen
Benutzers.
