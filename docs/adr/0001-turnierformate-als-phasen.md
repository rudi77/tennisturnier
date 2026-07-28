# ADR-0001 — Turnierformate als komponierbare Phasen

**Status:** Accepted

## Kontext

Gefordert sind K.O., Gruppenphase + K.O., Round Robin/Liga und Schweizer System,
plus der Wunsch, „zur Laufzeit einen custom Turniermodus anzulegen".

## Betrachtete Optionen

**A — Enum + Verzweigung pro Format.**
Ein `TournamentType`-Enum, dahinter Spezialfälle. Gruppenphase + K.O. wird zum
Sonderfall mit eigenem Code-Pfad. Jeder neue Modus multipliziert die Verzweigungen.
Verworfen.

**B — Plugin-System mit Code zur Laufzeit** (Roslyn-Scripting, Lua, WASM).
Erfüllt den Wunsch wörtlich, bringt aber Sandboxing, Ressourcenlimits,
Plugin-Versionierung und vor allem die Frage mit: was passiert mit einem laufenden
Turnier, wenn jemand die Paarungsregel ändert? Für eine Vereins-App ist das ein
Vielfaches des Aufwands der eigentlichen Anwendung. Verworfen.

**C — Deklarative Komposition aus Phasen.** Gewählt.

## Entscheidung

Ein Turnier ist eine **geordnete Sequenz von Phasen**. „Gruppenphase + K.O." ist kein
eigenes Format, sondern eine Komposition:

```json
{
  "id": "group-then-ko",
  "version": 3,
  "name": "Gruppenphase mit anschließendem K.O.",
  "phases": [
    {
      "ordinal": 1,
      "format": "RoundRobin",
      "parameters": { "groupCount": 4, "encounters": 1 },
      "scoring": { "win": 2, "loss": 0, "walkover": 0 },
      "tiebreakers": ["DirectEncounter", "SetRatio", "GameRatio", "Lot"]
    },
    {
      "ordinal": 2,
      "format": "Knockout",
      "qualification": { "from": 1, "rule": "TopNPerGroup", "n": 2, "seeding": "CrossGroup" },
      "parameters": { "thirdPlaceMatch": true }
    }
  ],
  "matchFormat": { "bestOf": 3, "finalSetMode": "MatchTiebreak10", "tiebreakAt": 6 }
}
```

Custom-Modus = neue Phasenfolge + Parameter. Deklarativ, versioniert, diff-bar,
testbar, ohne Deployment. Ein Turnier referenziert eine **eingefrorene Version** der
Definition — Änderungen an der Vorlage berühren laufende Turniere nicht.

## Die tragende Abstraktion

```csharp
public interface IPhaseFormat
{
    /// Erzeugt die nächste Menge an Paarungen. RoundRobin liefert alle auf einmal,
    /// Schweizer System genau eine Runde, Knockout füllt Positionen der nächsten
    /// Runde, sobald die Vorgänger entschieden sind.
    IReadOnlyList<Pairing> GeneratePairings(PhaseState state);

    bool IsComplete(PhaseState state);

    Standings ComputeStandings(PhaseState state);
}
```

Vier Implementierungen: `RoundRobinFormat`, `KnockoutFormat`, `SwissFormat`,
`SingleEliminationConsolationFormat` (falls Trostrunde gewünscht).

Alles Weitere — Satzformat, Tiebreak-Regeln, Punktesystem, Anzahl Gruppen,
Qualifikantenzahl — ist **Parameter, nicht Vererbung**. Sobald jemand
`RoundRobinMitSonderregel : RoundRobinFormat` schreibt, wurde ein Parameter übersehen.

## ParticipantRef

```csharp
public abstract record ParticipantRef
{
    public sealed record Entry(Guid EntryId) : ParticipantRef;
    public sealed record WinnerOf(Guid MatchId) : ParticipantRef;
    public sealed record LoserOf(Guid MatchId) : ParticipantRef;
    public sealed record GroupPosition(Guid PhaseId, string Group, int Rank) : ParticipantRef;
    public sealed record Bye : ParticipantRef;
    public sealed record Unassigned : ParticipantRef;
}
```

Damit ist der Übergang Gruppenphase → K.O. derselbe Mechanismus wie der Übergang
Viertelfinale → Halbfinale: eine Referenz wird aufgelöst, sobald ihr Vorgänger
entschieden ist. Kein Sonderfall.

## Konsequenzen

**Positiv.** Neue Formate durch Komposition ohne Deployment. Qualifikationsregeln sind
einheitlich. Der Draw kann vollständig aufgebaut werden, bevor ein einziger Teilnehmer
feststeht — Grundlage für die öffentliche Vorschau.

**Negativ, ehrlich benannt.** Ein *genuin neuer Paarungsalgorithmus* (z. B. eine
exotische Setzlisten-Logik, die keiner der vier Formate abbildet) braucht weiterhin
eine neue `IPhaseFormat`-Implementierung und ein Deployment. Der Wunsch „beliebiger
Modus zur Laufzeit" wird zu ~90 % erfüllt, nicht zu 100 %. Das ist der Preis dafür,
kein Plugin-System zu betreiben — und der richtige Handel.

**Offen.** Schweizer System braucht eine Paarungsstrategie (Dutch-System o. ä.) und
eine Regel gegen Wiederholungspaarungen. Das ist die aufwendigste der vier
Implementierungen und kommt daher zuletzt.
