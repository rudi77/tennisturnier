# Das Domänenmodell

Stand: nach M15 (Profil, Feed, Kontakte, Verabredungen).

Diese Datei zeichnet nach, was in `src/TennisTurnier.Domain` steht. Sie ersetzt
keine ADR — das *Warum* steht dort, hier steht das *Was*. Wenn beide sich
widersprechen, gilt der Code.

> `domaenenmodell.html` daneben ist ein älterer Export dieser Datei aus VS Code
> und wird nicht mitgepflegt. Maßgeblich ist das Markdown.

## Lesehilfe

- **Aggregatwurzeln** sind fett benannt: `Tournament`, `Phase`, `Participant`,
  `Player`, `CourtAssignment`, `FormatTemplate`, `UserAccount`,
  `TournamentProjection`, `TournamentPost`, `PlayDate`.
- **(VO)** kennzeichnet ein Wertobjekt. Es hat keine eigene Identität und wird
  mit seinem Besitzer geschrieben — in der Datenbank meist als Spalten derselben
  Zeile oder als JSON.
- Beziehungen **über eine Aggregatgrenze hinweg** sind reine Id-Referenzen. Es
  gibt keine Objektnavigation von `Match` zu `TournamentEntry`; es gibt eine
  `Guid`.
- `Phase`, `Match`, `CourtWindow` und `CourtAssignment` tragen alle eine eigene
  `TournamentId`, obwohl sie über ihren Vater erreichbar wären. Das ist Absicht:
  der Query-Filter aus ADR-0004 arbeitet auf der Menge der sichtbaren Turniere
  und bliebe sonst zweistufig.

---

## 1. Der Turnierkern

