# ADR-0026 — Zählen ohne Netz, und nichts zählt doppelt

**Status:** Accepted

## Kontext

Am Platz ist der Empfang schlecht. Bisher ging jeder getippte Punkt sofort an
den Server. Scheiterte das, gab es eine Fehlermeldung, und der Punkt war weg.
Wer weiterzählte, zählte auf einem Stand, der nicht mehr stimmte.

Einen Punkt einfach noch einmal zu schicken, reicht nicht. Kam er beim ersten
Mal an und nur die Antwort ging verloren, zählt er doppelt.

## Entscheidung

**Jeder Live-Schritt sagt, auf welchem Stand er getippt wurde, und die
Oberfläche schickt ihn nach, bis er ankommt.**

- `LiveRequest.After` ist die Zahl der Schritte, die das Match beim Tippen
  hatte. Stimmt sie nicht mehr, antwortet der Server mit **409** und übernimmt
  nichts. Das Feld ist freiwillig: Der Agent und das ganze Ergebnis kommen
  ohne es aus.
- Die Oberfläche stellt jeden Schritt in eine Warteschlange (`outbox.ts`), die
  im `localStorage` liegt und ein Neuladen übersteht. Verschickt wird der Reihe
  nach.
  - Ein Netzfehler lässt den Schritt liegen. Die Warteschlange versucht es
    wieder, sobald der Browser „online“ meldet, und sonst alle fünf Sekunden.
  - Eine 409 verwirft alles, was für dieses Match noch aussteht, denn es
    beruhte auf demselben Stand. Der Zähl-Dialog sagt dann, wie viele
    Eingaben nicht übernommen wurden.
  - Jeder andere Fehler verwirft nur den einen Schritt und zeigt seine
    Meldung.
- Der Zähl-Dialog wartet nicht mehr auf den Server. Er zeigt, wie viele
  Eingaben unterwegs sind oder auf Netz warten.

### Verworfen

- **Eine Kennung je Schritt, die der Server sich merkt.** Das wäre genauso
  sicher, bräuchte aber eine Liste bereits gesehener Kennungen im Turnier.
  Die Zahl der Schritte gibt es ohnehin.
- **Den Stand im Browser vorausrechnen.** Dann zeigte die Anzeigetafel ohne
  Netz sofort den neuen Punkt. Dafür müsste die Zählung (`LiveScoring`) ein
  zweites Mal in TypeScript existieren, und zwei Wahrheiten über den Stand
  sind genau das, was ADR-0022 vermeiden wollte.

## Folgen

- Ohne Netz bleibt die Anzeigetafel auf dem letzten bestätigten Stand stehen.
  Darunter steht, wie viele Eingaben warten.
- Zählen zwei Handys dasselbe Match, gewinnt, wer zuerst ankommt. Das andere
  Handy bekommt eine Meldung statt eines doppelten Punkts.
