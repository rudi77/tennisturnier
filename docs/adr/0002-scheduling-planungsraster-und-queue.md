# ADR-0002 — Scheduling: Planungsraster und Turniertag-Queue

**Status:** Accepted

## Kontext

Spielplan „auto + manuell korrigierbar". Die harte Randbedingung bei Tennis:
**die Matchdauer ist unbekannt.** Ein Zweisatz-Match dauert 50 Minuten, ein
Dreisatz-Match mit Tiebreaks über zwei Stunden. Ein starres Zeitraster kippt beim
ersten langen Match, und danach ist der gesamte Aushang Fiktion.

## Entscheidung

**Zwei Betriebsmodi auf einem Datenmodell.**

*Planungsmodus* (vor dem Turnier): Zeitraster mit geschätzten Dauern, Solver erzeugt
einen Vorschlag. Zweck ist Machbarkeitsprüfung und Kommunikation („Samstag ab 9:00,
Sonntag Finaltag"), nicht Minutengenauigkeit.

*Turniertagmodus*: Umschaltung auf **Court-Queues**. Jeder Platz hat eine geordnete
Warteschlange; ein Match hat statt einer Startzeit eine `EarliestStart`-Zusage
(„nicht vor 14:00"). Fertig gemeldetes Match → nächstes aus der Queue wird aufgerufen.
Das ist exakt, wie reale Turnierleitungen arbeiten, und es degradiert nicht bei Verzug.

```csharp
public sealed class CourtAssignment
{
    public Guid Id { get; init; }
    public Guid MatchId { get; init; }
    public Guid CourtId { get; init; }

    public int SequenceOnCourt { get; set; }            // Queue-Position
    public DateTimeOffset? EarliestStart { get; set; }
    public DateTimeOffset? PlannedStart { get; set; }   // nur Planungsmodus
    public TimeSpan EstimatedDuration { get; set; }

    public DateTimeOffset? ActualStart { get; set; }
    public DateTimeOffset? ActualEnd { get; set; }

    public AssignmentSource Source { get; set; }        // Auto | Manual | Pinned
    public AssignmentStatus Status { get; set; }        // Planned | Called | Running
                                                        // | Finished | Suspended
}
```

`CourtAssignment` ist bewusst eine **eigene Entität**, kein Feldpaar am Match. Ein
Match kann mehrfach zugewiesen, verschoben, unterbrochen und auf einen anderen Platz
umgesetzt werden. Historie ist am Turniertag Gold wert.

## Stabilität beim Re-Solve — der eigentliche Knackpunkt

Wenn die Turnierleitung ein Match manuell umsetzt und der Solver danach neu läuft,
darf er **nicht den halben Plan neu würfeln**. Das ist der häufigste Grund, warum
Auto-Scheduling in der Praxis abgeschaltet wird.

- `Manual` und `Pinned` gehen als **harte Constraints** in den nächsten Lauf.
- Die Zielfunktion enthält einen **Stabilitätsterm**: Anzahl geänderter Zuweisungen
  gegenüber dem Vorzustand wird bestraft.
- Jeder Solver-Lauf erzeugt einen **Vorschlag mit Diff**, der bestätigt werden muss.
  Kein stilles Überschreiben.

## Constraints

**Hart**
- Ein Spieler kann nicht zwei Matches gleichzeitig spielen.
- Mindestpause zwischen zwei Matches desselben Spielers (konfigurierbar, Default
  30 min; bei Doppel/Mixed relevanter, als man denkt).
- Abhängigkeitskette: Match B mit `WinnerOf(A)` startet nach Ende von A.
- Platzverfügbarkeit (`CourtAvailability`, inkl. Sperren).
- Phasenreihenfolge: keine K.O.-Partie vor Abschluss der Gruppenphase.

**Weich**
- Leerlauf auf Plätzen minimieren.
- Auslastung über Plätze balancieren.
- Matches derselben Runde zeitlich bündeln (Fairness bei Ruhezeiten).
- Verfügbarkeitswünsche der Spieler.
- Finalspiele auf den Center Court.

## Solver-Wahl

Für ein Vereinsturnier (≤ 128 Teilnehmer, ≤ 12 Plätze) ist **List-Scheduling mit
Prioritäten nach kritischer Pfadtiefe plus lokale Suche** ausreichend und vor allem
*erklärbar* — die Turnierleitung will wissen, warum ein Match dort liegt, wo es liegt.

CP-SAT (OR-Tools) ist die formal saubere Antwort, aber ein schwerer Einstieg und
schlecht debugbar, wenn die Lösung unbefriedigend aussieht. Daher: hinter
`IScheduleSolver` kapseln, heuristisch starten, austauschbar halten.

```csharp
public interface IScheduleSolver
{
    ScheduleProposal Solve(SchedulingProblem problem, Schedule? current);
}
```

## Konsequenzen

Das Umschalten Planung → Turniertag muss ein expliziter Zustandsübergang sein, sonst
entsteht Verwirrung darüber, ob eine angezeigte Uhrzeit eine Zusage oder eine Schätzung
ist. In der API-Antwort und in jeder UI müssen beide sichtbar unterschiedlich
dargestellt werden.
