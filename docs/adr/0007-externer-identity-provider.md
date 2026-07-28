# ADR-0007 — Externer Identity Provider, Rollen bleiben in der Anwendung

**Status:** Accepted

## Kontext

Die Anmeldung soll über einen externen Identity Provider laufen: Keycloak lokal,
Entra ID in Produktion. Damit stellt sich die Frage, wo die Rollen aus ADR-0004
gepflegt werden.

## Entscheidung

**Der IdP liefert ausschließlich Identität** — `sub`, E-Mail, Anzeigename. Die
Zuordnung „dieser Benutzer ist ClubAdmin von Verein X" liegt in der Anwendung, in der
Tabelle `RoleAssignment(UserId, Role, ScopeType, ScopeId)`.

Begründung: Die Rollen aus ADR-0004 sind ressourcengebunden. Ein IdP-Claim wie
`role=ClubAdmin` trägt den Scope nicht; man müsste ihn in den Claim-Wert kodieren
(`ClubAdmin:<guid>`) und bei jeder Vereinsgründung im IdP nachpflegen. Damit läge ein
Teil des Domänenmodells in einem System, das die Domäne nicht kennt.

Technisch: JWT-Bearer-Validierung gegen die konfigurierte Authority, Authority und
Audience aus der Konfiguration, damit derselbe Code Keycloak und Entra ID bedient.
Ein `IUserContext`-Adapter löst `sub` einmal pro Request auf interne `UserId` plus
geladene Rollenzuweisungen auf.

## Konsequenzen

**Positiv.** Kein Passwort-Hashing, keine Konto-Wiederherstellung, kein
Zweifaktor-Aufbau in dieser Anwendung. Rollenvergabe bleibt eine fachliche Operation
mit Audit-Spur.

**Negativ.** Ein neuer Benutzer existiert erst nach seiner ersten Anmeldung in der
lokalen Tabelle. Eine Einladung „mach Person X zum ClubAdmin", bevor sie sich je
angemeldet hat, braucht deshalb eine Vorab-Zuweisung per E-Mail-Adresse, die beim
ersten Login eingelöst wird. Das ist bewusst noch nicht gebaut und beim ersten Bedarf
nachzuziehen.
