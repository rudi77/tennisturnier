# ADR-0012 — Das Turnier ist eine Gruppe: Mitgliedschaft statt anonymer Selbstmeldung

**Status:** Accepted

**Ersetzt:** [ADR-0010](0010-oeffentliche-selbstmeldung.md)

## Kontext

ADR-0010 löste ein echtes Problem: „Meldung offen" hieß vorher „die
Turnierleitung tippt ab, was per E-Mail hereinkommt". Der Token-Link hat das
beendet. Der Preis war ein zweiter Personenbegriff neben dem Konto — ein
`Player` ohne Konto, eine E-Mail-Adresse ohne Verifikation, ein
Bestätigungscode als einziger Rückzugsweg.

Der Eigentümer richtet MATCHDAY neu aus: **ein Turnier soll funktionieren wie
eine WhatsApp-Gruppe.** Wer dazugehört, sieht den ganzen Verlauf — Meldungen,
Draw, Spielplan, Ergebnisse. Man kommt hinein über einen geteilten Link oder
über eine persönliche Einladung, und man ist danach ein Mitglied und kein
Formulareintrag.

Dazu kamen vier Lücken, die er beim Ausprobieren benannt hat: registrieren ging
nicht, der Weg über Google war unsichtbar, „Abmelden" meldete nur lokal ab, und
vor der Anwendung stand eine Startseite mit zwei Knöpfen, die niemand braucht.

Diese Neuausrichtung entwertet ADR-0010 nicht rückwirkend — sie ändert die
Frage. ADR-0010 fragte: *wie meldet sich jemand ohne Konto?* Diese ADR fragt:
*wie gehört jemand dazu?*

## Betrachtete Optionen

**A — Anonyme Meldung behalten und Mitgliedschaft danebenstellen.** Verworfen.
Zwei Wege in dasselbe Turnier heißen zwei Personenbegriffe, zwei
Berechtigungsmodelle und zwei Datenschutzregime — und in der Oberfläche zwei
Erklärungen für dasselbe. Der Bestätigungscode existierte nur, weil der Melder
keine Identität hatte; mit Konto ist er überflüssig.

**B — Nur noch Einladung durch die Turnierleitung.** Verworfen. Damit stürbe
genau die Niederschwelligkeit, die ADR-0010 gewonnen hat: der Aushang im
Vereinsheim mit einem Link darauf ist der Normalfall, nicht die Ausnahme. Der
Eigentümer hat zweimal ausdrücklich bestätigt, dass die Selbstanmeldung über
den Link bleiben muss.

**C — Beitritt über den Link, aber mit Konto; zusätzlich persönliche
Einladung.** Gewählt.

## Entscheidung

### Mitgliedschaft ist eine Rolle

`Role.Member` im Tournament-Scope, kein eigenes Entity. Die Sichtbarkeit eines
Turniers hängt schon heute vollständig an den Rollenzuweisungen — der
Query-Filter im `TennisTurnierDbContext` fragt `SeesEverything ||
TournamentIds.Contains(t.Id)`. Ein neuer Wert im Enum fällt dort ohne
Änderung hinein. In der Rechtematrix steht `[Role.Member] = []`: ein Mitglied
darf nichts tun, es sieht nur — und das kommt aus dem Filter, nicht aus der
Matrix.

Rollen liegen als String in der Datenbank (`HasConversion<string>`), ein neuer
Enum-Wert braucht deshalb keine Migration.

### Der Beitrittslink ist der alte Anmeldelink

`?r=<token>` bleibt, samt Erneuerung — Aushänge, die schon hängen, führen
weiter zum richtigen Turnier. Was sich ändert, ist, was hinter dem Link
passiert: `GET`/`POST /api/join/{token}` verlangen eine Anmeldung. Wer kein
Konto hat, wird zum Aussteller geleitet, legt dort eines an und kommt zurück
— **die Selbstanmeldung bleibt möglich, nur nicht mehr anonym.**

Beim Beitritt entscheidet der Beitretende, ob er mitspielt. Wer nur zusehen
will — der Partner ohne eigene Meldung, der Vereinskollege — tritt bei, ohne
zu melden. Das ist der Fall, den es vorher gar nicht gab.

### Der Weg über den Aussteller wird gestellt

Die verwahrte Route (`sessionStorage`, wiederhergestellt beim Rücksprung) ist
das Stück, ohne das der ganze Weg nicht trägt: der `redirect_uri` ist die
Wurzel, die Abfrage `?r=…` ginge über die Anmeldung verloren. Sie wird vor dem
Redirect verwahrt und beim Rücksprung zurückgeschrieben — genau einmal, sonst
löschte der zweite Lauf unter `<StrictMode>` sie wieder weg.

### Spieler und Konto wachsen zusammen

