# ADR-0019 — Eine Anmeldung mit Google, und ein Schalter davor

**Status:** Accepted

## Kontext

ADR-0016 hat Konten ausdrücklich abgelehnt: Wer ein Turnier anlegt, bekommt
einen Verwalterlink, alle anderen den Mitschau-Link, und der Browser führt eine
selbst vergebene Kennung, damit „meine Turniere" ohne Anmeldung funktioniert.
Dort steht auch, wann das kippt: „Sobald Fremde oder Vereine im Spiel sind,
kippt diese Entscheidung zurück zu Konten — das ist dann ein neues ADR, kein
Schalter."

Dieser Fall ist eingetreten, wenn auch anders als erwartet. Die Instanz steht
öffentlich im Netz. Die Browserkennung ist kein Geheimnis, sondern eine
Kopfzeile, die jeder setzen kann: `X-Matchday-Client: <fremde Kennung>` genügt,
um die Turnierliste eines anderen zu sehen und seine Turniere zu ändern. Für
einen Freundeskreis auf einem Rechner war das tragbar. Für eine erreichbare
Adresse ist es keine Zugangsbeschränkung, sondern eine Verabredung.

## Entscheidung

**Eine Anmeldung mit Google, und ein Schalter davor.**

`Auth__Required` entscheidet. Steht er auf `false` — die Vorgabe —, verhält sich
alles wie in ADR-0016 beschrieben; es wird nicht einmal eine Authentifizierung
eingehängt. Steht er auf `true`, verlangt jeder besitzergebundene Aufruf ein
gültiges Google-Id-Token.

Der Schalter ist Absicht, keine Unentschlossenheit. Er erlaubt, die Anmeldung
einzuschalten, ohne die lokale Entwicklung und die Tests an ein Google-Konto zu
binden, und er erlaubt, sie im Betrieb zu setzen, bevor die Oberfläche sie
überall abbildet. Er ist ausdrücklich **kein** Ersatz für die Entscheidung: Auf
einer öffentlich erreichbaren Instanz gehört er auf `true`.

### Wer prüft

Die Prüfung macht ASP.NET, nicht MATCHDAY: Signatur gegen Googles Schlüssel,
Aussteller, Audience und Ablauf. Die Audience ist die eigene Client-Id, ein
Token für eine andere Anwendung gilt hier also nicht. Eigene Krypto wäre genau
die falsche Stelle für Selbstgebautes.

Verlangt die Instanz eine Anmeldung, ohne eine Client-Id zu kennen, bricht der
Start ab. Ohne Client-Id kann kein Token gelten — die Anwendung wiese jeden ab,
und zwar still.

### Eine Stelle, nicht dreißig

Jeder besitzergebundene Aufruf läuft durch `ActorOf`. Dort und nur dort hängt
die Anmeldung: Ohne Schalter kommt die Kennung aus der Kopfzeile, mit Schalter
aus dem geprüften Token, als `google:<subject>`. Das Präfix hält beide Welten
auseinander — sonst käme ein Browser, der sich eine Google-Id als Kennung gibt,
nach dem Abschalten an fremde Turniere.

### Was offen bleibt

**Das Mitschauen.** Zuschauer haben kein Konto und sollen keins brauchen; das
ist der Kern von ADR-0016 und überlebt diese Entscheidung unverändert. Offen
bleiben deshalb die Turniersicht, der Live-Strom, die Gesundheitsprüfung und
die Frage, ob überhaupt eine Anmeldung verlangt wird — Letzteres muss lesen
können, wer sich anmelden soll.

**Der Verwalterlink dagegen nicht.** Er ist ein Schlüssel, kein Besitz. Mit
Schalter muss angemeldet sein, wer ihn einlöst; was er damit darf, entscheidet
weiterhin das Token. Ein weitergegebener Link öffnet damit nicht mehr die Tür
für jeden, der ihn findet.

## Folgen

Die eigenen Turniere folgen dem Konto statt dem Browser. Das ist der eigentliche
Gewinn und war mit der Browserkennung nicht zu haben: ein neues Gerät, dieselben
Turniere.

Der Preis ist ein Bruch in den Daten. Turniere, die ohne Anmeldung angelegt
wurden, gehören einer Browserkennung; nach dem Einschalten gehören neue
Turniere einem Konto. Die alten verschwinden nicht, sie sind nur nicht mehr
„meine" — erreichbar bleiben sie über ihren Verwalterlink. Eine Übernahme wäre
möglich, ist aber nicht gebaut: Sie hieße, einem Konto auf Zuruf fremde
Turniere zuzuschreiben, und dafür fehlt der Beweis, dass es die eigenen sind.

Google-Id-Token laufen nach einer Stunde ab. Die Oberfläche wirft ein
abgelaufenes Token weg, bevor sie es benutzt, und ein 401 im Betrieb führt
zurück auf die Anmeldung statt in eine Reihe von Fehlermeldungen. Ein stiller
Auffrischungsweg ist nicht gebaut — für „fürs erste" ist ein erneuter Klick
zumutbar.

Nur Google. Mehrere Aussteller wären dieselbe Mechanik mit einer Liste statt
einem Wert, aber jeder weitere will eigene Einrichtung und eigene Prüfung. Wenn
es so weit ist, ist es eine Zeile mehr in dieser Datei, kein Umbau.
