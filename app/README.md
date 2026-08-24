# MATCHDAY — Weboberfläche

Die Oberfläche zur Turnierplattform: Turnierleitung (Spielplan, Draw, Turnier anlegen)
und die öffentliche Live-Ansicht.

Umgesetzt aus dem Design-Handoff `Tennisturnier Plattform.dc.html` (Claude Design,
Projekt *Tennisturnier-Plattform UI-Design*) gegen die bestehende .NET-API dieses
Repositories. Die Tokens unter `src/styles/tokens/` sind unverändert aus dem
Design-System übernommen.

React 19 · TypeScript · Vite. Kein UI-Framework — das Design System *ist* das Framework.

---

## Starten

Drei Prozesse, in dieser Reihenfolge:

```bash
docker compose up -d keycloak                    # IdP mit Test-Realm
dotnet run --project src/TennisTurnier.Api       # API auf http://localhost:5188

cd app
cp .env.example .env.local                       # einmalig
npm install
npm run dev                                      # http://localhost:5000
```

Anmelden mit einem der Testbenutzer aus dem Realm (`systemadmin`, `clubadmin`,
`referee`; Passwort jeweils gleich dem Benutzernamen). Die Namen stammen aus der
Zeit des Vereins und sagen nichts mehr über Rollen: die vergibt die Anwendung.
Wer sich anmeldet, darf Turniere ausschreiben und führt, was er anlegt.

> **Der Port 5000 ist nicht verhandelbar.** Der Test-Realm
> (`deploy/keycloak/import/tennisturnier-realm.json`) trägt für den öffentlichen Client
> `tennisturnier-api` genau `http://localhost:5000/*` als Redirect-URI und
> `http://localhost:5000` als Web-Origin. Auf Vites Standardport 5173 bricht der
> Login-Redirect ab, bevor irgendetwas geladen ist. Deshalb steht in
> `vite.config.ts` `strictPort: true`.

### Skripte

| Befehl | Wirkung |
| --- | --- |
| `npm run dev` | Dev-Server auf 5000, Proxy auf die API |
| `npm run build` | Typprüfung und Produktionsbündel nach `dist/` |
| `npm run typecheck` | nur `tsc` |
| `npm run preview` | das gebaute Bündel lokal ausliefern |

---

## Warum ein Proxy und kein CORS

Die API konfiguriert **kein CORS** (`Program.cs` ruft weder `AddCors` noch
`UseCors`). Ein Frontend auf einem eigenen Port wäre damit im Browser blockiert.

Der Dev-Server leitet deshalb `/api`, `/public`, `/hubs` und `/health` an
`http://localhost:5188` weiter — alles ist gleich-origin, und das Backend bleibt
unverändert.

**Für Produktion ist das eine offene Entscheidung**, und sie gehört ins Backend,
nicht hierher:

- entweder das gebaute `dist/` wird von derselben Herkunft ausgeliefert wie die
  API (`UseStaticFiles` + Fallback auf `index.html`) — dann bleibt es bei
  gleich-origin und es ändert sich nichts;
- oder das Frontend läuft getrennt — dann braucht die API eine CORS-Richtlinie
  für dessen Herkunft, und die Redirect-URIs im IdP müssen mitgezogen werden.

`VITE_API_BASE_URL` ist für den zweiten Fall vorbereitet und im Dev-Betrieb leer.

---

## Umgebungsvariablen

Siehe `.env.example`. Die vier, auf die es ankommt:

| Variable | Bedeutung |
| --- | --- |
| `VITE_API_PROXY_TARGET` | Ziel des Dev-Proxys (Vorgabe `http://localhost:5188`) |
| `VITE_API_BASE_URL` | Basis-URL der API. Leer = gleiche Herkunft |
| `VITE_OIDC_AUTHORITY` | Aussteller. Leer = **nur** die öffentliche Ansicht |
| `VITE_OIDC_CLIENT_ID` | öffentlicher Client (Vorgabe `tennisturnier-api`) |

---

## Aufbau