```mermaid
erDiagram
    TOURNAMENT {
        Guid Id PK
        string Name
        Discipline Discipline "Singles / Doubles / Mixed"
        DateOnly StartsOn
        DateOnly EndsOn
        TournamentState State "Draft .. Completed / Abandoned"
        SchedulingMode SchedulingMode "Planning / MatchDay"
        Guid FormatTemplateId FK
        int Version "optimistische Nebenlaeufigkeit"
    }

    VENUE {
        string Name
        string Address "optional"
        string City "optional"
        string TimeZoneId "IANA, z.B. Europe/Vienna"
    }

    REGISTRATION_LINK {
        string Token "128 Bit Base64Url, rotierbar"
        int Capacity "offen = unbegrenzt"
        DateTimeOffset Deadline "offen = bis zum Meldeschluss von Hand"
    }

    FORMAT_SNAPSHOT {
        Guid TemplateId
        int TemplateVersion
        FormatDefinition Definition "eingefrorene Kopie"
    }

    TOURNAMENT_COURT {
        Guid Id PK
        Guid TournamentId FK
        string Name "eindeutig im Turnier"
        CourtSurface Surface "Clay / Hard / Carpet / Grass / Artificial"
        CourtLocation Location "Outdoor / Indoor"
        bool IsCenterCourt "weiche Regel fuer Finalspiele"
        bool IsActive "stillgelegt bleibt lesbar"
    }

    COURT_WINDOW {
        Guid Id PK
        Guid TournamentId FK
        Guid CourtId FK
        TimeSlot Period "das Fenster IST die Verfuegbarkeit"
    }

    TOURNAMENT_ENTRY {
        Guid Id PK
        Guid TournamentId FK
        Guid ParticipantId FK
        int Seed "leer = ungesetzt"
        EntryStatus Status "Applied / Accepted / WaitingList / Withdrawn"
        EntryOrigin Origin "Organiser / SelfService"
        DateTimeOffset RegisteredAt
        string ConfirmationCode "48 Bit, Rueckweg ohne Konto"
    }

    PARTICIPANT {
        Guid Id PK
        string DisplayName "beim Melden festgeschrieben"
        bool IsTeam
    }

    PLAYER {
        Guid Id PK
        string FirstName
        string LastName
        string DisplayName "abgeleitet: Nachname, Vorname"
    }

    PLAYER_CONTACT {
        string Email
        string Phone
        DateOnly DateOfBirth
    }

    PHASE {
        Guid Id PK
        Guid TournamentId FK
        int Ordinal "beginnt bei 1"
        PhaseFormatKind Format "Knockout / RoundRobin / Swiss"
        string Name
        PhaseStatus Status "abgeleitet aus den Matches"
    }

    MATCH {
        Guid Id PK
        Guid TournamentId FK
        Guid PhaseId FK
        int Round
        int Position
        string Label "Finale, Spiel um Platz 3"
        string Group "nur in Gruppenphasen"
        MatchStatus Status "abgeleitet: Pending / Ready / Finished"
        int Version
    }

    MATCH_SIDE {
        ParticipantRef Origin "Entry / WinnerOf / LoserOf / GroupPosition / Bye / Unassigned"
        Guid EntryId "leer, solange nicht aufgeloest"
    }

    SCORE {
        MatchOutcome Outcome "Normal / Retirement / Walkover / Disqualification / Bye"
        int WinnerSide "1 oder 2"
        SetScore AbandonedSet "getrennt, denn niemand hat ihn gewonnen"
    }

    SET_SCORE {
        int Games1
        int Games2
        int TiebreakPoints "Stand des Unterlegenen"
    }

    COURT_ASSIGNMENT {
        Guid Id PK
        Guid TournamentId FK
        Guid MatchId FK
        Guid CourtId FK
        int SequenceOnCourt "lueckenlos ab 1"
        DateTimeOffset PlannedStart "Schaetzung, nur im Planungsmodus"
        DateTimeOffset EarliestStart "Zusage, nur am Turniertag"
        TimeSpan EstimatedDuration
        DateTimeOffset ActualStart
        DateTimeOffset ActualEnd
        AssignmentSource Source "Auto / Manual / Pinned"
        AssignmentStatus Status "Planned / Called / Running / Finished / Suspended"
        int Version
    }

    TOURNAMENT_PROJECTION {
        Guid Id PK "gleich der TournamentId"
        string Json "nur Oeffentliches, ADR-0003"
        string ETag "SHA-256 ueber den Inhalt"
        DateTimeOffset UpdatedAt
        int Version "zaehlt Aenderungen, nicht Neuaufbauten"
    }

    TOURNAMENT ||--|| VENUE : "(VO) eingebettet"
    TOURNAMENT ||--|| REGISTRATION_LINK : "(VO) entsteht mit dem Turnier"
    TOURNAMENT ||--o| FORMAT_SNAPSHOT : "(VO) eingefroren beim Auslosen"
    TOURNAMENT ||--o{ TOURNAMENT_COURT : "stellt"
    TOURNAMENT_COURT ||--o{ COURT_WINDOW : "steht zur Verfuegung"
    TOURNAMENT ||--o{ TOURNAMENT_ENTRY : "nimmt an"
    TOURNAMENT_ENTRY }o--|| PARTICIPANT : "meldet"
    PARTICIPANT }o--|{ PLAYER : "1 im Einzel, 2 im Doppel"
    PLAYER ||--|| PLAYER_CONTACT : "(VO) nie oeffentlich"
    TOURNAMENT ||--o{ PHASE : "gliedert sich in"
    PHASE ||--o{ MATCH : "enthaelt"
    MATCH ||--|{ MATCH_SIDE : "(VO) Side1 und Side2"
    MATCH_SIDE }o--o| TOURNAMENT_ENTRY : "aufgeloest zu"
    MATCH ||--o| SCORE : "(VO) sobald entschieden"
    SCORE ||--o{ SET_SCORE : "(VO) CompletedSets"
    MATCH ||--o{ COURT_ASSIGNMENT : "wird angesetzt auf"
    COURT_ASSIGNMENT }o--|| TOURNAMENT_COURT : "belegt"
    TOURNAMENT ||--o| TOURNAMENT_PROJECTION : "Lesestand, 1:1"
```

