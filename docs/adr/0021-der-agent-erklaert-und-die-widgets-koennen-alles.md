# ADR-0021 — Der Agent erklärt auch, und jeder Weg führt durch

**Status:** Accepted

## Kontext

ADR-0016 hat den Agenten als primäre Bedienoberfläche gewählt und die Widgets
als die Anzeige daneben, die „direkt die HTTP-API" ruft — damit die Anwendung
auch ohne Modellzugang benutzbar bleibt. In der Praxis sind daraus zwei
Halbheiten geworden:

1. **Der Agent konnte nur handeln.** Die Anweisungen beschrieben die Werkzeuge
   und schärften ein, nie selbst fachlich zu entscheiden. Was MATCHDAY kann, wie
   der Ablauf aussieht, wie „jeder gegen jeden" funktioniert, was ein
   Match-Tiebreak ist — davon stand nichts darin. Gefragt wird es trotzdem, und
   zwar genau von denen, die die Anwendung zum ersten Mal sehen. Ein Modell, das
   darauf aus seinem Allgemeinwissen antwortet, erzählt Tennisregeln richtig und
   Hausregeln falsch: eine Setzliste, die es hier nicht gibt, ein Satzformat je
   Match, das die Domäne nicht kennt.
2. **Die Widgets konnten nicht alles.** Anlegen ging nur mit einem Namen — Modus,
   Disziplin, Datum, Ort und Satzformat gab es nur im Gespräch. Den Rahmen
   ändern, die Auslosung zurücknehmen, ein Turnier löschen: gar nicht. Ohne
   Modellzugang — der Fall, für den die Widgets gebaut wurden — endete der Weg
   also nach dem zweiten Schritt.

## Entscheidung

**Beide Wege führen durch, und der Agent gibt auch Auskunft.**

### Der Agent erklärt

Die Anweisungen sagen jetzt zwei Aufgaben: ein Turnier führen und Fragen dazu
beantworten — zur Anwendung, zum Ablauf, zu den Modi, zum Satzformat und zu den
Spielregeln. Das Wissen dafür steht in `Knowledge` und hängt unter den
Anweisungen: was MATCHDAY kann und was ausdrücklich nicht, der Ablauf von der
Idee bis zur Platzierung, beide Modi samt Freilosen und Tabellensortierung, das
Satzformat mit allen drei Varianten des letzten Satzes, die Arten von Ergebnis,
die Tenniszählung in vier Zeilen und die Grenzen, an die jemand stößt.

Es steht in den Anweisungen und nicht in einem Werkzeug. Eine Frage wie „wie
funktioniert der Match-Tiebreak?" hat keine Wirkung auf ein Turnier; eine Runde
zum Server und zurück würde die Antwort nur langsamer machen und in der
Oberfläche ein Widget auslösen, das nichts zu zeigen hat.

Der Preis ist ein längerer Vorspann bei jeder Anfrage. Er ist klein gegenüber
dem Nutzen, und er wird von der Prompt-Zwischenspeicherung getragen, weil er
sich von Anfrage zu Anfrage nicht ändert — anders als der Kontext, der weiter je
Nachricht mitkommt.

Damit dieses Wissen nicht still veraltet, hängt ein Test daran: Zu jedem Wert,
den die Domäne anbietet — jeder Modus, jede Disziplin, jede Variante des letzten
Satzes —, muss etwas im Wissen stehen. Kommt einer dazu, fällt der Test um.

### Jeder Weg führt durch

Die Widgets können jetzt jeden Schritt, den auch der Agent kann: anlegen mit
Name, Datum, Ort, Disziplin, Modus und Satzformat; den Rahmen ändern; Teilnehmer
und Teams eintragen und streichen; auslosen und die Auslosung zurücknehmen;
Ergebnisse eintragen, ändern und löschen; teilen; löschen. Ein Turnier lässt
sich vollständig im Gespräch führen, vollständig über die Widgets, oder gemischt
— beide rufen dieselben `TournamentActions`, und der Live-Strom bringt jede
Änderung auf den anderen Weg, ohne dass jemand neu lädt.

Das Formular dafür gibt es einmal und wird zweimal benutzt: beim Anlegen
zusammengeklappt hinter „Mehr einstellen", in den Einstellungen offen. Was nach
der Auslosung fest ist, steht dort gesperrt und mit dem Grund daneben — ein Feld,
das nichts annimmt, ohne zu sagen warum, ist schlimmer als keines.

Was wehtut, steht hinter einer Rückfrage: Auslosung zurücknehmen und Turnier
löschen fragen nach, bevor sie es tun. Der Agent tut dasselbe; dort ist es eine
Regel in den Anweisungen, hier ein zweiter Klick.

## Konsequenzen

- Ohne Modellzugang ist MATCHDAY vollständig benutzbar, nicht nur lesbar. Der
  Satz über dem stummen Eingabefeld sagt weiterhin, was fehlt.
- Die Anweisungen sind kein reines Handlungsregelwerk mehr. Sie müssen mit der
  Domäne mitwandern: Ändert sich eine Regel, ändert sich der Text. Der Test hält
  nur die Begriffe fest, nicht ihre Richtigkeit.
- Der Agent darf beim Erklären länger antworten als die ein bis drei Sätze, die
  für Handlungen gelten. Listen bleiben ihm dort erlaubt, wo ein Widget sie nicht
  ohnehin zeigt.
- Zwei Wege heißen zwei Stellen, an denen eine neue Handlung auftauchen muss.
  Das ist die Kehrseite und ausdrücklich gewollt: Die Widgets sind kein
  Notausgang, sondern ein Weg.

## Verworfene Optionen

**Ein Werkzeug `explain`, das die Auskunft liefert.** Eine Runde mehr, ein
Widget ohne Inhalt, und das Modell müsste erst merken, dass es fragen soll. Für
Wissen, das sich je Anfrage nicht ändert, ist der Vorspann der richtige Ort.

**Eine Hilfeseite in der Oberfläche.** Sie beantwortet die Frage nicht, die
gerade gestellt wird, sondern die, die jemand beim Schreiben der Seite erwartet
hat. Das Gespräch ist hier die bessere Form — und die Seite wäre ein zweiter Ort
für dieselbe Wahrheit.

**Nur das Gespräch ausbauen und die Widgets Anzeige bleiben lassen.** Dann
hängt jede Handlung an einem erreichbaren Modell. ADR-0016 wollte das
ausdrücklich nicht, und ein 429 mitten im Turnier gibt ihm recht.
