# ADR-0018 — Railway liefert MATCHDAY aus, und die Pipeline hält es nicht auf

**Status:** Accepted

## Kontext

ADR-0016 hat den Neuanfang unter `matchday/` beschlossen: der alte Baum bleibt
stehen, bis der neue seine vier Dinge beherrscht, und wird dann entfernt.
Beschlossen wurde damit, was gebaut wird — nicht, was ausgeliefert wird. Das
blieb der alte Baum, und zwar unbemerkt.

Sichtbar wurde es erst beim Nachrechnen: Der letzte erfolgreiche Deploy auf
Railway stammte vom 31. August. Jeder Push danach lief neun Minuten und wurde
Sekunden später verworfen. Die Live-Instanz zeigte über zwei Wochen einen
Stand, den niemand mehr für den aktuellen hielt.

Drei Ursachen lagen übereinander, jede für sich hinreichend:

1. Das `railway.json` in der Wurzel nannte das `Dockerfile` daneben, und das
   baut `src/TennisTurnier.Api`.
2. Railway wartet auf die GitHub-Actions-Pipeline und deployt nur bei Grün.
   Rot war seit dem 3. September ein einziger Job: „Ende zu Ende" prüft die
   Anmeldung des **alten** Baums gegen Keycloak.
3. `matchday/Dockerfile` deklarierte `VOLUME /data`. Railways Builder weist das
   zurück und verlangt einen Datenträger, der im Dienst konfiguriert wird.

Die zweite ist die interessante. Der alte Baum wird nicht mehr ausgeliefert —
sein Testdurchlauf schützt also nichts mehr, was live geht, hält aber alles
auf, was live gehen soll. Ein Wächter vor einer Tür, die nicht mehr benutzt
wird, und zugleich das Schloss an der Tür daneben.

## Entscheidung

**Die Wurzel liefert MATCHDAY aus.** `railway.json` nennt
`matchday/Dockerfile`; der Bau läuft aus der Wurzel, weil ein Dockerfile nicht
aus seinem Kontext hinausgreifen kann — die `COPY`-Pfade dort tragen deshalb das
Präfix `matchday/`. Das zweite `railway.json` unter `matchday/` wäre damit tote
Anweisung und ist entfernt.

Bewusst **kein** zweiter Railway-Dienst und **kein** geändertes Root Directory:
Der bestehende Dienst behält Domain, Datenträger und Variablen. Der Datenträger
hängt ohnehin schon auf `/data`, und beide Anwendungen legen ihre Datenbank
genau dort ab. Ein neuer Dienst hätte eine neue Adresse bedeutet, und die steht
in Lesezeichen.

**Die Pipeline prüft je Baum.** Ein vorgeschalteter Job sieht nach, welcher Baum
sich geändert hat; die übrigen hängen daran. Wer den alten anfasst, bekommt ihn
unverändert vollständig geprüft, Keycloak und Playwright eingeschlossen. Wer nur
`matchday/` anfasst, wartet nicht auf einen Durchlauf, der seine Änderung nicht
berührt — und wird von dessen Zustand nicht aufgehalten.

Die Pipeline selbst zählt zu `matchday`: geprüft wird sie an dem Baum, der auch
ausgeliefert wird.

## Folgen

Ein Push, der nur `matchday/` berührt, ist in unter einer Minute durch statt in
neun. Das ist der Nebeneffekt, nicht der Zweck; der Zweck ist, dass der alte
Baum die Auslieferung des neuen nicht mehr blockiert.

Der Preis ist echt und soll benannt sein: Die Prüfung hängt am Umfang des
einzelnen Pushes, nicht am Zustand des Hauptzweigs. Ein roter alter Baum bleibt
rot und wird von einem späteren, unbeteiligten Push nicht erneut geprüft — die
Pipeline meldet dann Grün für einen Stand, in dem der alte Baum rot ist. Das ist
tragbar, solange er nicht ausgeliefert wird, und es endet, wenn er nach ADR-0016
entfernt wird. Bis dahin gilt: Sein Zustand ist an seinen eigenen Läufen
abzulesen, nicht am letzten Ergebnis des Hauptzweigs.

Zwei Dinge bleiben außerhalb des Repositories und damit Handarbeit im Dienst:
der Datenträger auf `/data` — ohne ihn ist die Datenbank nach jedem Neustart
leer — und die `AZURE_OPENAI_*`-Variablen. Fehlen sie, läuft MATCHDAY trotzdem;
nur das Eingabefeld bleibt stumm und sagt, was fehlt (ADR-0017).

Den Port gibt Railway über `PORT` vor. Ein fest verdrahtetes `ASPNETCORE_URLS`
hört daran vorbei, deshalb nimmt der `ENTRYPOINT` ihn entgegen.
