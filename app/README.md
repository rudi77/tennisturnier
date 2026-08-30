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
  auth/         oidc.ts · AuthProvider.tsx
  components/   core/ · layout/ (AppNav, AppBar, Sheet, ScreenHeader) · tournament/
  screens/      FlowScreen · TournamentsScreen · EntriesScreen · DrawScreen
                BoardScreen · CreateScreen · JoinScreen · PublicScreen
                FeedScreen · ProfileScreen · ConnectionsScreen · PlayDatesScreen
  hooks/        useResource · useToast · usePublicView · useRoute
  lib/          time.ts (Zeitzone, TimeSpan) · labels.ts (deutsche Beschriftungen)
  styles/       tokens/ (unverändert aus dem Design System) · app.css
```

### Die Hülle

`AppNav` und `AppBar` stehen einmal in `App.tsx` und nicht in den Bildschirmen.
Die Navigation ist am Telefon eine Fußleiste mit fünf Zielen — Ablauf, Feed,
Meldungen, Draw, Spielplan — und am Schreibtisch dieselbe Liste als Spalte; das
Markup ist beide Male dasselbe, den Unterschied macht `app.css` an der Schwelle
von 900 Pixeln. Der seltenere Rest steht am Telefon hinter „Mehr" in einer
`Sheet`, der Lade, die von unten hereinkommt.

Fünf und nicht acht: der Feed steht vorn, weil er das ist, wofür jemand die
Anwendung am Turniertag öffnet, ohne etwas eintragen zu wollen. Profil,
Mitspieler und Verabredungen stehen hinter „Mehr" — man kommt zu ihnen
üblicherweise über einen Namen und nicht über die Leiste.

Die Beschriftung eines Navigationspunkts steht als `aria-label` am Knopf und
nicht nur im Text: sichtbar ist je nach Breite die lange oder die kurze, und
eine vom Stylesheet ausgeblendete zählt für den zugänglichen Namen nicht.

Welches Turnier gemeint ist, sagt die `AppBar`. Ein Bildschirm sagt über
`ScreenHeader` nur noch, welcher er ist.

### Die sozialen Schirme

Vier Bildschirme gehören nicht zum Turnierablauf: `ProfileScreen`,
`ConnectionsScreen`, `PlayDatesScreen` und — als einziger davon am Turnier —
`FeedScreen`.

Sie hängen an einem weiteren Parameter in der Adresszeile: `?p=<playerId>` nennt
den Spieler, dessen Profil gemeint ist. Er steht neben `t` und nicht darin, weil
ein Profil zu keinem Turnier gehört — es rechnet über alle, die der Aufrufer
sieht (ADR-0013).

Der Weg zwischen ihnen führt über Namen: aus der Meldungsliste, aus einer
Feed-Zeile, aus einem Gegner im Profil, aus einem Gast einer Verabredung. Jeder
dieser Namen ist ein `md-linkbtn` — ein Knopf, der wie ein Link aussieht. Als
`<a href>` wäre er ein Versprechen, das die Anwendung nicht hält: sie führt ihre
Navigation über `history.pushState`, und ein Mittelklick öffnete eine leere
Seite.

### Mobil zuerst — und zwar wörtlich

Was in `app.css` ohne Medienabfrage steht, gilt am Telefon. Jede Medienabfrage
lautet `min-width` und nimmt zurück, was dort zu eng gedacht wäre. Dieselbe
Reihenfolge gilt für die Schriftskala in `tokens/typography.css`: sie beginnt
bei 15 Pixeln und wird zum Schreibtisch hin dichter, nicht umgekehrt.

Eingabefelder sind 16 Pixel groß (`--fs-input`). Das ist keine
Geschmacksfrage — darunter zoomt iOS beim Antippen die ganze Seite heran und
kommt nicht von selbst zurück. Trefferflächen sind nie kleiner als
`--hit-target` (44px).

`e2e/mobil.spec.ts` prüft auf 390 Pixeln, dass kein Bildschirm über den rechten
Rand ragt. Der Fehler entsteht im Zusammenspiel; wer ihn im Bauteil sucht,
findet ihn dort nicht. `e2e/ansicht.spec.ts` legt Aufnahmen ab — kein Test, ein
Werkzeug (`MATCHDAY_ANSICHT=1 npx playwright test ansicht`), und im regulären
Durchgang ausgenommen.

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
3. **Anlegen fragt nur, was niemand raten kann.** Name, Anlage, Tag, Disziplin,
   Modus stehen vorn; Plätze, Zeiten, Satzformat, Zeitzone und die Parameter der
   Vorlage stehen hinter einer Lade und haben Vorgaben. Das war einmal ein
   Assistent aus fünf Schritten — wer ihn zum ersten Mal vor sich hatte, musste
   Belag und Lage von Platz 2 entscheiden, bevor überhaupt eine Meldung offen
   war. Verschwunden ist nichts, es steht nur nicht mehr im Weg.
4. **Satzformat, Gruppen und Qualifikanten gehören zur *Formatvorlage*,** nicht
   zum Turnier. Eingebaute Vorlagen sind nicht editierbar; wer etwas ändert,
   bekommt beim Anlegen eine eigene Kopie. Das Satzformat selbst ist die
   Ausnahme: es gehört dem Turnier und legt keine Kopie an.
5. **Plätze legt das Anlegen an und bucht ihre Zeiten** — alle Plätze, alle
   Turniertage, eine Uhrzeitspanne, in einem Aufruf. Hier stand einmal, eine
   Auswahl der Plätze *pro Turnier* kenne die API nicht; die Lücke ist mit
   ADR-0009 zugegangen.

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
  entfallen; Plätze und ihre Zeiten entstehen jetzt beim Anlegen.
- `JoinScreen` — der Beitritt, erreichbar über `?r=<token>` und **hinter** der
  Anmeldung. Hier stand einmal ein `RegistrationScreen` ohne Konto davor; seit
  ADR-0012 ist ein Turnier eine Gruppe, und wer beitritt, hat ein Konto. Der
  Link bleibt derselbe: wer noch keines hat, legt beim Aussteller eines an und
  landet danach wieder hier — dafür wird die Route über den Redirect verwahrt
  (`stashRoute`/`restoreRoute` in `oidc.ts`). Zwei Schaltflächen, weil es zwei
  Arten gibt dazuzugehören: mitspielen oder bloß zusehen.
- `EntriesScreen` — die Meldungsverwaltung: Beitrittslink samt Kapazität und
  Meldeschluss, der Schalter zwischen privat und öffentlich, Meldungen annehmen
  oder auf die Warteliste, dazu das Panel, über das Schiedsrichter, weitere
  Turnierleiter und Mitglieder berufen oder eingeladen werden. Eine Einladung an
  eine Adresse ohne Konto steht dort als „eingeladen, noch nie angemeldet".
  Kontaktdaten stehen nur darin, wenn das Backend sie mitschickt — ein
  Ausblenden im Frontend wäre kein Schutz, sondern eine Behauptung.
- `DrawPreparation` (im `DrawScreen`, solange kein Draw steht) — Meldung öffnen,
  Teilnehmer melden, Meldeschluss, auslosen. Die Endpunkte dafür lagen in
  `endpoints.ts`, aber kein Screen rief sie auf: ein angelegtes Turnier blieb im
  Zustand `Draft` stehen, und der Turniertag antwortete zu Recht mit „setzt eine
  Auslosung voraus", ohne den Weg dorthin zu zeigen. Eine hier erfasste Meldung
  wird gleich angenommen; Warteliste, Setzpositionen und Rückzug stehen im
  `EntriesScreen`, wo sie hingehören — dort kommen seit ADR-0012 auch die
  Meldungen an, die niemand erfasst hat.

---

## Prüfstand

Das .NET-SDK ließ sich in der Entwicklungsumgebung nicht installieren
(`dot.net` antwortete mit 403), deshalb wurde die Oberfläche gegen einen
Stub-Server geprüft, der genau diesen Vertrag bedient — inklusive der Asymmetrie
bei den Aufzählungen. Geprüft und in Ordnung: alle vier Screens, beide
Spielplanmodi, alle drei Bracket-Varianten, das Anlegen samt seiner Lade, beide
Live-Geräte, Ergebnis-Modal, Solver-Vorschlag mit Diff und Begründungen,
ETag/304-Zyklus und der Rückfall auf Polling, wenn der SignalR-Hub fehlt.

**Nicht geprüft**, weil dafür die echte API laufen muss: der OIDC-Anmeldefluss gegen
Keycloak, die schreibenden Endpunkte gegen echte Domänenregeln (422-Fälle),
SignalR-Push und die Nebenläufigkeit (409).