### Was an diesem Bild wichtig ist

**`MatchSide` trägt Herkunft und Auflösung getrennt.** `Origin` ist die Regel
(„Sieger aus Match 7"), `EntryId` ihr aktuelles Ergebnis. Würde die Auflösung die
Herkunft überschreiben, ließe sich ein Ergebnis nicht mehr zurücknehmen, ohne den
falschen Namen stehen zu lassen. `ParticipantRef` ist deshalb ein Summentyp und
das Fundament des ganzen Baums (ADR-0001) — „Zweiter der Gruppe B" und „Sieger
aus Match 7" sind derselbe Mechanismus.

**Ein Match kann mehrere Platzzuweisungen haben.** Unterbrochen und auf einem
anderen Platz fortgesetzt: erst beide Zuweisungen zusammen erzählen, was an
diesem Tag passiert ist (ADR-0002).

**Der abgebrochene Satz steht getrennt von den gespielten.** Läge er in derselben
Liste, zählte ein Stand von 2:1 als gewonnener Satz, und die Tabelle wiese einen
Satz aus, der nie zu Ende gespielt wurde.

**Teilnahme in drei Schichten.** `Player` ist die Person und existiert
turnierübergreifend, `Participant` die antretende Einheit (einer oder ein Paar),
`TournamentEntry` die Meldung mit Status und Setzposition. `Participant` von
Anfang an einzuziehen ist der Grund, warum das Doppel kein Sonderfall in
Setzliste, Spielplan und Ergebniseingabe ist.

---

## 2. Formate

Ein Turniermodus ist eine Phasenfolge, kein Code (ADR-0001). „Gruppenphase mit
anschließendem K.o." ist eine Komposition aus zwei `PhaseDefinition`.

```mermaid
erDiagram
    FORMAT_TEMPLATE {
        Guid Id PK
        Guid OwnerUserId FK "leer bei den mitgelieferten Vorlagen"
        int Version "zaehlt bei jeder Aenderung hoch"
        bool IsBuiltIn "abgeleitet: OwnerUserId ist leer"
    }

    FORMAT_DEFINITION {
        string Id
        string Name
    }

    PHASE_DEFINITION {
        int Ordinal "lueckenlos ab 1"
        PhaseFormatKind Format "Knockout / RoundRobin / Swiss"
        string Name
        int GroupCount "nur RoundRobin, hoechstens 26"
        int Encounters "Hin- oder Hin- und Rueckrunde"
        int Rounds "nur Swiss, leer = ceil(log2(n))"
        bool ThirdPlaceMatch "nur Knockout"
        Tiebreaker Tiebreakers "geordnet, Lot muss zuletzt stehen"
    }

    QUALIFICATION {
        int FromPhase "muss auf eine fruehere Phase zeigen"
        QualificationRule Rule "TopNPerGroup / BestThirds / All"
        int N
        SeedingRule Seeding "CrossGroup / ByRank"
    }

    MATCH_FORMAT {
        int BestOf "1, 3 oder 5"
        FinalSetMode FinalSetMode "Regular / MatchTiebreak10 / Advantage"
        int TiebreakAt "1 bis 12"
        int SetsToWin "abgeleitet"
    }

    SCORING_RULES {
        int Win
        int Loss
        int Walkover
    }

    TOURNAMENT {
        Guid Id PK
        Guid FormatTemplateId FK
    }

    FORMAT_SNAPSHOT {
        Guid TemplateId
        int TemplateVersion
    }

    USER_ACCOUNT {
        Guid Id PK
    }

    USER_ACCOUNT ||--o{ FORMAT_TEMPLATE : "besitzt"
    FORMAT_TEMPLATE ||--|| FORMAT_DEFINITION : "(VO)"
    FORMAT_DEFINITION ||--|{ PHASE_DEFINITION : "(VO) geordnete Folge"
    FORMAT_DEFINITION ||--|| MATCH_FORMAT : "(VO) turnierweit"
    PHASE_DEFINITION ||--o| MATCH_FORMAT : "(VO) ueberschreibt je Phase"
    PHASE_DEFINITION ||--o| QUALIFICATION : "(VO) ab der zweiten Phase Pflicht"
    PHASE_DEFINITION ||--|| SCORING_RULES : "(VO)"
    TOURNAMENT }o--|| FORMAT_TEMPLATE : "waehlt"
    TOURNAMENT ||--o| FORMAT_SNAPSHOT : "friert ein"
    FORMAT_SNAPSHOT ||--|| FORMAT_DEFINITION : "Kopie zum Zeitpunkt der Auslosung"
```

Der `FormatSnapshot` ist der Grund, warum eine geänderte Vorlage ein laufendes
Turnier nicht berührt. `TemplateVersion` hält fest, aus welchem Stand er stammt.

Die Formate selbst — `KnockoutFormat`, `RoundRobinFormat`, `SwissFormat` —
stehen hinter `IPhaseFormat` und sind zustandslos: sie bekommen einen
`PhaseState` und rechnen. Deshalb lässt sich jede Paarungslogik ohne Aggregat und
ohne Datenbank testen.

---

## 3. Benutzer und Rollen

```mermaid
erDiagram
    USER_ACCOUNT {
        Guid Id PK
        string Issuer "zusammen mit SubjectId eindeutig"
        string SubjectId "der sub aus dem Token"
        string Email
        string DisplayName
    }

    ROLE_ASSIGNMENT {
        Guid Id PK
        Guid UserId FK
        Role Role "SystemAdmin / Organizer / TournamentDirector / Referee"
    }

    RESOURCE_SCOPE {
        ScopeType Type "Global / Tournament"
        Guid ResourceId "leer genau dann, wenn Global"
    }

    TOURNAMENT {
        Guid Id PK
    }

    USER_ACCOUNT ||--o{ ROLE_ASSIGNMENT : "hat"
    ROLE_ASSIGNMENT ||--|| RESOURCE_SCOPE : "(VO) gilt in"
    RESOURCE_SCOPE }o--o| TOURNAMENT : "bei Type = Tournament"
```

Die Rechte-Matrix steht an einer einzigen Stelle (`Security/Permissions.cs`):

| Rolle | Scope | Rechte |
|---|---|---|
| `SystemAdmin` | Global | alle |
| `Organizer` | Global | `CreateTournament` |
| `TournamentDirector` | Tournament | `ManageTournament`, `EnterResults`, `ViewInternals`, `ViewMembers`, `WriteInFeed` |
| `Referee` | Tournament | `EnterResults`, `ViewMembers`, `WriteInFeed` |
| `Member` | Tournament | `ViewMembers`, `WriteInFeed` |

`Member` ist die Rolle, die ein Turnier zur Gruppe macht (ADR-0012). Ihre zwei
Rechte betreffen beide die Gruppe: sie sieht, wer dazugehört, und sie darf im
Feed schreiben (ADR-0014). Alles Weitere, was ein Mitglied sieht — Draw,
Spielplan, Ergebnisse —, kommt nicht aus dieser Matrix, sondern aus dem
Query-Filter, der an der Rollenzuweisung hängt.

`Organizer` ist global und trotzdem harmlos: sein einziges Recht beantwortet
„wer darf herein", nicht „wer sieht was". Wer ein Turnier anlegt, wird dessen
`TournamentDirector`. `RoleAssignment` weist im Konstruktor jeden Scope ab, der
nicht zur Rolle passt — eine globale Rolle lässt sich am Turnier nicht vergeben.

---

## 4. Der Lebenszyklus eines Turniers

Kein ER, aber ohne ihn ist die Hälfte der Regeln oben unverständlich.

```mermaid
stateDiagram-v2
    [*] --> Draft
    Draft --> RegistrationOpen : OpenRegistration
    RegistrationOpen --> RegistrationClosed : CloseRegistration
    RegistrationClosed --> RegistrationOpen : ReopenRegistration
    RegistrationClosed --> DrawGenerated : GenerateDraw
    DrawGenerated --> RegistrationOpen : ReopenRegistration (verwirft den Draw)
    DrawGenerated --> InProgress : Start
    InProgress --> Completed : Complete
    Completed --> InProgress : Resume (kein Endpunkt)
    Draft --> Abandoned : Abandon
    RegistrationOpen --> Abandoned : Abandon
    RegistrationClosed --> Abandoned : Abandon
    DrawGenerated --> Abandoned : Abandon
    InProgress --> Abandoned : Abandon
    Completed --> [*]
    Abandoned --> [*]
```

Ab `DrawGenerated` gilt `IsFrozen`: Teilnehmerfeld und Format sind eingefroren,
eine Nachmeldung verlangt den ausdrücklichen Rückschritt. Der Weg aus
`RegistrationClosed` zurück kostet nichts und muss es geben — sonst wäre ein zu
früh geschlossenes Turnier mit weniger als zwei Meldungen eine Sackgasse.

`Resume` hat bewusst keinen Endpunkt. Er folgt aus der Rücknahme eines
Ergebnisses, so wie `Complete` aus dem letzten Ergebnis folgt.

`SchedulingMode` (`Planning` / `MatchDay`) läuft **orthogonal** zu diesem
Automaten: der Wechsel zum Turniertag setzt eine Auslosung voraus, ist aber kein
Zustandsübergang des Turniers. Im Planungsmodus ist eine Uhrzeit eine Schätzung,
am Turniertag eine Zusage (ADR-0002).

---

## 5. Das soziale Umfeld

Drei Bausteine stehen neben dem Turnierkern. Zwei davon gehören einem Turnier
und erben dessen Sichtbarkeit; der dritte gehört keinem und trägt seine eigene.

```mermaid
erDiagram
    TOURNAMENT {
        Guid Id PK
    }

    TOURNAMENT_POST {
        Guid Id PK "UUIDv7 — zeitgeordnet"
        Guid TournamentId FK
        PostKind Kind "Message / Joined / DrawGenerated / ResultRecorded / ScheduleConfirmed / StateChanged"
        Guid AuthorUserId "leer bei einem Ereignis"
        string Text "fertig geschrieben, nicht gerendert"
        Guid MatchId "worüber berichtet wird"
    }

    POST_COMMENT {
        Guid Id PK
        Guid PostId FK
        Guid AuthorUserId
        string Text
    }

    PLAY_DATE {
        Guid Id PK
        Guid HostUserId "kein Turnier — die Wurzel ist sie selbst"
        Discipline Discipline "bestimmt RequiredPlayers"
        string VenueName "freier Text, kein Platz"
        bool IsCancelled "das Einzige am Zustand, was gespeichert wird"
    }

    PLAY_DATE_INVITATION {
        Guid Id PK
        Guid PlayDateId FK
        Guid UserId "daran hängt die Sichtbarkeit"
        Guid PlayerId "daran hängt der Weg ins Profil"
        InvitationResponse Response "Pending / Accepted / Declined"
    }

    PLAYER {
        Guid Id PK
        Guid UserAccountId "die Brücke zwischen Spieler und Konto"
    }

    TOURNAMENT ||--o{ TOURNAMENT_POST : "hat einen Feed"
    TOURNAMENT_POST ||--o{ POST_COMMENT : "hat Antworten"
    PLAY_DATE ||--o{ PLAY_DATE_INVITATION : "lädt ein"
    PLAY_DATE_INVITATION }o--|| PLAYER : "zeigt auf"
```

### Was an diesem Bild wichtig ist

**`TournamentPost.Text` ist ein Text und kein Verweis.** Auch bei einem
Ereignis: „Anna Müller schlägt Lena Berger 6:4 6:2" wird beim Eintragen des
Ergebnisses geschrieben und danach nicht mehr angefasst. Ein Eintrag, der zur
Anzeigezeit aus dem Match gerendert würde, wäre normalisiert und immer aktuell —
und genau deshalb falsch, denn ein Feed ist ein Protokoll (ADR-0014). Eine
Korrektur schreibt eine zweite Zeile.

**Die Id ist eine UUIDv7.** Der Feed sortiert nach `CreatedAt`, und zwei
Einträge können denselben Zeitstempel tragen — ein Beitritt und die Meldung dazu
entstehen im selben Aufruf. Als Stichentscheid taugt eine zufällige Guid nicht;
eine v7 trägt ihre Entstehungszeit vorn und ordnet damit richtig, auch als Text,
in dem SQLite sie ablegt.

**`PlayDate` hat kein Turnier — und das ist die Entscheidung.** ADR-0009 sagt,
was die Wurzel *eines Turniers* ist, nicht, dass es nur eine Sorte Wurzel gäbe.
Sie kennt keine Phase, keinen Draw und kein Ergebnis. Deshalb greift auf ihr
auch der Query-Filter aus ADR-0004 nicht: sie trägt ihren eigenen, und er lautet
„der Gastgeber und die Eingeladenen, sonst niemand" (ADR-0015).

**Am Zustand einer Verabredung wird genau eines gespeichert: `IsCancelled`.**
`RequiredPlayers` folgt aus der Disziplin (Einzel zwei, sonst vier), `Committed`
aus den Zusagen samt Gastgeber, `IsConfirmed` aus beidem und „vorbei" aus der
Uhr. Drei Zustände zu pflegen hieße, drei Gelegenheiten zu haben, sie falsch zu
setzen.

**`Player.Profile` (VO)** trägt die zwei Angaben, die niemand berechnen kann —
einen Text über sich und den Heimatverein. Sie liegen bewusst neben
`PlayerContact` und nicht darin: Kontakt ist das, was nie nach außen darf,
Profil das, was ausdrücklich dafür geschrieben wurde (ADR-0013).

**Die Spielhistorie ist kein Aggregat.** Bilanz, Turniere und die letzten
Matches eines Spielers werden bei jedem Aufruf über den sichtbaren Ausschnitt
gerechnet — es gibt dafür keine Tabelle und keine Projektion. Der
Kontaktgraph fällt aus derselben Rechnung ab.

---

## Wo man weiterliest

| Frage | Datei |
|---|---|
| Warum Phasen und `ParticipantRef` | `docs/adr/0001-turnierformate-als-phasen.md` |
| Warum Warteschlange statt Zeitraster | `docs/adr/0002-scheduling-planungsraster-und-queue.md` |
| Warum die öffentliche Sicht ein eigener Lesestand ist | `docs/adr/0003-getrenntes-read-modell.md` |
| Warum Rollen einen Scope tragen | `docs/adr/0004-club-scoped-autorisierung.md` (Superseded) |
| Warum der Spieler keine Vereinsbindung hat | `docs/adr/0008-spielerstammdaten.md` (Superseded) |
| Warum der Zähler fachlich und nicht `rowversion` ist | `docs/adr/0006-sqlite-als-startdatenbank.md` |
| Warum der Verein weg ist | `docs/adr/0009-turnier-als-wurzelaggregat.md` |
| Wie der Meldeweg ohne Konto funktioniert | `docs/adr/0010-oeffentliche-selbstmeldung.md` (Superseded) |
| Warum ein Turnier eine Gruppe ist | `docs/adr/0012-mitgliedschaft-statt-selbstmeldung.md` |
| Warum ein Profil für zwei Betrachter verschieden aussieht | `docs/adr/0013-spielerprofil-und-verbindungen.md` |
| Warum ein Feed-Eintrag seinen Text mitbringt | `docs/adr/0014-turnierfeed.md` |
| Warum eine Verabredung kein Turnier mit einem Match ist | `docs/adr/0015-verabredungen.md` |
