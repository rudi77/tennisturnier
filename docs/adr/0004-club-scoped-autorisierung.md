# ADR-0004 — Autorisierung ist club-scoped

**Status:** Accepted

## Kontext

„Man kann als Administrator einen Tennisverein auswählen" — hier steckt eine
Mehrmandantenfähigkeit, die leicht übersehen wird.

## Entscheidung

Rollen werden **nicht global**, sondern an eine Ressource gebunden vergeben:

| Rolle | Scope | Rechte |
|---|---|---|
| `SystemAdmin` | global | Vereine anlegen, alles |
| `ClubAdmin` | Club | Plätze, Mitglieder, Turniere des Vereins |
| `TournamentDirector` | Tournament | Draw, Spielplan, Ergebnisse |
| `Referee` | Tournament | nur Ergebniseingabe |
| `Player` | self | Anmeldung, eigene Daten |
| `Anonymous` | — | öffentliche Ansicht |

Ein `ClubAdmin` von Verein A darf Verein B nicht sehen. Das muss als Query-Filter auf
Persistenzebene erzwungen werden (EF Core Global Query Filter auf `ClubId`), nicht nur
im Controller — sonst leckt die erste vergessene Endpoint-Prüfung fremde Daten.

Der Query-Filter ist die eigentliche Sicherheitsgrenze; die Endpunkt-Prüfung ist die
zweite Verteidigungslinie.

## Konsequenzen

Zugriff auf eine Ressource außerhalb des eigenen Scopes wird als **404** beantwortet,
nicht als 403 — ein 403 verrät, dass die Ressource existiert.

Der Filter benötigt den aufrufenden Benutzer zur Query-Zeit. Das bindet den
`DbContext` an einen Scoped-Kontext (`IUserContext`) und macht Hintergrundjobs zu
einem Sonderfall, der einen expliziten Systemkontext setzen muss.
