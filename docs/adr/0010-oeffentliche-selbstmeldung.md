# ADR-0010 — Öffentliche Selbstmeldung über einen Token-Link

**Status:** Accepted

## Kontext

`RegistrationOpen` existierte als Zustand, aber melden konnte nur die
Turnierleitung. Für einen Spieler gab es keinen Weg, sich selbst zu melden — und
damit blieb der Zustand eine Behauptung: „Meldung offen" hieß in Wahrheit „die
Turnierleitung tippt jetzt ab, was per E-Mail hereinkommt".

Die Anforderung des Eigentümers ist ausdrücklich: **Anmeldung über einen
öffentlichen Link, ohne Konto.** Wer sich für ein Vereinsturnier meldet, legt
dafür kein Konto an; eine Anmeldemaske vor dem Formular nähme dem Link seinen
Zweck.

## Betrachtete Optionen

**A — Meldung nur mit Konto.** Verworfen: das ist der Zustand, der ersetzt
werden soll. Die Hürde steht genau dort, wo Niederschwelligkeit gebraucht wird.

**B — Öffentlicher Endpunkt mit der Turnier-Id.** Verworfen: die Id steht in
jedem Link zur öffentlichen Live-Ansicht. Wer sie kennt, könnte in jedes fremde
Turnier melden.

**C — Öffentlicher Endpunkt mit einem eigenen, nicht zu erratenden Token.**
Gewählt.

## Entscheidung

`Tournament` trägt einen `RegistrationLink`: ein Token (128 Bit aus
`RandomNumberGenerator`, Base64Url), dazu Kapazität und Meldeschluss.

**Das Token entsteht mit dem Turnier**, nicht erst beim Öffnen der Meldung. So
lässt sich der Link vorbereiten — auf den Aushang, in die Vereinszeitung —, und
er überlebt ein `ReopenRegistration`. Ob gemeldet werden kann, entscheidet der
Zustand des Turniers, nicht die Existenz des Links.

Es wird **im Klartext geführt und nicht als Hash**. Ein Hash machte es
unmöglich, den Link ein zweites Mal anzuzeigen — und genau das ist sein Zweck.
Das Token schützt auch keine Leserechte: was hinter ihm steht, sagt nicht mehr
als der Aushang. Es autorisiert allein das Schreiben einer Meldung, und gegen
ein Token in falschen Händen gibt es die Erneuerung.

### Der Weg

1. `GET /api/tournaments/{id}/registration` → Link, Kapazität, Meldeschluss,
   Zählstand. Nur für die Turnierleitung.
2. Der Melder öffnet `…/?r=<token>`.
3. `GET /public/registrations/{token}` (anonym) → **absichtlich karg**:
   Turniername, Ortsname, Zeitraum, Disziplin, offen ja/nein, freie Plätze.
   Keine Teilnehmerliste, keine Namen — sonst wäre der Link ein Weg an der
   öffentlichen Projektion vorbei (ADR-0003).
4. `POST /public/registrations/{token}` (anonym) → `{ bestaetigungscode, status }`.
5. Die Turnierleitung nimmt an oder verschiebt auf die Warteliste — über die
   **bestehenden** Endpunkte. Eine Selbstmeldung ist keine zweite Sorte Meldung.

### Die drei Regeln, die den Endpunkt am Leben halten

Der Query-Filter aus [ADR-0009](0009-turnier-als-wurzelaggregat.md) blendet
einem Anonymen jedes Turnier aus. Daraus folgen drei Regeln, und alle drei
scheitern unsichtbar: die Meldung wäre gespeichert und der Melder bekäme 404.

1. **Das Turnier kommt ausschließlich über `FindByRegistrationTokenAsync`.** Das
   ist die einzige Abfrage mit `IgnoreQueryFilters` auf Turnieren — die
   ausdrückliche, einzige Ausnahme, an genau einer Stelle. Kein zweiter Aufruf
   im anonymen Pfad geht über den normalen Repositoryweg.
2. **Kein Neuaufbau der öffentlichen Projektion.** Er lädt das Turnier über
   denselben Filter und scheiterte aus demselben Grund. Er wäre ohnehin
   gegenstandslos: gemeldet wird nur im Zustand `RegistrationOpen`, und dort
   gibt es keine Projektion.
3. **Kein `FlushAsync`.** Es leert den ChangeTracker; die eben angelegten
   Spieler und der Teilnehmer wären danach nicht mehr Teil derselben
   Arbeitseinheit. Genau ein `SaveChangesAsync`, am Ende.

