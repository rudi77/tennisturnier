# ADR-0008 — Spieler existieren vereinsübergreifend

**Status:** Accepted

## Kontext

Der Fahrplan ließ offen, ob ein Spieler je Verein oder vereinsübergreifend
existiert. Die Frage wird in M2 entscheidend, weil `TournamentEntry` auf einen
Teilnehmer zeigen muss.

Sie sieht nach einer Detailfrage aus, ist aber die Weichenstellung für die
Mehrmandantenfähigkeit aus ADR-0004: Ein vereinseigener Spieler wäre sauber
abgeschottet, aber ein Gastspieler aus dem Nachbarverein — der Normalfall bei
jedem offenen Turnier — wäre dann nicht abbildbar, ohne ihn zu duplizieren.

## Betrachtete Optionen

**A — Spieler gehört einem Verein.** Passt perfekt zum Query-Filter, macht aber
jedes offene Turnier zum Sonderfall. Ein Gastspieler entstünde als Kopie je
Verein, und damit wäre jede Frage nach seiner Turnierhistorie unbeantwortbar.
Verworfen.

**B — Spieler existiert global, Vereinszugehörigkeit ist eine Beziehung.**
Gewählt.

## Entscheidung

`Player` ist eine eigenständige Entität ohne `ClubId`. Die Zugehörigkeit zu einem
Verein ist eine eigene Beziehung (`ClubMembership`), von der es null, eine oder
mehrere geben kann.

Damit ist ein Gastspieler kein Sonderfall, sondern schlicht ein Spieler ohne
Mitgliedschaft im ausrichtenden Verein.

## Konsequenzen — der Preis

Der Query-Filter aus ADR-0004 greift bei `Player` nicht, weil es keine `ClubId`
gibt. Der Schutz personenbezogener Daten muss deshalb an anderer Stelle
entstehen:

- Die Suche nach Spielern liefert nur Name und Verein, nie Kontaktdaten.
- Kontaktdaten sieht, wer `ViewInternals` im Verein einer Mitgliedschaft des
  Spielers hat oder in einem Turnier, für das er gemeldet ist.
- Die öffentliche Projektion (ADR-0003) enthält von einem Spieler ausschließlich
  Anzeigename und Verein.

Diese drei Regeln ersetzen den Filter und müssen getestet sein — sonst ist der
Preis für die Flexibilität ein Datenleck statt eines Kompromisses.
