# ADR-0005 — Hexagonale Architektur mit erzwungenen Fitnessfunktionen

**Status:** Accepted

## Kontext

Der fachliche Kern dieser Anwendung — Paarungserzeugung, Tabellen, Tiebreaker,
Satzvalidierung, Platzverfügbarkeit — ist reine Logik ohne Infrastrukturbezug. Genau
diese Logik ist auch der Teil, der die meisten Tests braucht und sich am häufigsten
ändert. Wenn er im selben Projekt wie EF Core und ASP.NET Core liegt, kostet jeder
Test eine Datenbank.

## Entscheidung

Ports & Adapters mit vier Ringen:

```
TennisTurnier.Domain                        keine Projekt-, keine Paketreferenzen
TennisTurnier.Application                   → Domain (Ports + Anwendungsfälle)
TennisTurnier.Adapters.*                    → Application (EF Core, OIDC, Solver)
TennisTurnier.Api                           → alle (Composition Root)
```

**Driven Ports** (die Anwendung ruft nach außen): Repositories, `IUnitOfWork`,
`IClock`, `IUserContext`, `IScheduleSolver`, `ITournamentNotifier`, `IAuditLog`.

**Driving Ports** (Anwendungsfälle, von der API aufgerufen): ein Interface je
Anwendungsfall-Gruppe, z. B. `ITournamentLifecycleService`, `IDrawService`,
`IResultService`.

Die API-Schicht kennt ausschließlich Driving Ports und DTOs — nie `DbContext`, nie
EF-Typen, nie Domänenentitäten in Response-Bodies.

## Durchsetzung

Eine Architektur, die nicht getestet wird, erodiert. `TennisTurnier.Architecture.Tests`
prüft mit NetArchTest die Abhängigkeitsrichtung bei jedem Build.

Dazu kommen **Positivkontrollen** in `FitnessFunctionSelfTests`: NetArchTest meldet
Erfolg, wenn die geprüfte Typmenge leer ist. Ohne einen absichtlich verletzenden
Kanarienvogel-Typ wären die Regeln stillschweigend wirkungslos, sobald jemand einen
Namensraum umbenennt oder eine Assembly falsch verdrahtet.

## Konsequenzen

**Positiv.** Die gesamte Turnierlogik ist ohne Datenbank testbar. Der Wechsel der
Datenbank (siehe ADR-0006) ist ein neues Adapter-Projekt, kein Umbau.

**Negativ, ehrlich benannt.** Mehr Projekte, mehr Mapping zwischen Domänenmodell und
DTOs, und ein spürbarer Anteil an Interfaces mit genau einer Implementierung. Bei einer
Anwendung dieser Größe ist das vertretbar, weil der Domänenkern ungewöhnlich groß und
infrastrukturfrei ist — bei einer reinen CRUD-Anwendung wäre es Overhead ohne Ertrag.