### Prüfreihenfolge

Token → `RegistrationOpen` → Meldeschluss → Disziplin passt zu Partner →
Duplikat → Kapazität.

**Unbekanntes Token, geschlossene Meldung und abgelaufener Meldeschluss liefern
denselben 404**, damit der Endpunkt kein Orakel dafür ist, welche Token es gibt.

**Ein fehlender Partner nicht.** Abweichend vom ursprünglichen Entwurf ist das
ein benannter 422: er liegt am Formular und nicht am Link, und ein 404 wäre für
einen Melder, der gerade seine Daten eingetippt hat, nicht erklärbar. Verdeckt
wird dadurch nichts, was der GET auf dasselbe Token nicht ohnehin sagt — er
nennt die Disziplin.

**Bei erschöpfter Kapazität entsteht die Meldung als Warteliste**, nicht als
Fehler. Für den Melder ist das die bessere Antwort, und die Turnierleitung
entscheidet ohnehin, wer nachrückt.

### Duplikate: idempotent statt Fehler

Dieselbe Person — gleicher Name, gleiche E-Mail, Groß-/Kleinschreibung egal —
mit einer nicht zurückgezogenen Meldung legt nichts Neues an und bekommt
denselben Bestätigungscode. Das erschlägt zwei Dinge auf einmal: den Doppelklick
auf „Absenden", der der häufigste Fall ist, und die E-Mail-Enumeration — wer
eine fremde Adresse einträgt, erfährt nicht, ob sie schon gemeldet war.

### Abbildung auf Spieler

Ein oder zwei `Player` mit `PlayerContact(email, telefon, null)`, dazu
`Participant.Single`/`Team` und ein `TournamentEntry` mit
`Origin = SelfService`. **Kein Geburtsdatum:** es wird für nichts gebraucht, und
was nicht erhoben wird, ist weder zu schützen noch zu löschen.

Vorher wird nach einem bestehenden Spieler mit gleichem Namen und gleicher
E-Mail gesucht — sonst legte derselbe Mensch bei jedem Turnier einen neuen an.
Die Erkennung ist unscharf und weiß das: zwei Namensvettern mit derselben
Adresse werden zusammengeführt, zwei Adressen desselben Menschen bleiben
getrennt. Der zweite Fehler ist der billigere, deshalb ist die Bedingung streng.

### Missbrauch

`AddRateLimiter` auf den anonymen Endpunkten, 20 Anfragen je 10 Minuten und IP.
Dazu Kapazität, Meldeschluss, Zustandsautomat und die Idempotenz.

**Kein CAPTCHA:** Niederschwelligkeit ist der Zweck des Links.

Begrenzt wird ausschließlich hier. Alles unter `/api` steht hinter der
Anmeldung; wer dort zu viel anfragt, hat ein Konto, das man entziehen kann. Der
Melder ohne Konto hat keines — und eine Schranke am Turniertag träfe den
Betrieb.

## Konsequenzen — der Preis

**Personenbezogene Daten von Menschen ohne Konto.** Es werden Namen und
E-Mail-Adressen erhoben. Dagegen stehen: der Datenschutzhinweis im Formular, der
Bestätigungscode als Rückzugsweg, und die Datensparsamkeit der öffentlichen
Projektion — öffentlich sichtbar ist ausschließlich der Anzeigename im
Spielplan.

**Was fehlt, ist benannt und nicht vergessen:**

- **Keine E-Mail-Verifikation.** Wer eine fremde Adresse einträgt, meldet damit
  jemand anderen. Die Turnierleitung sieht die Adresse und kann nachfragen; mehr
  gibt es in diesem Stand nicht.
- **Keine Aufbewahrungsfrist.** Kontaktdaten aus Selbstmeldungen bleiben
  unbegrenzt stehen. `EntryOrigin` und `RegisteredAt` sind die Felder, an denen
  eine Löschregel später ansetzen wird — sie stehen deshalb schon jetzt da.

Beides steht in der Roadmap als offener Punkt. Das ist eine Entscheidung, kein
Versäumnis.

**Das Token in Protokollen.** Es steht in der Adresszeile. Dagegen stehen
`Referrer-Policy: no-referrer` auf jeder Antwort, ein `NotFoundException`-Zweig
ohne Kennung in der Meldung — das Token steht in keinem `ProblemDetails` — und
die Erneuerung als Notausgang.
