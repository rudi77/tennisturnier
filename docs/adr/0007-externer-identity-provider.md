# ADR-0007 — Externer Identity Provider, Rollen bleiben in der Anwendung

**Status:** Accepted

> **Nachtrag zum Aussteller.** „Entra ID in Produktion" unten ist die Annahme,
> unter der diese Entscheidung getroffen wurde, und nicht mehr das, was
> ausgeliefert wird: `deploy/keycloak/` stellt Keycloak als zweiten Dienst
> hin, mit Google als Anmeldeweg dahinter — der Weg, den die README unter
> *Betrieb* beschreibt. An der Entscheidung ändert das nichts, im Gegenteil:
> dass der Aussteller austauschbar ist, war ihr Kern. Auch die Rollen liegen
> weiterhin in der Anwendung. Ausgetauscht wurde nur der Name im Beispiel.
>
> Zwei Dinge aus diesem Abschnitt sind seither enger gefasst: die E-Mail aus
> dem Token zählt nur mit bestätigtem `email_verified`
> (`Oidc__TrustUnverifiedEmail` ist der ausdrückliche Ausweg), und eine leere
> Audience schaltet die Empfängerprüfung nicht mehr stillschweigend ab
> (`Oidc__RequireAudience`).

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
ersten Login eingelöst wird. Für Vereinsrollen ist das weiterhin nicht gebaut und beim
ersten Bedarf nachzuziehen.

**Der erste Administrator** ist ein Sonderfall davon, der sich nicht aufschieben
ließ: Rollen vergibt, wer eine Rolle hat, und nach einer frischen Migration hat
niemand eine. `Security:BootstrapSystemAdmins` nennt E-Mail-Adressen oder
Subject-IDs, die bei ihrer nächsten Anmeldung die globale Rolle `SystemAdmin`
bekommen — eingelöst in der Benutzerauflösung, weil das Konto vorher nicht
existiert. Die Konfiguration ist der einzige Kanal, der außerhalb der Datenbank
liegt und deshalb vor der ersten Anmeldung gefüllt sein kann.

Bewusst eng gehalten: nur `SystemAdmin`, nur global, und die Vergabe erscheint als
Warnung im Protokoll. Der Eintrag gehört geleert, sobald der erste Administrator
steht — sonst entsteht neben der Rollentabelle eine zweite, stille Quelle für
Berechtigungen, und genau die zu vermeiden ist der Zweck dieser Entscheidung.
