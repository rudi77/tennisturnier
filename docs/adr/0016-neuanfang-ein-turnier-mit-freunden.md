# ADR-0016 — Neuanfang: ein Turnier mit Freunden, geführt von einem Agenten

**Status:** Accepted

## Kontext

MATCHDAY ist in fünfzehn Milestones zu einer Turnierplattform für Vereine
gewachsen: Plätze und Platzzeiten, ein Spielplan-Solver, eine Turniertag-Queue,
fünf Turnierformate, Formatvorlagen, Rollen, ein externer Identity Provider,
Mitgliedschaft, Feed, Profil, Kontaktgraph, Verabredungen. Rund 35.000 Zeilen
Backend, 29.000 Zeilen Oberfläche, zwölf Screens.

Gebraucht wird davon ein Bruchteil. Der tatsächliche Anwendungsfall ist: **ein
Turnier anlegen, Freunde eintragen, spielen, und alle sehen auf dem Handy, wie
es steht.** Alles andere ist für einen Verein gebaut, den es nicht gibt.

Dazu kommt eine zweite Beobachtung. Die Oberfläche besteht zum größten Teil aus
Formularen, Wizards und Navigation — aus dem Weg *zu* einer Handlung, nicht aus
der Handlung selbst. Ein Sprachmodell mit Werkzeugen nimmt genau diesen Teil
ab: „Leg ein Doppelturnier am Samstag an, Rudi, Max, Anna und Tom spielen mit"
ist ein Satz, kein Wizard.

## Entscheidung

**Ein frischer Baum unter `matchday/`**, der genau vier Dinge kann, und ein
Agent als primäre Bedienoberfläche. Der alte Baum bleibt stehen, bis der neue
diese vier Dinge beherrscht, und wird dann entfernt.

### Die vier Dinge

1. **Ein Turnier** mit Name, Datum, Ort, Modus und Satzformat. Zwei Modi:
   K.o. und Jeder gegen jeden. Keine Phasenverkettung, kein Schweizer System,
   keine Vorlagen.
2. **Teilnehmer** als Namen. Kein Spielerstamm, keine Kontaktdaten, keine
   Setzliste, keine Warteliste.
3. **Ergebnisse** und das, was daraus folgt: das Bracket oder die Tabelle.
4. **Ein Link zum Mitschauen**, der sich ohne Neuladen aktualisiert.

### Keine Konten: das Doodle-Modell

Wer ein Turnier anlegt, bekommt einen **Verwalterlink** mit einem geheimen
Token. Wer ihn hat, darf alles an diesem Turnier. Alle anderen bekommen den
**Mitschau-Link**. Es gibt keinen Identity Provider, keine Rollen, keine
Mitgliedschaft.

Damit „meine Turniere" ohne Anmeldung funktioniert, führt der Browser eine
zufällige Kennung, die beim Anlegen als Eigentümer eingetragen wird. Sie ist
kein Konto — ein neuer Browser kennt die Turniere nicht, der Verwalterlink
kennt sie immer.

Der Preis: ein weitergegebener Verwalterlink ist nicht zurückholbar, nur
rotierbar. Für einen Freundeskreis ist das in Ordnung. Sobald Fremde oder
Vereine im Spiel sind, kippt diese Entscheidung zurück zu Konten — das ist
dann ein neues ADR, kein Schalter.

### Der Agent führt, die Domäne entscheidet

Die Oberfläche ist ein Gespräch mit einem Eingabefeld unten, wahlweise per
Sprache. Der Agent hat **Werkzeuge**, die eins zu eins die Anwendungsfälle
sind: Turnier anlegen, ändern, Teilnehmer hinzufügen und entfernen, auslosen,
Ergebnis eintragen und zurücknehmen, Links geben, Turnier löschen.

Der Agent hat **Widgets** aus einem festen Katalog: Turnierkarte,
Teilnehmerliste, Bracket, Tabelle, Ergebnismaske, Teilen. Er erzeugt kein
HTML; er benennt Widget und Daten. Jedes Werkzeugergebnis trägt sein Widget
selbst, so dass die Darstellung keine Entscheidung des Modells ist.

Drei Regeln:

- **Der Agent entscheidet nie fachlich.** Auslosung, Satzprüfung und Tabelle
  kommen aus der Domäne. Ein Ergebnis, das die Domäne abweist, ist abgewiesen,
  und der Agent sagt warum.
- **Widgets handeln am Modell vorbei.** „Annehmen", „Auslosen", ein Ergebnis
  eintragen — das sind Knöpfe im Widget, die direkt die HTTP-API rufen. Das
  Modell ist für das Offene da, nicht für den vierzigsten Klick am Platz.
- **Unumkehrbares fragt.** Auslosen, Löschen und Auslosung zurücknehmen bestätigt
  der Agent erst nach ausdrücklicher Zustimmung im Gespräch, oder der Mensch
  drückt den Knopf im Widget selbst.