```
src/
  api/          types.ts (Verträge) · client.ts (fetch + ProblemDetails)
                endpoints.ts (Endpunkte 1:1) · realtime.ts (SignalR)
  auth/         oidc.ts · AuthProvider.tsx · LoginScreen.tsx
  components/   core/ · layout/ · tournament/
  screens/      TournamentsScreen · EntriesScreen · DrawScreen · BoardScreen
                WizardScreen · RegistrationScreen · PublicScreen
  hooks/        useResource · useToast · usePublicView · useRoute
  lib/          time.ts (Zeitzone, TimeSpan) · labels.ts (deutsche Beschriftungen)
  styles/       tokens/ (unverändert aus dem Design System) · app.css
```

### Zwei Dinge, die man wissen muss, bevor man etwas ändert

**1. Eine Zeit ist entweder eine Zusage oder eine Schätzung.**
`EarliestStart` wird fett und aufrecht gesetzt, `PlannedStart` kursiv, grau und mit
`~`. Jede Zeit läuft über `<TimeLabel>`; es gibt keinen Weg daran vorbei, und das
ist Absicht. Eine Schätzung als exakte Uhrzeit zu drucken ist eine Lüge, für die
später das Werkzeug verantwortlich gemacht wird. Passt eine Schätzung nicht mehr in
die Öffnungszeiten (`withinOpeningHours === false`), wird sie rot — das ist eine
Auskunft, keine Fehlermeldung.

**2. Die Aufzählungen kommen in zwei Darstellungen.**
Die `/api`-Endpunkte serialisieren mit den Vorgaben von Minimal API, und dort ist
kein `JsonStringEnumConverter` registriert — Aufzählungen sind **Zahlen**. Die
öffentliche Projektion wird getrennt serialisiert (`PublicViewService.PublicJson`)
und registriert ihn — dieselben Aufzählungen sind dort **Zeichenketten**. Beide
Formen stehen in `types.ts` nebeneinander (`AssignmentStatus` vs.
`PublicAssignmentStatus`). Eine davon beim Lesen stillschweigend umzubiegen wäre die
Sorte Fehler, die sich als „der Status zeigt nie *läuft*" äußert.

### Fehler

`ApiError` bildet die Abbildung des `DomainExceptionHandler` ab und ebnet sie nicht
ein: 404 heißt „nicht gefunden **oder** fremdes Turnier" (ADR-0009 — die Oberfläche
darf den Unterschied nicht erfinden), 409 heißt „jemand war schneller" und ist am
Turniertag der Normalfall, 422 trägt die Meldung der Domäne und wird unverändert
angezeigt.

---

## Was gegenüber dem Entwurf anders ist

Der Prototyp kannte genau ein Turnier und keine API. Vier Stellen mussten deshalb
anders geschnitten werden; jede ist im Screen selbst benannt, nicht nur hier.

1. **Turnierauswahl.** Die API ist mandantenfähig — ein Turnier sieht nur, wer
   eine Rolle daran hat —, also steht in der Kopfzeile eine Auswahl. Der Entwurf
   hatte sie nicht, weil er nur ein Turnier kannte. Ein Verein stand hier einmal
   daneben; er ist mit ADR-0009 entfallen.
2. **Der Moduswechsel Planung ↔ Turniertag ruft die API** (`scheduling/match-day`
   bzw. `…/planning`) statt ein lokales Flag zu setzen. Er ändert die Bedeutung jeder
   angezeigten Uhrzeit — das ist ein Zustandsübergang, kein Schalter.
3. **Wizard, Schritt „Parameter".** Satzformat, Gruppen und Qualifikanten gehören zur
   *Formatvorlage*, nicht zum Turnier. Eingebaute Vorlagen sind nicht editierbar; wer
   etwas ändert, bekommt beim Anlegen eine eigene Kopie. Genau das tut der
   Schritt, und die Vorschau sagt es an.
