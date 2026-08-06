# Übergabe — Stand 6. August 2026

Branch `feature/erster-turnierablauf`, HEAD = `f355bae`, Arbeitsverzeichnis
sauber, **682 Tests grün**, Frontend baut.

Der Plan
`~/.claude/plans/ich-finde-dass-matchday-vivid-pancake.md` ist **vollständig
abgearbeitet** — alle fünf Stufen. Was danach kam, waren vier Fehler aus dem
Durchklicken.

---

## Was passiert ist

Der Verein ist als Aggregat abgeschafft. Das Turnier ist die Wurzel und trägt
Ort, Disziplin, Plätze, Platzzeiten und den Anmeldelink selbst. Wer sich
anmeldet, darf ausschreiben; wer ausschreibt, führt sein Turnier. Melden kann
sich jeder über einen Link, ohne Konto.

### Commits dieser Sitzung

| Commit | Was |
|---|---|
| `6045e1d` | **Stufe 3** — Tests der Produktivumstellung nachgezogen, Frontend umgebaut |
| `5654ab3` | **Stufe 4** — öffentliche Selbstmeldung |
| `e0fbd9d` | **Stufe 5** — Rollenverwaltung, ADRs, Roadmap |
| `1e5654b` | Fix: Meldeschluss war eine Sackgasse |
| `74e81ea` | Fix: Ergebnismaske kannte das Satzformat nicht |
| `f355bae` | Fix: letztes Ergebnis schließt das Turnier ab |

Davor (aus der vorigen Sitzung, ebenfalls auf diesem Branch): `cbc45b5`
Stufe 1, `24c3a7b` Stufe 2, `81adbe7` Stufe 0.

---

## Stufe 3 — Turnier als Wurzel (`6045e1d`)

Der Produktivcode war beim Übernehmen schon umgestellt, Tests und Frontend
nicht. Beides nachgezogen.

**Backend, ergänzt:**

- `Tournament.RequireMatchesDiscipline(bool hasPartner)` — die Prüfung, die es
  laut Plan geben sollte. Aufgerufen in `TournamentService.EnterAsync`, wo
  Ausschreibung und Teilnehmer zum ersten Mal aufeinandertreffen.
- `PublicTournamentView.ClubName` → `VenueName`.

**Frontend, umgebaut:** `ClubScreen` gelöscht, `TournamentsScreen` als neuer
Einstieg, `WizardScreen` beginnt mit Eckdaten (Ort/Zeitzone/Disziplin) und legt
im Schritt „Plätze" zum ersten Mal echte Plätze samt Zeiten an — über die
Massenanlage. Im Gantt-Board stehen statt Sperren die Lücken zwischen den
gebuchten Fenstern.

**Abweichung vom Plan (aus der vorigen Sitzung übernommen und begründet):**
`RequireCourtTimesRecorded()` prüft nur beim Spielplanvorschlag, nicht beim
Auslosen. Steht in ADR-0009.

---

## Stufe 4 — Selbstmeldung (`5654ab3`)

`Application/Registration/` mit `RegistrationService`,
`Api/Endpoints/RegistrationEndpoints.cs`, Ratenbegrenzung in `Program.cs`.

`TournamentEntry` bekam `Origin` (`Organiser | SelfService`), `RegisteredAt` und
`ConfirmationCode`. Eigene Migration `Selbstmeldung`.

**Die drei Regeln, ohne die der Endpunkt still scheitert** — stehen im
Klassenkommentar von `RegistrationService` und in ADR-0010:

1. Turnier **ausschließlich** über `FindByRegistrationTokenAsync` (das einzige
   `IgnoreQueryFilters` auf Turnieren).
2. Kein Neuaufbau der öffentlichen Projektion.
3. Kein `FlushAsync` — genau ein `SaveChangesAsync`.

**Abweichung vom Plan:** Ein fehlender Partner liefert **422**, nicht den
einheitlichen 404. Er liegt am Formular und nicht am Link; verdeckt wird nichts,
was der `GET` auf dasselbe Token nicht ohnehin sagt. Token, Zustand und
Meldeschluss liefern weiterhin denselben 404.

**Abweichung vom Plan:** Die Ratenbegrenzung ist über
`Security:PublicRegistrationRequestsPerWindow` übersteuerbar (Vorgabe 20/10 min).
Ohne den Schalter prüfte die Api-Testbaugruppe nur noch die Schranke; sie wird
jetzt in `AnmeldungRatenbegrenzungTests` mit eigener Fabrik geprüft.

**Frontend:** `RegistrationScreen` (öffentlich, vor der Anmeldemaske),
`EntriesScreen` (Meldungsverwaltung), `hooks/useRoute.ts` statt eines Routers.

---

## Stufe 5 — Rollen und Aufräumen (`e0fbd9d`)

`Application/Security/RoleService.cs` + `Api/Endpoints/RoleEndpoints.cs` + Panel
im `EntriesScreen`. Zwei Sperren, beide getestet:

- Eine **globale Rolle** lässt sich am Turnier nicht vergeben (Eskalationssperre).
- Die **letzte Turnierleitung** ist nicht entziehbar (sonst herrenloses Turnier).

Berufen wird über die E-Mail-Adresse eines **bestehenden** Kontos.