Die Mitschau-Ansicht ist reine Oberfläche ohne Modell. Zuschauer chatten
nicht, und pro Zuschauer ein Modellaufruf wäre unbezahlbar.

Ohne konfigurierten Modellschlüssel läuft die Anwendung trotzdem: die Widgets
sind vollständig, nur das Eingabefeld ist stumm.

### Technik

- **.NET 10, ein Prozess.** `Matchday.Domain` (reine Logik, ohne Pakete) und
  `Matchday.Server` (Minimal API, Agent, SQLite, liefert die Oberfläche aus).
  Keine hexagonale Ringstruktur mit Adapterprojekten: bei zwei Projekten und
  einem Datenspeicher wäre das Overhead ohne Ertrag — genau der Fall, den
  ADR-0005 selbst benennt.
- **SQLite als Dokumentspeicher.** Ein Turnier ist eine JSON-Zeile mit
  Versionszähler. Keine Migrationen, kein ORM, kein Mapping. Ein Turnier hat
  zwanzig Teilnehmer und dreißig Matches; es passt in eine Zeile und wird
  immer als Ganzes gelesen und geschrieben.
- **Live per Server-Sent Events**, nicht SignalR. Es fließt nur in eine
  Richtung, und ein `EventSource` braucht keine Bibliothek.
- **Claude Opus 5** über das offizielle C#-SDK mit adaptivem Thinking, in einer
  selbst geschriebenen Werkzeugschleife: jeder Werkzeugaufruf wird als Ereignis
  an die Oberfläche gestreamt, und die Widgets entstehen aus den Ergebnissen.
- **Vite und React** für die Oberfläche, ohne Router, ohne Auth-Bibliothek,
  ohne SignalR-Client. Sprache über die Web Speech API des Browsers.

### Was aus dem alten Baum übernommen wird

Wörtlich oder nahezu wörtlich, samt Tests:

- Die Satzvalidierung (`Score`, `SetScore`, `MatchFormat`, `FinalSetMode`),
  einschließlich Aufgabe, Nichtantreten und Freilos.
- Der Bracket-Aufbau: Zweierpotenz, Setzreihenfolge, Freilose an die ersten
  Positionen, Rundenbezeichnungen.
- Der Round-Robin-Paarungsplan und die Tabellenlogik in ihrer einfachen Form
  (Siege, Satzdifferenz, Spieldifferenz).
- Die Idee der datensparsamen Projektion aus ADR-0003: die Mitschau-Ansicht
  ist eine eigene Sicht ohne Token.

Nicht übernommen: Phasen, Qualifikation, Tiebreaker-Ketten, Scheduling, Queue,
Rollen, OIDC, Mitgliedschaft, Feed, Profil, Kontaktgraph, Verabredungen,
CSV-Import, Formatvorlagen.

## Verworfen

**Den bestehenden Baum zurückschneiden.** Der Query-Filter, die Rollen, der
Verein-zu-Turnier-Umbau und die Phasenmaschine haben Fäden durch alle Schichten
gezogen. Sie zu lösen kostet mehr als 2.000 Zeilen neu zu schreiben, und am Ende
stünde ein Baum, der seine eigene Geschichte nicht mehr erklären kann.

**Den Agenten als Zusatz an die alte Oberfläche hängen.** War die erste
Empfehlung. Sie setzt voraus, dass die alte Oberfläche bleiben soll — und das
soll sie nicht.

**Chat als einzige Bedienung, auch am Platz.** Ergebnisse trägt man tippend
schneller ein als sprechend, und ein Modellaufruf pro Ergebnis ist eine
Wartezeit pro Ergebnis. Deshalb sind die Widgets selbst bedienbar.

**Konten behalten.** Sie sind die richtige Antwort auf eine Frage, die dieser
Anwendungsfall nicht stellt.

## Konsequenzen

**Positiv.** Ein Wochenende statt eines Umbaus. Eine Domäne, die man an einem
Nachmittag liest. Kein zweiter Dienst auf Railway. Eine Bedienung, die mit
einem Satz beginnt.

**Negativ, ehrlich benannt.** Jede Eingabe im Gespräch kostet einen
Modellaufruf, Sekunden und Geld. Das Verhalten des Agenten ist nicht durch
Unit-Tests abgesichert, nur die Werkzeuge dahinter sind es. Ein Verwalterlink
ist ein Geheimnis, das der Browser trägt. Und was der alte Baum konnte — ein
Verein mit dreißig Spielern an zwei Tagen auf sechs Plätzen — kann der neue
nicht und soll es nicht.

**Für die ADRs davor.** ADR-0001, 0002, 0007, 0012, 0013, 0014 und 0015
beschreiben den alten Baum und gelten dort weiter, bis er entfernt wird. Für
`matchday/` gelten sie nicht. ADR-0003 (Projektion), ADR-0006 (SQLite) und
ADR-0011 (Satzformat am Turnier) gelten in vereinfachter Form weiter.