4. **Wizard, Schritt „Plätze".** Hier stand einmal, eine Auswahl der Plätze *pro
   Turnier* kenne die API nicht — Plätze gehörten dem Verein. Genau diese Lücke
   ist mit ADR-0009 zugegangen: der Schritt legt die Plätze an und bucht ihre
   Zeiten, beide Plätze, alle Turniertage, eine Uhrzeitspanne, in einem Aufruf.

Dazu eine Ergänzung, die der Entwurf nicht hatte, die die API aber braucht: bei
`Retirement`, `Walkover` und `Disqualification` fragt die Ergebniseingabe nach der
**betroffenen Seite** (`affectedSide`). Ohne sie weiß die Domäne nicht, wer
weiterkommt.

### Noch nicht gebaut

Bewusst nicht, weil es dafür keine Vorlage im Entwurf gab: Gruppentabellen mit
Tiebreaker-Kette (`…/phases/{id}/standings` ist im Client vorhanden, aber
unbenutzt), Schweizer System als eigener Screen. Die Endpunkte dafür stehen; es
fehlt die Gestaltung.

**Nachgereicht**, weil eine frische Installation sonst nicht bis zum ersten
Turnier kommt — nichts davon hatte im Entwurf eine Vorlage, nichts davon fehlt
trotzdem *optional*:

- `TournamentsScreen` — der Einstieg: die eigenen Turniere, und die Schaltfläche,
  mit der das erste entsteht. Hier stand einmal ein `ClubScreen`, auf dem jemand
  einen Verein anlegen musste, bevor irgendetwas ging. Er ist mit ADR-0009
  entfallen; Plätze und ihre Zeiten legt jetzt der Wizard an.
- `RegistrationScreen` — die öffentliche Anmeldung, erreichbar über `?r=<token>`
  und **vor** der Anmeldemaske. Wer über einen Aushang kommt, soll kein Konto
  brauchen; eine Anmeldemaske davor nähme dem Link seinen Zweck (ADR-0010).
- `EntriesScreen` — die Meldungsverwaltung: Anmeldelink samt Kapazität und
  Meldeschluss, Meldungen annehmen oder auf die Warteliste, dazu das Panel, über
  das Schiedsrichter und weitere Turnierleiter berufen werden. Kontaktdaten
  stehen nur darin, wenn das Backend sie mitschickt — ein Ausblenden im Frontend
  wäre kein Schutz, sondern eine Behauptung.
- `DrawPreparation` (im `DrawScreen`, solange kein Draw steht) — Meldung öffnen,
  Teilnehmer melden, Meldeschluss, auslosen. Die Endpunkte dafür lagen in
  `endpoints.ts`, aber kein Screen rief sie auf: ein angelegtes Turnier blieb im
  Zustand `Draft` stehen, und der Turniertag antwortete zu Recht mit „setzt eine
  Auslosung voraus", ohne den Weg dorthin zu zeigen. Eine hier erfasste Meldung
  wird gleich angenommen; Warteliste, Setzpositionen und Rückzug stehen im
  `EntriesScreen`, wo sie hingehören — dort kommen seit ADR-0010 auch die
  Meldungen an, die niemand erfasst hat.

---

## Prüfstand

Das .NET-SDK ließ sich in der Entwicklungsumgebung nicht installieren
(`dot.net` antwortete mit 403), deshalb wurde die Oberfläche gegen einen
Stub-Server geprüft, der genau diesen Vertrag bedient — inklusive der Asymmetrie
bei den Aufzählungen. Geprüft und in Ordnung: alle vier Screens, beide
Spielplanmodi, alle drei Bracket-Varianten, alle vier Wizard-Schritte, beide
Live-Geräte, Ergebnis-Modal, Solver-Vorschlag mit Diff und Begründungen,
ETag/304-Zyklus und der Rückfall auf Polling, wenn der SignalR-Hub fehlt.

**Nicht geprüft**, weil dafür die echte API laufen muss: der OIDC-Anmeldefluss gegen
Keycloak, die schreibenden Endpunkte gegen echte Domänenregeln (422-Fälle),
SignalR-Push und die Nebenläufigkeit (409).