**Dokumentation:** ADR-0009 (Turnier als Wurzel) und ADR-0010 (Selbstmeldung)
neu als `Accepted`. ADR-0004 und ADR-0008 auf `Superseded` mit einem Vermerk,
was von ihnen weiter gilt. M9/M10 in der Roadmap, drei neue offene Punkte
benannt.

---

## Die vier Fehler aus dem Durchklicken

| Fehler | Ursache | Commit |
|---|---|---|
| „Meldung schließen" gesperrt | Oberfläche stellte dieselbe Bedingung wie das Auslosen | `1e5654b` |
| Meldeschluss war endgültig | `ReopenRegistration` setzte einen Draw voraus — aus `RegistrationClosed` gab es keinen Weg zurück | `1e5654b` |
| Einzel/Doppel frei wählbar | Rest aus der Zeit vor der Disziplin am Turnier; stand auf „Einzel" bei einem Doppelturnier | `1e5654b` |
| Ergebnismaske bot immer 3 Sätze | `SET_COUNT = 3` fest verdrahtet, Satzformat unbekannt; Stepper ging nur bis 9 | `74e81ea` |
| Turnier blieb auf „läuft" | Niemand rief je `tournament.Complete()` auf | `f355bae` |

Zum letzten: `Tournament.Resume()` ist der Gegenzug und hat **bewusst keinen
Endpunkt** — er folgt aus der Rücknahme eines Ergebnisses. Gefragt wird das
Phasenformat (`IPhaseFormat.IsComplete`) und nicht der abgeleitete
`Phase.Status`; im Schweizer System wäre der nach jeder Runde „Completed".

---

## Zustand der Umgebung

- **Datenbank ist zurückgesetzt.** `src/TennisTurnier.Api/tennisturnier.db`
  wurde gelöscht und neu erzeugt: 4 mitgelieferte Formatvorlagen, 2 Migrationen,
  sonst leer. Kein Benutzerkonto — beim nächsten Anmelden entsteht es neu, samt
  Rolle `Organizer`.
- **Keine API läuft.** Sie muss von Hand gestartet werden.
- Eine laufende `dotnet run`-Instanz **sperrt die Build-Ausgabe** von
  `TennisTurnier.Api`. Wer bauen will, muss sie vorher beenden — das ist während
  dieser Sitzung dreimal aufgetreten.

```bash
docker compose up -d keycloak
dotnet run --project src/TennisTurnier.Api
cd app && npm run dev          # Port 5000, nicht verhandelbar
```

---

## Was offen ist

### Nicht gemacht, weil es dafür einen Browser und Keycloak braucht

Der Handdurchlauf aus dem Plan: anmelden → Turnier anlegen → Anmeldelink im
privaten Fenster → zwei Doppel melden → annehmen → auslosen → Spielplan →
Turniertag → Ergebnis → Live-Ansicht ohne Neuladen. Die API selbst wurde gegen
eine frische Datenbank geprüft (beide Migrationen, `Referrer-Policy`, 404 auf
unbekannte Token, `[]` für Anonyme).

### Benannte Lücken (stehen in `docs/roadmap.md`)

- **Keine E-Mail-Verifikation** bei der Selbstmeldung.
- **Keine Aufbewahrungsfrist** für Kontaktdaten. `Origin` und `RegisteredAt`
  sind die Felder, an denen eine Löschregel ansetzen wird.
- **Einladung noch nicht angemeldeter Benutzer** gibt es nicht.

### Was auffiel, aber nicht angefasst wurde

- **`DrawScreen` und `EntriesScreen` überschneiden sich.** Beide verwalten
  Meldungen. Der `DrawScreen` sollte vermutlich nur noch anzeigen und fürs
  Verwalten nach drüben verweisen. Gestaltungsfrage, keine kaputte Stelle.
- **`app/` hat keinen Testrunner.** Seit `74e81ea` liegen die Satzformatregeln
  ein zweites Mal in `app/src/lib/matchFormat.ts` — bewusst (eine Maske, die
  ungültige Züge anbietet, ist eine Sackgasse), aber ungeprüft. Vitest für genau
  diese Datei wäre ein kleiner, lohnender Schritt.
- **Flake unter Last.** In einem von drei vollen Läufen fielen
  `PublicViewConsistencyTests.Ein_Konflikt_hinterlaesst_kein_gespeichertes_Ergebnis`
  und `…Gleichzeitige_Ergebnisse_gehen_der_oeffentlichen_Ansicht_nicht_verloren`
  aus; einzeln und in allen anderen Läufen grün. Sie prüfen gleichzeitige
  Schreibzugriffe, und SQLite serialisiert datenbankweit. Bestand schon vorher.
- **Der Branch ist nicht gemergt.** Acht Commits stehen vor `main`.

---

## Wo man nachliest

| Frage | Datei |
|---|---|
| Warum der Verein weg ist | `docs/adr/0009-turnier-als-wurzelaggregat.md` |
| Wie der Meldeweg funktioniert | `docs/adr/0010-oeffentliche-selbstmeldung.md` |
| Was von ADR-0004/0008 noch gilt | Vermerk oben in beiden Dateien |
| Offene Punkte, Milestones | `docs/roadmap.md` |
| Wie ein Test ein Turnier aufbaut | `tests/TennisTurnier.Api.Tests/TurnierAufbau.cs` |
| Der ganze Weg am Stück | `tests/TennisTurnier.Api.Tests/KompletterAblaufApiTests.cs` |
