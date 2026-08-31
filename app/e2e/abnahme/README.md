# Abnahme

Der lange Lauf über die Anwendung, durch dieselbe Oberfläche, die ein Mensch
bedient. Er ist wiederholbar:

```bash
docker compose up -d keycloak      # in der Repo-Wurzel, einmal
cd app && npm run abnahme
```

Er startet API und Oberfläche selbst (`playwright.config.ts`) und legt seine
Konten über die Verwaltungsschnittstelle des Ausstellers an — ein Konto kostet
damit rund 300 ms statt fünf Sekunden Registrierungsmaske. Der Weg über die
Maske bleibt trotzdem geprüft: `e2e/beitritt.spec.ts` geht ihn zu Fuß.

## Wozu, und wozu nicht

Dass die **Regeln** stimmen, sichern die Tests im Backend — über 1100, gegen
echtes SQLite über echtes HTTP. Sie hier zu wiederholen wäre doppelte
Buchführung.

Diese Abnahme prüft etwas anderes: dass die Regeln **auch vorfindbar** sind.
Dass die Maske nichts anbietet, was der Server abweist. Dass zwei Wege zum
selben Ziel nicht zwei Ziele ergeben. Und dass die Kombinationen tragen, die
im Einzeltest je für sich stehen.

Deshalb: Aufbau über die API, Handlung über die Oberfläche.

## Was abgedeckt ist

38 Durchläufe, rund dreieinhalb Minuten.

| Datei | Meilenstein / ADR | Was geprüft wird |
| --- | --- | --- |
| `mitgliedschaft.spec.ts` | M12, ADR-0012 | Beitritt mit und ohne Meldung, zweimal derselbe Link, ohne Anmeldung, bei geschlossener Meldung; die drei Rollen berufen und wieder entziehen |
| `meldungen.spec.ts` | M2, M10→M12 | annehmen, Warteliste, zurückziehen, Setzposition; Kapazität und der volle Fall; Liste aus der Datei samt Doppelerkennung |
| `formate.spec.ts` | M3, M5, M8, M11, ADR-0001 | alle vier Modi vom Anlegen bis zum Endstand; Baum nur bei K.-o. und bei der Komposition je Phase; die Tabelle in jedem Modus mit Zeilen |
| `ergebnisse.spec.ts` | M3, ADR-0011 | glattes Ergebnis und Propagierung; die Prüfung gegen das Satzformat *vor* dem Absenden; Aufgabe mitten im Satz, Nichtantreten; Korrektur durch Überschreiben; Freilos |
| `doppel.spec.ts` | ADR-0001, M11 | gemeldete Paare mit Partnerfeldern; von der Turnierleitung gestellte Teams samt Auflösen; das Paar im Draw |
| `spielplan.spec.ts` | M6, M7, ADR-0002 | Vorschlag rechnen, übernehmen, verwerfen; Umhängen per Drag & Drop; Turniertag mit Aufrufen, Start und Platz frei; die Unterbrechung samt ihrer Lücke |
| `sozial.spec.ts` | M13–M15, ADR-0014, ADR-0015 | die zweite Zeile nach einer Ergebniskorrektur; Rechte am eigenen und fremden Beitrag; eine Runde vorschlagen, absagen, zurückziehen; einladen nur, mit wem man gespielt hat |
| `sichten.spec.ts` | ADR-0003, ADR-0004, ADR-0012, ADR-0013 | was Mitglied, Schiedsrichter und Fremder sehen und dürfen; Profil 404 für Fremde und 200 für Mitspieler; privat/öffentlich in beide Richtungen |
| `oeffentlich.spec.ts` | M4, ADR-0003 | die Seite ohne Konto samt Reitern; keine Kontaktdaten; Aushangmodus; die Ansicht vor der Auslosung |

## Was bewusst offen bleibt

- **Der Push über SignalR.** Er trägt nur Kennung und ETag; geprüft ist, dass
  die Ansicht nach einem Neuladen stimmt, nicht dass sie von selbst nachzieht.
- **Mehrtägige Turniere** mit Platzzeiten je Tag.
- **Der Schwarm**: viele Meldungen, viele Plätze, lange Turniertage. Die
  Abnahme prüft Verhalten, nicht Last.

## Was die Abnahme gefunden hat

Beim Bau, nicht im Nachhinein — das ist ihr Zweck:

1. **Die Aufgabe mitten im Satz ließ sich nicht eintragen.** Die Maske schickte
   den abgebrochenen Satz als gespielten mit; der Server wies das Ergebnis zu
   Recht ab. Behoben — der letzte, unfertige Satz geht jetzt als
   `abandonedSet`, wie der Vertrag es vorsieht.
2. **Der Kasten für die Teilnehmerliste versprach zu viel.** „Wer schon im Feld
   steht, wird übersprungen" gilt nur für Zeilen mit Adresse; ohne Adresse
   entsteht bewusst ein zweiter Eintrag. Der Text sagt das jetzt.
3. **Eine unterbrochene Partie verschwindet vom Platzbrett.** „Pause" gibt den
   Platz frei — ausdrücklich so gewollt —, aber die unterbrochene Zuweisung
   steht danach in keiner Schlange, und der Knopf „Fortsetzen", den
   `QueueBoard` kennt, kann nie erscheinen. Über die API geht es weiter. Nicht
   behoben: die Fortsetzung gehörte außerhalb der Plätze, etwa als eigener
   Abschnitt „unterbrochen" — das ist ein Entwurf und keine Reparatur.
4. **Ein öffentliches Turnier vor der Auslosung sagt „nicht öffentlich".** Die
   Projektion entsteht erst mit dem Draw, und der Zuschauer bekommt dieselbe
   404 wie bei einem privaten Turnier. Bei einem offenen Turnier verriete
   „noch nicht ausgelost" nichts — unterscheiden kann die Oberfläche die
   beiden Fälle heute nur nicht.
5. **`matches.clearResult` ruft niemand.** Der Client bringt den Aufruf samt
   Test mit, keine Maske benutzt ihn — korrigiert wird durch Überschreiben.

Und zwei Fehler in der Abnahme selbst, die sie an sich selbst gefunden hat:
ein Prädikat, das den Vorteilssatz für beendet erklärte, und eine Zusicherung
auf „Reiter nicht vorhanden", die erfüllt war, bevor die Daten geladen waren —
grün, ohne etwas zu prüfen.
