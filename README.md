# tennisturnier

[![CI](https://github.com/rudi77/tennisturnier/actions/workflows/ci.yml/badge.svg)](https://github.com/rudi77/tennisturnier/actions/workflows/ci.yml)

Turnierplattform für Tennisvereine: Platzverwaltung, Turniere in verschiedenen Modi
(K.O., Gruppenphase + K.O., jeder gegen jeden, Liga, Schweizer System) für Einzel,
Doppel und Mixed, einstellbares Satzformat bis hinunter zu Kurzsätzen mit
Champions-Tiebreak, automatischer und manuell korrigierbarer Spielplan sowie eine
öffentliche Live-Ansicht.

**Status:** im Aufbau. Der Fahrplan steht in [docs/roadmap.md](docs/roadmap.md).

## Schnellstart

```bash
dotnet restore
dotnet tool restore
dotnet build
dotnet test
```

Voraussetzung ist das .NET 10 SDK. In Claude-Code-Web-Sessions erledigt der
SessionStart-Hook (`.claude/hooks/setup.sh`) Installation und Restore automatisch.

### Anwendung starten

```bash
docker compose up -d keycloak          # lokaler Identity Provider mit Test-Realm
dotnet run --project src/TennisTurnier.Api
```

Die Datenbank ist eine SQLite-Datei, die beim Start angelegt und migriert wird.
Ohne Keycloak startet die Anwendung ebenfalls — dann sind nur die öffentlichen
Endpunkte erreichbar.

> **Einmalig nach dem Umbau „Turnier als Wurzel":** Das Schema hat eine neue
> Baseline-Migration bekommen, und es gibt keinen datenerhaltenden Pfad — der
> Verein ist als Aggregat entfallen. Eine bestehende Datei aus der Zeit davor
> lässt sich nicht migrieren; sie wird gelöscht und beim nächsten Start neu
> angelegt:
>
> ```bash
> rm -f tennisturnier.db tennisturnier.db-wal tennisturnier.db-shm
> ```

Ein Token für die Testbenutzer (`systemadmin`, `clubadmin`, `referee`;
Passwort jeweils gleich dem Benutzernamen):

```bash
curl -s -X POST http://localhost:8080/realms/tennisturnier/protocol/openid-connect/token \
  -d grant_type=password -d client_id=tennisturnier-api \
  -d username=systemadmin -d password=systemadmin | jq -r .access_token
```

Die Rollen selbst vergibt die Anwendung, nicht Keycloak (siehe ADR-0007). Wer
sich anmeldet, wird `Organizer` und darf damit Turniere ausschreiben; wer eines
anlegt, wird dessen Turnierleiter. Alles Weitere — Schiedsrichter, weitere
Turnierleiter — berufen die Turnierleitungen selbst über
`/api/tournaments/{id}/roles`. Der Selbstservice lässt sich über
`Security:SelfServiceOrganizers` abschalten, wenn eine Instanz geschlossen
laufen soll.

## Betrieb

Ein Bild, ein Dienst. Das `Dockerfile` baut die Oberfläche und die Anwendung und
legt die gebauten Dateien der Oberfläche neben die Anwendung, die sie mit
ausliefert. Zwei Dienste wären der naheliegende Schnitt und hier der falsche:
gleich-origin heißt kein CORS, kein zweiter Hostname im Identity Provider und
keine Weiterleitung zwischen zwei Diensten.

```bash
docker build -t matchday .
docker run --rm -p 8080:8080 -v matchday-daten:/data \
  -e Oidc__Authority=https://idp.example.org/realms/matchday \
  -e Oidc__ClientId=matchday-web \
  matchday
```

### Railway

Railway baut direkt aus GitHub. `railway.json` legt fest, dass das `Dockerfile`
gebaut wird und nicht geraten — ohne diese Angabe sucht Railpack nach einem
Projekt, das es kennt, und findet in einer Projektmappe aus mehreren
Verzeichnissen keines.

1. In Railway ein Projekt aus dem GitHub-Repository anlegen. Mehr als das
   Repository braucht es nicht: Bauweise, Gesundheitsprüfung und
   Neustartverhalten stehen in `railway.json`.
2. Einen Datenträger anlegen und auf `/data` hängen. **Ohne ihn ist die
   Datenbank nach jedem Neustart leer** — sie ist eine Datei, und ein Container
   ohne Datenträger vergisst seine Dateien.
3. Die Variablen setzen (siehe unten).

Den Port gibt Railway über `PORT` vor; die Anwendung nimmt ihn beim Start
entgegen. Die Adresse der Instanz gehört anschließend in den Identity Provider —
als gültige Weiterleitung (`https://…/`) und als erlaubter Ursprung. Wer den
Keycloak-Dienst von unten mitnimmt, bekommt beides ohne Zutun.

| Variable | Bedeutung |
| --- | --- |
| `Oidc__Authority` | Der Aussteller, z. B. `https://idp.example.org/realms/matchday`. Leer heißt: keine Anmeldung, nur die öffentlichen Endpunkte. |
| `Oidc__ClientId` | Der Client, unter dem sich die Oberfläche anmeldet. |
| `Oidc__Audience` | Wofür ein Token gelten muss. Vorgabe `tennisturnier-api`. |
| `Oidc__Scope` | Vorgabe `openid profile email`. |
| `Security__OpenAccess` | `true` lässt die Instanz ohne Anmeldung laufen (siehe unten). Zusammen mit `Oidc__Authority` verweigert die Anwendung den Start. |
| `Security__BootstrapSystemAdmins__0` | Die E-Mail-Adresse, die beim ersten Anmelden Systemadministrator wird. Danach wieder leeren. |
| `Security__SelfServiceOrganizers` | Vorgabe `true`: wer sich anmeldet, darf Turniere ausschreiben. Für eine Instanz mit offenem Anmeldeweg (siehe Google) auf `false`. |
| `Tournament__TeamDrawSeed` | Saatwert für das Los der Teams. Nur für Vorführungen — wer ihn kennt, kennt die Paarung, bevor sie fällt. |
| `ConnectionStrings__Default` | Vorgabe `Data Source=/data/matchday.db`, passend zum Datenträger. |

Die Oberfläche holt sich `Oidc__Authority`, `Oidc__ClientId` und `Oidc__Scope`
zur Laufzeit über `/config.js`. Sie sind deshalb nicht ins Bündel gebaut, und
dasselbe Bild läuft gegen jeden Aussteller — ein Wechsel des Realms ist eine
Variable, kein neuer Bau.

### Der erste Schritt: ohne Anmeldung

Eine Instanz steht meist, bevor ein Identity Provider steht. `Security__OpenAccess=true`
lässt MATCHDAY dann trotzdem arbeiten: es gibt keine Anmeldemaske, jeder Aufruf
gilt als derselbe Benutzer, und der ist Systemadministrator.

Das ist keine halbe Anmeldung, sondern gar keine — wer die Adresse kennt, kann
Turniere anlegen, Ergebnisse eintragen und löschen. Deshalb steht es in der
Oberfläche über jedem Schirm und beim ersten Aufruf als Warnung im Protokoll,
statt still zu wirken. Für einen Probelauf im Verein ist das in Ordnung; für
eine Adresse, die herumgereicht wird, nicht.

Zwei Entscheidungen dahinter sind Absicht:

- Der Schalter muss ausdrücklich gesetzt werden. Aus einer fehlenden Authority
  abgeleitet hieße „kein Aussteller konfiguriert" plötzlich „offen für alle" —
  und dieser Wechsel darf niemandem versehentlich passieren. Ohne Schalter und
  ohne Authority bleibt es wie bisher bei der öffentlichen Live-Ansicht.
- Zusammen mit `Oidc__Authority` **startet die Anwendung nicht**. Der stille
  Ausgang wäre der gefährliche: ein versehentlich gesetzter Schalter machte eine
  angemeldete Instanz auf, ohne dass es jemandem auffiele.

Der offene Betrieb legt ein echtes Konto an (`Ohne Anmeldung`), dem alles
gehört, was in dieser Zeit entsteht. Wird die Anmeldung später eingeschaltet,
bleiben die Turniere also stehen — anmelden kann sich niemand mehr als dieses
Konto, und ein Systemadministrator kann ihm seine Rolle nehmen. Der Weg vom
ersten Schritt zum zweiten ist damit: Keycloak aufsetzen (unten),
`Security__OpenAccess` entfernen, `Oidc__Authority` setzen, sich anmelden und
über `Security__BootstrapSystemAdmins__0` zum Administrator machen.

### Keycloak als zweiter Dienst

MATCHDAY prüft Tokens, stellt aber keine aus. Ohne erreichbaren Aussteller
bleibt es bei der öffentlichen Ansicht — das lokale Keycloak aus
`docker-compose.yml` ist von Railway aus nicht erreichbar, und `localhost`
zeigt dort auf den Container selbst.

`deploy/keycloak/` enthält deshalb ein zweites Bild: derselbe Realm wie lokal,
aber im Produktionsmodus gegen PostgreSQL statt `start-dev` gegen den
Arbeitsspeicher. Der Unterschied ist keiner der Bequemlichkeit — `start-dev`
vergisst beim Neustart jeden Benutzer, jede Sitzung und jede Verknüpfung zu
einem Google-Konto.

1. Im selben Railway-Projekt einen Dienst aus demselben Repository anlegen und
   sein **Root Directory** auf `deploy/keycloak` setzen. Den Rest sagt das
   `railway.json` daneben.
2. Eine PostgreSQL-Datenbank hinzufügen und den Dienst darauf zeigen lassen:

   | Variable | Wert |
   | --- | --- |
   | `KC_DB_URL` | `jdbc:postgresql://${{Postgres.PGHOST}}:${{Postgres.PGPORT}}/${{Postgres.PGDATABASE}}` |
   | `KC_DB_USERNAME` | `${{Postgres.PGUSER}}` |
   | `KC_DB_PASSWORD` | `${{Postgres.PGPASSWORD}}` |
   | `KC_BOOTSTRAP_ADMIN_USERNAME` | Der erste Administrator — nur beim ersten Start nötig. |
   | `KC_BOOTSTRAP_ADMIN_PASSWORD` | Dessen Passwort. Danach beides wieder entfernen. |
   | `MATCHDAY_ORIGIN` | `https://${{matchday.RAILWAY_PUBLIC_DOMAIN}}` — **ohne** Schrägstrich am Ende. |

3. In MATCHDAY `Oidc__Authority` auf
   `https://${{keycloak.RAILWAY_PUBLIC_DOMAIN}}/realms/tennisturnier` setzen und
   `Oidc__ClientId` auf `tennisturnier-api`.

Beide Dienste bekommen von Railway je eine eigene Domain, und die Verweise
oben (`${{dienst.RAILWAY_PUBLIC_DOMAIN}}`, mit den tatsächlichen Dienstnamen)
ersparen das Abtippen: ändert sich eine Domain, zieht die andere Seite mit.

Zwei Domains sind dabei kein Umweg. Über die Grenze gehen genau zwei Dinge —
die Weiterleitung des Browsers zur Anmeldeseite, für die es keine
Same-Origin-Regel gibt, und der Aufruf, mit dem die Oberfläche den Code gegen
ein Token tauscht; für den steht die Adresse als erlaubter Ursprung im Client.
Alles Übrige prüft MATCHDAY vom Server aus, ohne Browser dazwischen. Dass
Oberfläche und API in **einem** Bild liegen, ist der Fall, in dem
Gleich-Origin tatsächlich zählt: die beiden reden ständig miteinander.

Das private Netz von Railway (`<dienst>.railway.internal`) taugt hier nicht:
Keycloak leitet seinen Aussteller aus der Anfrage ab, antwortet über die
interne Adresse also mit einem anderen `iss`, als in den Tokens im Browser
steht — und die Prüfung scheitert. `Oidc__Authority` gehört auf die öffentliche
Domain.

`MATCHDAY_ORIGIN` trägt die Adresse der Instanz als gültige Weiterleitung und
erlaubten Ursprung in den Client ein. Sie muss stehen, **bevor** Keycloak das
erste Mal startet: eingespielt wird der Realm nur, solange es ihn noch nicht
gibt — sonst überschriebe jeder Neustart die Benutzer, die inzwischen
dazugekommen sind. Was danach kommt, gehört in die Administrationsoberfläche.

Aus derselben Datei kommen `systemadmin`, `clubadmin` und `referee` mit
Passwörtern, die ihren Namen gleichen. Sie sind für den Entwicklungsbetrieb
gedacht; in einer erreichbaren Instanz gehören sie gelöscht oder umgestellt,
sobald ein echtes Konto Systemadministrator ist.

### Google als Anmeldeweg

Keycloak steht dann vor Google, nicht daneben: MATCHDAY kennt weiterhin genau
einen Aussteller, und wer sich mit Google anmeldet, bekommt in Keycloak ein
Konto, dem die Anwendung anschließend Rollen geben kann (ADR-0007).

1. In der Google Cloud Console einen OAuth-Client vom Typ *Webanwendung*
   anlegen. Als autorisierte Weiterleitungs-URI trägt er genau eine Adresse:
   `https://<keycloak-domain>/realms/tennisturnier/broker/google/endpoint`
2. Am Keycloak-Dienst setzen:

   | Variable | Wert |
   | --- | --- |
   | `KC_GOOGLE_ENABLED` | `true` |
   | `KC_GOOGLE_CLIENT_ID` | Die Client-ID aus der Google Cloud Console. |
   | `KC_GOOGLE_CLIENT_SECRET` | Das dazugehörige Secret. |

Auch diese drei wirken über den Realm-Import und damit nur beim ersten Start;
danach ist die Administrationsoberfläche der Ort dafür. Ohne sie bleibt der
Anmeldeweg angelegt, aber abgeschaltet — lokal sieht man deshalb nichts davon.

Ein offener Anmeldeweg heißt: jedes Google-Konto kommt bis zum Anmeldeschirm.
Rollen bringt das keine mit — außer der einen, die MATCHDAY von sich aus
vergibt: wer sich anmeldet, wird `Organizer` und darf ausschreiben. Für eine
Instanz, die nicht dem ganzen Internet offenstehen soll, gehört
`Security__SelfServiceOrganizers` deshalb auf `false`, und die Turnierleitungen
werden von Hand berufen.

Ein Hinweis für Instanzen mit vielen Meldungen: die Schranke der anonymen
Meldeendpunkte teilt nach IP, und hinter dem Proxy von Railway ist das die des
Proxys. Wo das zu eng wird, hebt `Security__PublicRegistrationRequestsPerWindow`
sie an.

## Tests

Drei Ebenen, die sich nicht ersetzen: die Domäne rechnet ohne Datenbank, die
API läuft gegen eine echte SQLite-Datei, und der Durchlauf im Browser geht
gegen den echten Stapel samt Keycloak.

```bash
./scripts/coverage.ps1        # Backend: alle Testprojekte, Abdeckung, Lücken
cd app && npm run coverage    # Frontend: Vitest mit MSW
cd app && npm run e2e         # Ende zu Ende: Playwright gegen API und Keycloak
```

`scripts/coverage.ps1` lässt die Testprojekte nacheinander laufen und rechnet
ihre Treffer in einem Bericht zusammen. Das ist kein Umweg: nachträglich
zusammenführen lässt sich nicht, weil eine Zeile mit zwei Ausgängen in zwei
Berichten mit je 50 Prozent steht und daraus nicht hervorgeht, ob es dieselbe
Hälfte war. Am Ende steht, was ungetestet blieb — und der Aufruf endet rot,
sobald das etwas ist. Für einen Ausschnitt: `-Filter MatchService`.

Der Stand ist **100 Prozent** in beiden Ebenen, Zeilen wie Zweige. Das ist
weniger ein Ziel als eine Arbeitsweise: was sich nicht erreichen ließ, war
jedes Mal Code, den es nicht braucht — eine Ausweichfassung für einen Fall, den
ein Fremdschlüssel ausschließt, eine zweite Prüfung derselben Sache, ein
Rückfall hinter einer Schleife, die immer trifft. Die Abdeckung war dabei das
Werkzeug, das sie gefunden hat.

Der Playwright-Durchlauf braucht Keycloak (`docker compose up -d keycloak`) und
startet API und Vite selbst; er benutzt Port 5001 und eine eigene
Datenbankdatei unter `app/.playwright`.

Dieselben drei Ebenen laufen im
[CI](https://github.com/rudi77/tennisturnier/actions/workflows/ci.yml), dort
zusätzlich mit dem Bau des Frontend-Bündels. Jeder Durchgang schreibt seine
Zahlen in die Zusammenfassung des Laufs — Tests je Projekt, Abdeckung nach
Assembly, Compilermeldungen — und legt die vollständigen Berichte als Artefakte
ab: die begehbare HTML-Abdeckung beider Ebenen und den Playwright-Bericht samt
Spuren gescheiterter Durchläufe. Warnungen und gescheiterte Tests stehen
zusätzlich als Annotation an ihrer Zeile.

## Architektur

Ports & Adapters. Der fachliche Kern — Paarungserzeugung, Tabellen, Tiebreaker,
Satzvalidierung, Platzverfügbarkeit — liegt in `TennisTurnier.Domain` und ist ohne
Datenbank testbar.

```
src/TennisTurnier.Domain                        keine Projekt-, keine Paketreferenzen
src/TennisTurnier.Application                   → Domain (Ports + Anwendungsfälle)
src/TennisTurnier.Adapters.Persistence.Sqlite   → Application (EF Core)
src/TennisTurnier.Adapters.Identity.Oidc        → Application (Keycloak / Entra ID)
src/TennisTurnier.Adapters.Scheduling           → Application (Spielplan-Solver)
src/TennisTurnier.Api                           → alle (Composition Root, Minimal API)
```

Die Abhängigkeitsrichtung wird nicht per Konvention gepflegt, sondern in
`tests/TennisTurnier.Architecture.Tests` bei jedem Build geprüft.

Die tragenden Entscheidungen samt verworfener Alternativen stehen in
[docs/adr](docs/adr/README.md) — insbesondere:

- [ADR-0001](docs/adr/0001-turnierformate-als-phasen.md): Turnierformate sind
  komponierbare Phasen, kein Enum und kein Plugin-System.
- [ADR-0002](docs/adr/0002-scheduling-planungsraster-und-queue.md): Spielplan im
  Planungsmodus, Court-Queues am Turniertag — weil Matchdauern unbekannt sind.
- [ADR-0003](docs/adr/0003-getrenntes-read-modell.md): eigene Projektion für die
  öffentliche Ansicht.
- [ADR-0009](docs/adr/0009-turnier-als-wurzelaggregat.md): das Turnier ist die
  Wurzel, der Verein ist entfallen; Rollen hängen am Turnier, durchgesetzt per
  Query-Filter. Ersetzt ADR-0004.
- [ADR-0010](docs/adr/0010-oeffentliche-selbstmeldung.md): Melden über einen
  Token-Link, ohne Konto — samt den drei Regeln, ohne die der Endpunkt still
  scheitert.
- [ADR-0008](docs/adr/0008-spielerstammdaten.md): Spieler gehören keinem Turnier
  — samt dem Preis, dass der Query-Filter bei ihnen nicht greift.

## Doppel: wer mit wem

Zwei Turniere, die sich für den Melder grundlegend unterscheiden — und die
Ausschreibung entscheidet welches:

- **Paare melden sich gemeinsam.** Das Vereinsdoppel: jeder bringt seinen
  Partner mit, und eine Meldung ist ein Paar. Das Meldeformular fragt nach
  ihm, die hochgeladene Liste hat Partnerspalten.
- **Die Turnierleitung stellt die Teams.** Der Schleiferl- oder Mixed-Abend: es
  meldet sich jeder für sich, und wer mit wem spielt, fällt danach — per Los
  über alle offenen Meldungen oder von Hand, zwei ausgewählt und zusammengelegt.

Im zweiten Fall ist ein Team eine eigene Meldung; die beiden Meldungen dahinter
bleiben bestehen und stehen auf „im Team". Das ist keine Buchhaltung: sie sind
die Anmeldungen zweier Menschen, samt Bestätigungscode und Meldezeitpunkt, und
wer sie zusammenlegt, nimmt ihnen nicht den Weg zu ihrer eigenen Anmeldung. Ein
Team lässt sich jederzeit wieder auflösen, solange nicht ausgelost ist.

Das Los nimmt echten Zufall. Für eine Vorführung oder eine Testumgebung lässt
sich in der Konfiguration ein Saatwert setzen — `Tournament:TeamDrawSeed` —,
und dann ergibt dieselbe Meldungsliste immer dieselben Teams. Für ein Turnier,
bei dem tatsächlich um etwas gelost wird, gehört er leer: wer den Saatwert
kennt, kennt die Paarung, bevor sie fällt.

Die Kapazität zählt dabei Menschen und nicht Teams — ein Feld für zwölf ist bei
zwölf Meldungen voll, nicht bei zwölf Paaren. Und ausgelost wird erst, wenn
niemand mehr ohne Team im Feld steht: eine einzelne Spielerin im Draw eines
Doppels fiele sonst erst am Platz auf, wenn zwei gegen eine antreten. Wer bei
ungerader Zahl übrig bleibt, bleibt sichtbar stehen — was mit ihm geschieht,
entscheidet die Turnierleitung und nicht das Los.

## Spielplan

Im Planungsmodus rechnet `POST /api/tournaments/{id}/schedule/proposal` einen
Vorschlag, ohne etwas zu verändern; erst `…/schedule/confirm` trägt ihn ein.
Diese Trennung ist Absicht (ADR-0002): ein Solverlauf, der den Plan still
überschreibt, ist der Grund, aus dem Turnierleitungen die Automatik abschalten.

Der Vorschlag nennt zu jeder Ansetzung, was sie bindet — „frühestmöglich nach
dem Vorspiel, das um 14:30 endet, zuzüglich 30 Minuten Pause" — und dazu einen
Diff: wie viele Ansetzungen bleiben, entstehen, sich verschieben. Von Hand
gesetzte und festgenagelte Zuweisungen gehen als harte Vorgabe in den nächsten
Lauf, und was zulässig bleiben kann, bleibt stehen: eine Verschiebung von Hand
bewegt nur das, was im Baum daran hängt oder ihr im Weg liegt.

Geprüft wird der Vorschlag vom selben `ScheduleValidator`, der auch eine
Verschiebung von Hand beurteilt. Ein Solver, der seine eigenen Ergebnisse für
zulässig erklärt, prüft nichts.

## Turniertag

`GET /api/tournaments/{id}/courts` zeigt je Platz, was gerade läuft und wer
wartet. Am Platz wird über `POST /api/assignments/{id}/call|start|finish|suspend`
gearbeitet — das darf auch der Schiedsrichter, er steht dort. Disponiert wird
getrennt davon: die Reihenfolge einer Warteschlange über
`POST /api/tournaments/{id}/courts/{courtId}/queue`, eine Zusage über
`…/assignments/{id}/promise`, die Fortsetzung einer unterbrochenen Partie über
`…/resume`. Diese drei verschieben alles dahinter und gehören deshalb der
Turnierleitung, nicht der Ergebniseingabe.

Der ganze Tagesbetrieb setzt den Turniertagmodus voraus — auch das Umstellen und
das Zusagen, die im Planungsmodus nur den gerechneten Spielplan zerstören
würden, ohne inhaltlich etwas zu ändern.

Die harte Randbedingung ist, dass die Matchdauer unbekannt ist. Deshalb ist die
**Reihenfolge** auf dem Platz die Aussage, nicht die Uhrzeit: die Schätzungen
der Wartenden werden nachgezogen, sobald tatsächlich etwas passiert, und die
Warteschlange nummeriert sich lückenlos neu — „Sie sind der Dritte auf Platz 2"
ist eine Auskunft, keine Sortierhilfe. Eine Zusage („nicht vor 14 Uhr") wird
dabei nie unterlaufen, auch wenn der Platz früher frei wird.

Weil jedes überzogene Match die Warteschlange nach hinten schiebt, steht das
Finale irgendwann rechnerisch um halb zwei nachts. Das ist keine Fehlfunktion,
sondern eine Auskunft, die die Turnierleitung braucht: jedes wartende Match
trägt `withinOpeningHours`, sobald seine Schätzung nicht mehr in die
Öffnungszeiten des Platzes passt.

Aufgerufen wird nur, wer feststeht, nur auf einen freien Platz und nicht vor
einer Zusage. Eingeplant ist der ganze Baum, lange bevor die Teilnehmer bekannt
sind — am Platz wird aber kein Platzhalter ausgerufen; auf einem Platz wird ein
Match gespielt, nicht zwei; und ein früherer Aufruf als zugesagt setzt voraus,
dass zuerst die Zusage geändert wird. Das ist eine Entscheidung, keine
Nebenwirkung.
Umgekehrt wird nicht jedes Match am Platz aufgerufen: ein Nichtantreten wird
eingetragen, ohne dass jemand hingeht, und gibt den Platz sofort frei.

Eine Unterbrechung lässt die Zuweisung als Historie stehen; die Fortsetzung
kann auf einem anderen Platz stattfinden und ist dann eine eigene Zuweisung.
Erst beide zusammen erzählen, was an diesem Tag passiert ist — genau deshalb
ist die Platzzuweisung eine eigene Entität (ADR-0002). Die alte Zuweisung wird
dabei ausdrücklich abgeschlossen: bliebe sie unterbrochen, ließe sie sich ein
zweites Mal fortsetzen, und dieselbe Partie liefe auf zwei Plätzen.

Das Ergebnis wird getrennt eingetragen: der Platz ist frei, sobald die Spieler
ihn verlassen, und nicht erst, wenn jemand Zeit hatte, den Zettel auszufüllen.

## Öffentliche Ansicht

`GET /public/tournaments/{id}` liefert ohne Anmeldung Bracket, Tabellen und die
aktuelle Platzbelegung — mit `ETag` und `Cache-Control`; ein zweiter Abruf mit
`If-None-Match` bekommt 304. Wer live zusehen will, abonniert im SignalR-Hub
`/hubs/tournament` sein Turnier und wird bei jeder inhaltlichen Änderung
benachrichtigt. Der Push trägt nur Turnier-Id und ETag: geholt wird die Ansicht
über denselben Endpunkt, den auch Polling benutzt.

Die Antwort kommt aus einer eigenen Projektion, nicht aus dem Schreibmodell
(ADR-0003). Sie ist die einzige Tabelle ohne Query-Filter, und genau deshalb
entscheidet allein
`TennisTurnier.Application.PublicView.TournamentViewBuilder`, was öffentlich
wird. Keine Kontaktdaten, keine Geburtsdaten, keine internen Notizen zu
Platzsperren und keine Ids von Personen. Ein Test in
`TennisTurnier.Api.Tests` prüft die ausgelieferte Antwort gegen eine
Verbotsliste — sonst rutscht das erste zusätzliche Feld unbemerkt hinaus.

Vor der Auslosung gibt es keine öffentliche Ansicht, und eine zurückgenommene
Auslosung lässt sie wieder verschwinden.

### Zuschauen

Zusehen darf jeder, der den Link hat: `?t=<turnier-id>` führt ohne Konto direkt
auf die Zuschauerseite — kein Login davor, keine Navigation daneben, die
ohnehin nur in eine Anmeldemaske führte. Die Turnierleitung findet den Link im
Ablauf, sobald ausgelost ist, zum Kopieren und zum Teilen; anders als der
Anmeldelink trägt er kein Token, weil es an dieser Antwort nichts zu schützen
gibt.

Die Seite ist nach den Fragen geteilt, mit denen jemand herkommt, und nicht
nach den Datenstrukturen der Antwort: was jetzt am Platz läuft und was als
Nächstes kommt, der vollständige Draw, die Tabellen, alle Ergebnisse, und was
auf jedem Platz los ist. Am Handy stehen die Reiter unten am Rand, wo der
Daumen ist, die Runden untereinander statt nebeneinander und die Tabelle ohne
Satz- und Spielverhältnis — am Bildschirm dieselbe Seite in Spalten. Ein
eigener Aushangmodus für den Monitor im Vereinsheim steht daneben
(`&kiosk=1`): aus vier Metern lesbar, ohne Bedienung.

Ein Turnier lässt sich nicht suchen — es gibt keinen öffentlichen Index. Wer
den Link nicht hat, findet es nicht, und das ist die Absicht.

## Turnierformate

Ein Turniermodus ist eine geordnete Folge von Phasen. „Gruppenphase mit
anschließendem K.o." ist deshalb kein eigenes Format, sondern eine Komposition
aus einer Round-Robin- und einer K.-o.-Phase. Ein eigener Modus entsteht als neue
Vorlage — neue Phasenfolge, neue Parameter, kein Deployment.

Umgesetzt sind K.-o.-System, Round Robin und das Schweizer System.
Mitgeliefert sind `ko-single`, `group-then-ko`, `liga-round-robin` und `swiss`.
Sie lassen sich nicht ändern, aber kopieren; die Kopie gehört dem, der sie
angelegt hat, und ist frei bearbeitbar.

Beim Auslosen wird die Definition in das Turnier kopiert und eingefroren. Wer die
Vorlage danach nachschärft, verändert damit kein laufendes Turnier.

Ein Turnier nimmt nur die mitgelieferten Vorlagen und die eigenen seines
Anlegers. Sichtbar heißt nicht verwendbar: die mitgelieferten sieht jeder, und
ein Turnier hinge sonst bis zur Auslosung an einer Definition, die ein Fremder
noch ändern kann.

### Von der Gruppe in die Endrunde

Beim Auslosen entstehen alle Phasen — auch die Endrunde, für die noch niemand
qualifiziert ist. Ihre Startplätze sind zunächst Gruppenplätze („Erster der
Gruppe A"), und genau daraus steht das Bracket, während die Gruppen noch laufen.
Ist eine Gruppenphase durch, werden die Plätze besetzt: derselbe Mechanismus wie
der Übergang vom Viertel- ins Halbfinale, kein Sonderfall.

Die Setzung der Qualifikanten ist so gewählt, dass ein Gruppensieger im ersten
K.-o.-Match auf den Zweiten einer *anderen* Gruppe trifft — sonst spielten zwei,
die gerade erst gegeneinander angetreten sind, sofort wieder gegeneinander.

Punktgleichheit löst eine geordnete Kette auf: direkter Vergleich, Satz-,
Spielverhältnis, Buchholz, Los. Die Reihenfolge kommt aus der Phasendefinition,
nicht aus dem Code — sie ist eine Festlegung der Ausschreibung. Der direkte
Vergleich zählt dabei nur die Begegnungen der Punktgleichen untereinander; bei
einem Dreier-Ringschluss entscheidet das nächste Kriterium.

### Das Schweizer System

Alle spielen jede Runde, gepaart wird nach Punktestand. Das ist das einzige
Format, dessen Draw beim Auslosen unvollständig ist: nur die erste Runde steht.
Jede weitere entsteht, sobald die vorige gespielt ist — sie hängt davon ab, wie
sie ausgegangen ist, und ein Draw, der sie vorab zeigte, zeigte eine Erfindung.

Gepaart wird nach dem Dutch-System: die Tabelle zerfällt in Punktgruppen, jede
Punktgruppe in obere und untere Hälfte, gepaart wird über Kreuz. Bleibt in einer
Punktgruppe jemand übrig, steigt er in die nächste ab — von unten, denn wer in
seiner Gruppe hinten steht, soll nicht die leichtere Aufgabe bekommen.

Darüber steht die Bedingung, dass sich zwei Spieler nicht zweimal begegnen. Sie
lässt sich nicht durch Sortieren erfüllen, sondern nur suchend: gefunden wird
die Paarung, die der idealen am nächsten kommt und keine Wiederholung enthält.
Geht das innerhalb der Punktgruppen nicht auf, gilt die Regel vor der Konvention
und es wird über das ganze Feld gesucht.

Gepaart wird nach dem Stand von jetzt, ohne Vorausschau. Bei sehr vielen Runden
kann sich das Verfahren damit selbst in eine Runde manövrieren, für die es keine
wiederholungsfreie Paarung mehr gibt. Dann wird wiederholt — und die Paarung
trägt es im Namen („Runde 6 · Wiederholung"). Abzubrechen wäre die schlechtere
Antwort: es hieße, dass sich das letzte Ergebnis der vorigen Runde nicht mehr
eintragen lässt und das Turnier ohne Vor- und Rückweg steht.

Bei ungerader Teilnehmerzahl setzt jede Runde einer aus — der Letzte der
Tabelle, der noch kein Freilos hatte, und höchstens einmal pro Turnier. Das
Freilos zählt wie ein Sieg: sonst fiele zurück, wer nichts dafür kann.
Entsprechend sind bei geradem Feld höchstens *n-1* Runden möglich und bei
ungeradem *n*; mehr weist die Auslosung ab. Das ist eine Grenze der
Möglichkeit, keine Empfehlung — je näher die Rundenzahl ihr kommt, desto
wahrscheinlicher wird eine Wiederholung.

Die Rundenzahl kommt aus der Definition, ohne Angabe `ceil(log2(n))` — so viele
Runden, wie ein K.-o.-Baum desselben Feldes hätte. Die Tabelle entscheidet
Punktgleichheit zuerst über Buchholz, die Summe der Punkte aller Gegner: nach
fünf Runden stehen regelmäßig ein halbes Dutzend Spieler auf demselben
Punktestand, und ohne dieses Kriterium wäre die Tabelle weitgehend aussagelos.

Wird ein Ergebnis korrigiert, werden alle daraus entstandenen Runden
zurückgenommen und neu gepaart — mit ihnen weiterzuspielen hieße, Paarungen zu
verwenden, die niemand mehr herleiten kann. Ist eine dieser Runden schon
gespielt oder steht eine ihrer Partien am Platz, wird die Korrektur abgewiesen:
diese Kette muss von hinten aufgerollt werden.

Das gilt für beide Wege, ein Ergebnis zu ändern. Eine Korrektur durch
Überschreiben (`PUT`) wird deshalb als das ausgeführt, was sie ist: erst
zurücknehmen, dann neu eintragen. Sonst verhielten sich die beiden Wege
unterschiedlich, und nur einer von ihnen zöge die Folgen nach.

## Der Turnierbaum

Beim Auslosen entsteht der vollständige Baum — auch die späteren Runden, deren
Teilnehmer noch niemand kennt. Möglich macht das ein Summentyp: eine Seite eines
Matches ist entweder eine Meldung, „Sieger aus Match X", „Verlierer aus Match X",
„Zweiter der Gruppe B", ein Freilos oder schlicht offen.

Daraus folgt zweierlei. Die öffentliche Ansicht kann das Bracket zeigen, bevor
ein Ball gespielt ist. Und der Übergang von der Gruppenphase in die Endrunde ist
derselbe Mechanismus wie der vom Viertel- ins Halbfinale: eine Referenz wird
aufgelöst, sobald ihr Vorgänger entschieden ist.

Ein Ergebnis wird deshalb nicht nur eingetragen, sondern weitergereicht. Eine
Korrektur geht denselben Weg zurück — allerdings nur, solange das Folgematch
noch nicht gespielt ist. Sonst stünde in der nächsten Runde jemand, der laut
korrigiertem Ergebnis nie hätte antreten dürfen; diese Kette muss von hinten
aufgerollt werden.

Eine Endplatzierung im K.-o.-System weist geteilte Ränge aus: ohne Spiel um
Platz 3 gibt es zwei Dritte und danach vier Fünfte. Wer in derselben Runde
ausscheidet, hat nicht gegeneinander gespielt — ihn durchzunummerieren erfände
Plätze, die das Turnier nicht ausgespielt hat, und an Platzierungen hängen
Pokale.

Ergebnistypen gibt es von Anfang an fünf: reguläres Ende, Aufgabe, Nichtantreten,
Disqualifikation und Freilos. Bei einer Aufgabe wird der abgebrochene Satz
getrennt von den gespielten geführt — seine Spiele zählen für das
Spielverhältnis, der Satz selbst für niemanden.
