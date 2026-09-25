# ADR-0028 — Ein eigener Schlüssel für den Mitschau-Link

**Status:** Accepted — ändert ADR-0016 an einer Stelle

## Kontext

Der Mitschau-Link war die Id des Turniers (`?t=<id>`). Erraten ließ sie sich
nicht, aber zurückholen auch nicht: Landete der Link an einer Stelle, an die er
nicht gehörte, half nur Löschen. Dabei zeigt die Mitschau die Namen aller
Mitspieler, dazu Ort und Termin.

Die Id hätte sich nicht einfach austauschen lassen. Sie steht in jeder Sicht,
jede Adresse der API hängt an ihr, und `GET /api/tournaments/{id}` stand offen.

## Entscheidung

- **Der Mitschau-Link bekommt einen eigenen Schlüssel** (`Tournament.ViewerToken`,
  16 Zufallsbytes wie das Verwaltertoken). Er hängt nicht am Verwaltertoken:
  Wer den Mitschau-Link erneuert, sperrt die Zuschauer aus, nicht die
  Turnierleitung oder die Helfer am Platz. Umgekehrt lässt das Rotieren des
  Verwalterlinks den Mitschau-Link stehen.
- **Die Verwaltung kann ihn erneuern:** unter „Teilen“ mit einer Rückfrage,
  im Gespräch mit `renew_viewer_link`, über die API mit
  `POST /api/tournaments/{id}/viewer-token/rotate`. Von selbst läuft er nie ab.
  Ein Link, der am Turniertag plötzlich nicht mehr geht, wäre schlimmer als
  das, wovor ein Ablauf schützen soll.
- **Die Id öffnet nichts mehr für Zuschauer.** `GET /api/tournaments/{id}` und
  `get_tournament` verlangen, dass man das Turnier verwaltet oder den
  Eintragen-Link hat. Zuschauer kommen über `GET /api/tournaments/by-viewer/{token}`
  herein, das offen bleibt, auch wenn die Instanz eine Anmeldung verlangt.
- **Die Live-Verbindung verlangt einen Ausweis:** einen der drei Links
  (`/api/tournaments/{id}/live?key=…`) oder das Eigentum am Turnier. Ein
  EventSource kann keine Kopfzeilen schicken. Der Schlüssel steht deshalb in
  der Adresse, und der Eigentümer weist sich per Cookie aus: mit Anmeldung
  über das Sitzungs-Cookie, ohne Anmeldung über `matchday.client`, das die
  Oberfläche vor dem Abonnieren setzt. So schaut die Turnierleitung auch live
  mit, wenn ihr Browser kein Verwaltertoken kennt, etwa bei einem Turnier, das
  sie im Gespräch angelegt hat. Die Browserkennung steht bewusst nicht in der
  Adresse: Ohne Anmeldung ist sie der Schlüssel zu allen eigenen Turnieren.
  Der Hub schiebt das Turnier selbst und nicht nur seine Sicht. Gilt der
  Ausweis nach einer Änderung nicht mehr, endet der Strom mit `revoked`. Die Mitschau-Seite räumt dann das Turnier weg und zeigt nur noch
  den Hinweis, nach dem neuen Link zu fragen.
- **Alte Links gelten weiter:** Ein Turnier ohne gespeicherten Schlüssel nimmt
  seine Id als Schlüssel. Schon geteilte `?t=<id>`-Links funktionieren also,
  bis jemand den Link erneuert.
- **Suchmaschinen bleiben draußen:** `<meta name="robots" content="noindex, nofollow">`
  im Kopf der Seite, dazu `X-Robots-Tag` auf jeder Antwort. Eine `robots.txt`
  mit `Disallow` gibt es bewusst nicht, denn manche Vorschau-Abholer von
  Messengern halten sich daran, und die Vorschau des Links soll bleiben.

## Folgen

- Wer einen alten Mitschau-Link erneuert, bekommt einen kürzeren, zufälligen.
  Die Id taucht danach in keinem geteilten Link mehr auf.
- Die Live-Aktualisierung bleibt für alle, wie sie war: für die Turnierleitung,
  für die Helfer mit dem Eintragen-Link und für die Zuschauer.
