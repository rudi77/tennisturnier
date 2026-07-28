# ADR-0009 — Serverseitige Oberfläche mit htmx, im API-Projekt

**Status:** Accepted

## Kontext

Die Anwendung hatte bis hierher nur eine HTTP-Schnittstelle. Bedient wird sie von
einer Turnierleitung, die am Turniertag mit einem Laptop am Ausschanktisch sitzt,
und von Zuschauern, die den Aushang auf dem Telefon lesen. Beide Gruppen bekommen
Listen, Tabellen und Formulare zu sehen — Bracket, Meldeliste, Warteschlange je
Platz, Ergebniseingabe.

Damit stellt sich die Frage nach Technik und Ort der Oberfläche.

## Entscheidung

**Serverseitig gerendertes HTML mit Razor Pages, Interaktivität über htmx, im
Projekt `TennisTurnier.Api`.**

Der Zustand, den die Oberfläche zeigt, gehört durchweg dem Server: welche
Zustandsübergänge ein Turnier gerade erlaubt, wer in der Warteschlange steht, ob
ein Ergebnis die Folgerunde füllen darf. Eine Single-Page-Anwendung müsste diesen
Zustand ein zweites Mal führen und mit dem Server abgleichen — samt der Regeln,
die ihn bestimmen. Genau diese zweite Wahrheit ist es, die auseinanderläuft.

Mit htmx bleibt sie an einer Stelle: eine Handlung geht als Formular an den
Server, und die Antwort ist der neu gerenderte Abschnitt, den sie verändert hat.
Ein Zustandsübergang tauscht den ganzen Turnierkopf aus, weil er Zustandsmarke,
mögliche Handlungen und Bearbeitbarkeit der Meldeliste zugleich ändert. Der
angezeigte Stand ist damit immer einer, den der Server tatsächlich hat.

**Im API-Projekt und nicht daneben.** Die Seiten rufen die Anwendungsfälle direkt
auf — dieselben `IClubService`, `ITournamentService`, `IMatchService`, die auch
die Minimal-API bedient. Ein eigenes Web-Projekt müsste stattdessen über HTTP mit
der API sprechen: ein zweiter Prozess, ein zweites Deployment, ein
weitergereichtes Token und eine Fehlerbehandlung, die JSON-Problemdetails wieder
in Meldungen übersetzt. Für eine Vereinsanwendung ist das Aufwand ohne Gegenwert.

Die Schichtung bleibt davon unberührt: `TennisTurnier.Api` ist die Composition
Root und darf nach ADR-0005 alles kennen. Die Seiten kennen die
Anwendungsschicht, keinen `DbContext` und keine Domänenentität, die nicht schon
im Vertrag der Anwendungsfälle steht.

### Verworfen

**SPA (React, Blazor WASM).** Sie zahlt sich, wo Zustand im Client entsteht —
Zeichenwerkzeuge, Editoren, Offlinebetrieb. Hier entsteht er im Server, und der
Preis wäre eine zweite Zustandshaltung, ein Build-Werkzeug und eine
API-Oberfläche, die jede Formularänderung mitmachen muss.

**Blazor Server.** Löst dasselbe Problem, hält dafür aber je Benutzer eine
Verbindung und einen Sitzungszustand vor. Am Turniertag hängen Zuschauer stunden­
lang auf demselben Aushang; ein Verbindungsabriss ist dort der Normalfall, kein
Ausnahmefall. Der öffentliche Aushang soll eine Seite sein, die man neu lädt.

**Formulare ohne htmx, mit Vollseiten-Neuladen.** Wäre noch einfacher, verliert
aber die Stelle im Bildschirm: wer in der Warteschlange von Platz 3 ein Match
nach vorn schiebt, steht danach wieder oben auf der Seite. htmx kostet eine
vendorierte Datei und keinen Build-Schritt.

## Konsequenzen

**Positiv.** Kein Build-Werkzeug, kein Paketmanager für das Frontend, kein
zweites Deployment. htmx liegt als eine Datei unter `wwwroot/js` — die Seiten
laden nichts aus dem Netz und laufen im Vereinsheim auch ohne Internet. Der
Ausfall einer Handlung ist ein fachlicher Fehler, den derselbe Anwendungsfall
wirft, den die API benutzt; die Seiten übersetzen ihn in eine Meldung, statt auf
eine Fehlerseite zu springen.

**Negativ.** Die Oberfläche kann nicht gegen eine fremde API laufen. Wer die
Anwendung einmal in zwei Prozesse teilen will, muss die Seiten umziehen und ihre
Aufrufe auf HTTP umstellen.

Jede Handlung ist ein Rundlauf zum Server. Das ist bei Ergebniseingabe und
Aufruf am Platz unkritisch — es sind wenige Kilobyte HTML —, wäre es aber bei
einer Ansicht, die auf jeden Tastendruck reagieren müsste. Die Spielersuche ist
deshalb die einzige Stelle mit Verzögerung, und sie schickt frühestens 300
Millisekunden nach dem letzten Zeichen.

**Die Anmeldung der Oberfläche ist offen.** Ein Browser braucht ein Cookie, die
API prüft ein Bearer-Token (ADR-0007). Solange kein Aussteller konfiguriert ist,
stellt eine Entwicklungsanmeldung das Cookie aus; sie macht den ersten Benutzer
zum Systemadministrator, weil die Rollenvergabe noch keinen Endpunkt hat. Sobald
`Oidc:Authority` gesetzt ist, verschwindet diese Seite ersatzlos — der
Authorization-Code-Flow gegen den Identity Provider steht noch aus, und bis
dahin ist die Oberfläche mit konfiguriertem Aussteller nicht benutzbar. Das ist
ausdrücklich eine offene Kante und keine Übergangslösung, die stehen bleiben
darf.
