# ADR-0006 — SQLite als Startdatenbank, PostgreSQL als Zielbild

**Status:** Accepted

## Kontext

Das ursprüngliche Architekturdokument ließ PostgreSQL und Azure SQL offen. Für den
Start wurde SQLite gewählt: kein Container, keine Verbindungszeichenfolge, ein
`dotnet run` genügt. Dank ADR-0005 ist das eine Adapter-Entscheidung, kein
Architekturmerkmal.

## Entscheidung

`TennisTurnier.Adapters.Persistence.Sqlite` ist der erste Persistenzadapter. Der
Wechsel auf PostgreSQL erfolgt später durch ein zweites Adapter-Projekt.

## Der Preis, bewusst benannt

Der Wechsel bleibt nur billig, wenn diese Regeln von Anfang an eingehalten werden:

- **Kein SQLite-spezifisches SQL.** Keine rohen Abfragen mit `json_extract`,
  `strftime` o. ä.
- **Keine Abhängigkeit von SQLites laxer Typisierung.** SQLite akzeptiert einen String
  in einer INTEGER-Spalte; PostgreSQL nicht.
- **JSON-Spalten werden nur als Ganzes gelesen und geschrieben**, nie serverseitig
  durchsucht. Ein `WHERE format->>'id' = ...` würde den Wechsel zu einem Umbau machen.
- **`DateTimeOffset` wird als TEXT gespeichert.** Die Sortierung stimmt nur bei
  normalisiertem ISO-8601 in UTC — dafür sorgt ein ValueConverter, nicht die
  Aufrufstelle.
- **Kein `decimal`.** SQLite bildet es auf REAL ab und verliert Genauigkeit.
- **Nebenläufigkeit** wird über eine fachlich gepflegte `Version`-Spalte abgebildet,
  nicht über `rowversion`/`xmin`. Das funktioniert auf beiden Systemen identisch.

Diese Regeln sind der eigentliche Inhalt dieser Entscheidung. Ohne sie ist der
angebliche Vorteil der Austauschbarkeit nur behauptet.

## Konsequenzen

SQLite schreibt datenbankweit seriell. Für ein Vereinsturnier mit einer Handvoll
gleichzeitiger Schreiber ist das unkritisch; ab mehreren parallelen Turnieren mit
Live-Ergebniseingabe ist es der erste Grund, auf PostgreSQL zu wechseln.
