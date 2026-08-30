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

| Datei | Meilenstein / ADR | Was geprüft wird |
| --- | --- | --- |
| `mitgliedschaft.spec.ts` | M12, ADR-0012 | Beitritt mit und ohne Meldung, zweimal derselbe Link, ohne Anmeldung, bei geschlossener Meldung; die drei Rollen berufen und wieder entziehen |
| `meldungen.spec.ts` | M2, M10→M12 | annehmen, Warteliste, zurückziehen, Setzposition; Kapazität und der volle Fall; Liste aus der Datei samt Doppelerkennung |
| `formate.spec.ts` | M3, M5, M8, M11, ADR-0001 | alle vier Modi vom Anlegen bis zum Endstand; Baum nur bei K.-o., Tabelle nur wo gezählt wird, bei der Komposition je Phase |
| `ergebnisse.spec.ts` | M3, ADR-0011 | glattes Ergebnis und Propagierung; die Prüfung gegen das Satzformat *vor* dem Absenden; Aufgabe mitten im Satz, Nichtantreten; Korrektur; Freilos |
| `sichten.spec.ts` | ADR-0003, ADR-0004, ADR-0012, ADR-0013 | was Mitglied, Schiedsrichter und Fremder sehen und dürfen; Profil 404 für Fremde und 200 für Mitspieler; privat/öffentlich in beide Richtungen |

## Was noch fehlt

Ehrlich benannt, damit niemand die Abdeckung für vollständig hält:

- **Spielplan und Turniertag** (M6, M7, ADR-0002). Der schnelle Durchgang deckt
  ihn in `e2e/spielplan.spec.ts` ab — Vorschlag, Übernehmen, Verwerfen, Queue
  mit Aufrufen/Start/Platz frei. Was fehlt, sind die Kombinationen: Umhängen
  per Drag & Drop, Pause und Fortsetzen, Platzzeiten über mehrere Tage.
- **Feed, Profil, Mitspieler, Verabredungen im Lebenszyklus** (M13–M15). Der
  glückliche Weg steht in `e2e/soziales.spec.ts`; es fehlen Absage, Zurückziehen
  einer Runde, die zweite Zeile nach einer Ergebniskorrektur und die Rechte am
  fremden Beitrag.
- **Doppel** in der Breite: gemeldete Paare gegen von der Turnierleitung
  gestellte Teams, quer durch Meldung, Draw und Ergebnis.
- **Die öffentliche Ansicht** in ihren Reitern, der Aushangmodus und der Push
  über SignalR.

## Was die Abnahme gefunden hat

Beim Bau, nicht im Nachhinein — das ist ihr Zweck:

1. **Die Aufgabe mitten im Satz ließ sich nicht eintragen.** Die Maske schickte
   den abgebrochenen Satz als gespielten mit; der Server wies das Ergebnis zu
   Recht ab. Behoben — der letzte, unfertige Satz geht jetzt als
   `abandonedSet`, wie der Vertrag es vorsieht.
2. **Der Kasten für die Teilnehmerliste versprach zu viel.** „Wer schon im Feld
   steht, wird übersprungen" gilt nur für Zeilen mit Adresse; ohne Adresse
   entsteht bewusst ein zweiter Eintrag. Der Text sagt das jetzt.
3. **`matches.clearResult` ruft niemand.** Der Client bringt den Aufruf samt
   Test mit, keine Maske benutzt ihn — korrigiert wird durch Überschreiben.
   Festgehalten, nicht entfernt: das ist eine Entscheidung für den Eigentümer.
