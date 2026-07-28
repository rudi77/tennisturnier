# tennisturnier

Turnierplattform für Tennisvereine: Platzverwaltung, Turniere in verschiedenen Modi
(K.O., Gruppenphase + K.O., Liga, Schweizer System), automatischer und manuell
korrigierbarer Spielplan sowie eine öffentliche Live-Ansicht.

**Status:** im Aufbau. Der Fahrplan steht in [docs/roadmap.md](docs/roadmap.md).

## Schnellstart

```bash
dotnet restore
dotnet build
dotnet test
```

Voraussetzung ist das .NET 10 SDK. In Claude-Code-Web-Sessions erledigt der
SessionStart-Hook (`.claude/hooks/setup.sh`) Installation und Restore automatisch.

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
