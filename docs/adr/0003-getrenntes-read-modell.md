# ADR-0003 — Getrenntes Read-Modell für die öffentliche Ansicht

**Status:** Accepted

## Kontext

Öffentliche Live-Ansicht bedeutet: während die Turnierleitung schreibt, pollen im
Zweifel einige hundert Zuschauer und Spieler dasselbe Bracket vom Handy. Lastprofil
und Zugriffsmuster sind gegenläufig zum Schreibpfad.

## Entscheidung

**Materialisierte Projektion, kein volles CQRS/Event-Sourcing.**

- Pro Turnier eine denormalisierte `TournamentView`-Projektion (Bracket, Tabellen,
  aktuelle Platzbelegung) als JSON-Dokument.
- Invalidierung bei Ergebniseingabe, Draw-Änderung, Zuweisungsänderung.
- Auslieferung mit ETag/`Cache-Control`, optional über CDN.
- Live-Push via SignalR für offene Ansichten; Polling als Fallback.
- Öffentlicher Endpunkt ohne Auth, aber **datensparsam**: keine Kontaktdaten, keine
  Geburtsdaten, keine internen Notizen.

Der Projektions-Mapper ist die einzige Stelle, an der die Datensparsamkeit
durchgesetzt wird. Deshalb gehört dazu ein Test, der die serialisierte Projektion
gegen eine Verbotsliste von Feldnamen prüft — sonst rutscht das erste zusätzliche Feld
unbemerkt in die Öffentlichkeit.

## Verworfen: Event Sourcing

Wäre für Ergebnishistorie und Streitfälle reizvoll, aber die Ergebnismenge ist klein
und ein Audit-Log auf `Match` und `CourtAssignment` erfüllt denselben Zweck bei einem
Bruchteil des Aufwands.

## Konsequenzen

Die Projektion ist abgeleiteter Zustand und muss jederzeit aus den Quelldaten neu
aufbaubar sein — ein Rebuild-Kommando gehört dazu, sonst ist ein Bug im Mapper nur
durch Datenreparatur zu beheben.