`Player.UserAccountId` (`Guid?`, eindeutig) verbindet die beiden bisher
getrennten Welten. Beim Beitritt wird zuerst über das Konto gesucht, sonst
über Name und Konto-Adresse — ein per CSV importierter Spieler wird dann
**adoptiert** statt verdoppelt. Importierte Spieler bleiben kontenlos; die
Verknüpfung entsteht, wenn der Mensch dazu beitritt.

### Einladungen überleben das fehlende Konto

`Invitation` (Turnier, Adresse in Kleinschreibung, Rolle) hält eine Zusage für
jemanden, den es in der Anwendung noch nicht gibt. `RoleService.GrantAsync`
wirft deshalb nicht mehr bei unbekannter Adresse, sondern legt eine Einladung
an; `InvitationRedemption` löst sie beim ersten Login ein — dasselbe Muster wie
`OrganizerBootstrap`. Bis dahin steht sie in der Mitgliederliste als
„eingeladen, noch nie angemeldet" und lässt sich zurücknehmen.

**Es wird keine E-Mail verschickt.** Es gibt keinen Mail-Adapter, und einen
einzuführen, um eine Einladung zuzustellen, wäre eine eigene Entscheidung mit
eigenem Betriebsaufwand. Die Turnierleitung teilt den Beitrittslink selbst —
über denselben Kanal, über den sie ohnehin mit ihren Leuten redet.

### Privat ist die Vorgabe

`Tournament.IsPublic`, Vorgabe `false`. Die öffentliche Projektion liefert nur,
wer öffentlich ist — oder wer selbst dazugehört. Ein privates Turnier
antwortet dem Fremden mit 404, nicht mit 403: die Existenz eines Turniers ist
selbst eine Auskunft (ADR-0004).

Damit fällt auch die Vorratshaltung der Zuschaueransicht. `Cache-Control:
public, max-age=15` war richtig, solange die Ansicht für jeden bestimmt war;
mit einer Sichtbarkeit, die sich umlegen lässt, wäre sie ein Leck — wer zusieht,
während zugemacht wird, sähe weiter zu, und ein gemeinsamer Zwischenspeicher
zeigte die Ansicht sogar jemandem, der sie nie geladen hat. Jetzt steht dort
`no-cache`: jedes Mal nachfragen, den Rumpf spart weiterhin der ETag.

### Abmelden meldet ab

`signoutRedirect()` statt `removeUser()`. Der alte Kommentar sprach vom
Vereinsrechner im Turnierbüro, auf dem eine überlebende Sitzung praktisch sei.
Mit persönlichen Konten ist genau das der Fehler: „Abmelden" hieß, dass der
nächste Aufruf wortlos denselben Menschen zurückbrachte.

### Die Startseite entfällt

Wer nicht angemeldet ist und keinen Zuschauerlink geöffnet hat, wird geleitet
— ohne Zwischenschritt. Die Maske des Ausstellers ist der Einstieg, und dort
stehen auch der Weg über Google und der zum Registrieren. Eine eigene
Anmeldemaske in der Anwendung wäre eine zweite Stelle, an der Identität
aussieht, als entstünde sie hier (ADR-0007).

## Konsequenzen

**Die Hürde ist wieder da, und das ist beabsichtigt.** Wer melden will,
braucht ein Konto. Dafür gibt es dahinter keinen zweiten Personenbegriff mehr,
keinen Bestätigungscode, keine unverifizierte Adresse als einzige Kennung — und
die Frage „darf der das sehen?" hat genau eine Antwort.

**Was stirbt:** `Application/Registration/` samt `/public/registrations/*`, der
Bestätigungscode in Entity, DTO und Spalte, die anonyme Meldemaske — und der
Rate-Limiter. Er stand ausschließlich vor dem öffentlichen Meldeendpunkt; der
Beitritt ist authentifiziert, und alles unter `/api` steht hinter einem Konto,
das man entziehen kann.

**Der Partner im Doppel braucht weiterhin kein Konto.** Wer sich zu zweit
meldet, nennt den Partner namentlich. Der bleibt ein kontenloser `Player`, bis
er selbst beitritt — dann wird er verknüpft. Ein Paar-Zustandsautomat
(„Partner hat bestätigt") wäre die saubere Lösung und ist bewusst nicht gebaut:
er kostet mehr, als er am Vereinsturnier einbringt.

**Der SignalR-Hub bleibt ungegated.** Er trägt nur Turnier-Id und ETag; die
Daten holt der Client über die Projektion, und die ist gegated. Ein Fremder
erführe darüber, dass sich an einem Turnier etwas ändert, dessen Id er kennt —
mehr nicht.

**Selbst austreten geht nicht.** Wer eine Gruppe verlassen will, muss die
Turnierleitung fragen. Das ist eine Lücke, kein Entwurf; sie steht in der
Roadmap.

**Bestehende Keycloak-Instanzen ziehen nicht automatisch nach.** Der
Realm-Import greift nur beim ersten Start gegen eine leere Datenbank.
`registrationAllowed` und `post.logout.redirect.uris` müssen dort von Hand in
der Admin-Konsole gesetzt werden — im README steht, wo.
