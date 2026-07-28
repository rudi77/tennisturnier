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
`referee`; Passwort jeweils gleich dem Benutzernamen).

> **Der Port 5000 ist nicht verhandelbar.** Der Test-Realm
> (`deploy/keycloak/tennisturnier-realm.json`) trägt für den öffentlichen Client
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
  screens/      BoardScreen · DrawScreen · WizardScreen · PublicScreen
  hooks/        useResource · useToast · usePublicView
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
ein: 404 heißt „nicht gefunden **oder** fremder Verein" (ADR-0004 — die Oberfläche
darf den Unterschied nicht erfinden), 409 heißt „jemand war schneller" und ist am
Turniertag der Normalfall, 422 trägt die Meldung der Domäne und wird unverändert
angezeigt.

---

## Was gegenüber dem Entwurf anders ist

Der Prototyp kannte genau ein Turnier und keine API. Vier Stellen mussten deshalb
anders geschnitten werden; jede ist im Screen selbst benannt, nicht nur hier.

1. **Turnierauswahl.** Die API ist mandantenfähig und club-scoped, also steht in der
   Kopfzeile eine Auswahl für Verein und Turnier. Der Entwurf hatte sie nicht, weil
   er nur ein Turnier kannte.
2. **Der Moduswechsel Planung ↔ Turniertag ruft die API** (`scheduling/match-day`
   bzw. `…/planning`) statt ein lokales Flag zu setzen. Er ändert die Bedeutung jeder
   angezeigten Uhrzeit — das ist ein Zustandsübergang, kein Schalter.
3. **Wizard, Schritt „Parameter".** Satzformat, Gruppen und Qualifikanten gehören zur
   *Formatvorlage*, nicht zum Turnier. Eingebaute Vorlagen sind nicht editierbar; wer
   etwas ändert, legt beim Anlegen eine Kopie des Vereins an. Genau das tut der
   Schritt, und die Vorschau sagt es an.
4. **Wizard, Schritt „Plätze".** Eine Auswahl der Plätze *pro Turnier* gibt es in der
   API nicht — Plätze gehören dem Verein, der Solver nimmt die aktiven. Der Schritt
   zeigt deshalb die freien Fenster aus `…/free-windows`, statt eine Auswahl
   vorzutäuschen, die nirgends ankommt.

Dazu eine Ergänzung, die der Entwurf nicht hatte, die die API aber braucht: bei
`Retirement`, `Walkover` und `Disqualification` fragt die Ergebniseingabe nach der
**betroffenen Seite** (`affectedSide`). Ohne sie weiß die Domäne nicht, wer
weiterkommt.

### Noch nicht gebaut

Bewusst nicht, weil es dafür keine Vorlage im Entwurf gab: Gruppentabellen mit
Tiebreaker-Kette (`…/phases/{id}/standings` ist im Client vorhanden, aber
unbenutzt), Schweizer System als eigener Screen, Teilnehmerliste mit Seeds und
Warteliste, Club-Administration (Plätze, Öffnungszeiten, Sperren anlegen). Die
Endpunkte dafür stehen; es fehlt die Gestaltung.

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
