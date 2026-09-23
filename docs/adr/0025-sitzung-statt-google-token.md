# ADR-0025 — Eine Sitzung statt des Google-Tokens

**Status:** Accepted — ersetzt in ADR-0019 den Absatz über den Ablauf der Token

## Kontext

Nach ADR-0019 schickte der Browser das Google-Id-Token bei jedem Aufruf mit.
Das Token gilt eine Stunde. ADR-0019 hat das bewusst hingenommen („für fürs
erste ist ein erneuter Klick zumutbar“).

Am Turniertag trifft das die falsche Person im falschen Moment: Die
Turnierleitung ist seit dem Vormittag in der App, und mitten im Halbfinale
steht sie wieder auf der Anmeldung. Dazu kam, dass das Token im
`sessionStorage` lag, wo jedes Skript der Seite es lesen konnte.

## Entscheidung

**Das Google-Token wird genau einmal eingelöst, danach trägt ein Cookie.**

- `POST /api/auth/session` nimmt das Token als `Bearer` entgegen. ASP.NET
  prüft es wie bisher (Signatur, Aussteller, Audience, Ablauf), MATCHDAY
  prüft die Freigabeliste (ADR-0023). Erst dann gibt es die Sitzung. Ein Konto,
  das nicht herein darf, bekommt kein Cookie.
- Das Cookie `matchday.session` ist `HttpOnly` und `SameSite=Lax`, hinter
  TLS auch `Secure`. Es gilt 30 Tage und verlängert sich mit jedem Aufruf.
  Es trägt Subjekt, Adresse, Bestätigung, Name und Bild, also genau das, was
  `AccountOf` und die Kopfzeile brauchen.
- Ein Richtlinien-Schema wählt je Anfrage: mit `Authorization: Bearer` das
  Google-Token, sonst das Cookie. `AccountOf` bleibt die eine Stelle, an der
  die Anmeldung hängt, und die Freigabeliste gilt bei jedem Aufruf, nicht nur
  beim Einlösen.
- `GET /api/auth/me` sagt der Oberfläche beim Laden, ob die Sitzung steht.
  `POST /api/auth/logout` beendet sie. Abgemeldet wird über ein Menü am
  eigenen Bild, nicht mehr mit einem Tipp darauf.
- Die Schlüssel, mit denen das Cookie verschlüsselt ist, liegen auf dem
  Datenträger (`Auth__KeysPath`, im Bild `/data/keys`). Lägen sie im
  Container, wäre nach jedem Deploy jede Sitzung ungültig.
- Railway schließt TLS vor der Anwendung ab. Die Anwendung übernimmt deshalb
  `X-Forwarded-Proto` und `X-Forwarded-Host`, sonst hielte sie jede Anfrage für
  http und gäbe das Cookie ohne `Secure` heraus.

CSRF: Die Aufrufe mit Cookie ändern nur mit `POST`, `PUT` und `DELETE`, und
ein `Lax`-Cookie geht bei solchen Anfragen von fremden Seiten nicht mit.
Anfragen mit JSON-Rumpf verlangen zudem den passenden Content-Type, den eine
fremde Seite ohne CORS nicht setzen darf.

### Verworfen

- **Das Token still erneuern** (Google One Tap mit `auto_select`). Das hängt an
  Cookies von Drittanbietern und an Browsereinstellungen, auf die MATCHDAY
  keinen Einfluss hat, und es ließe das Token im Skriptzugriff.
- **Eigene Refresh-Token.** Das wäre dieselbe Sitzung mit mehr beweglichen
  Teilen.

## Folgen

- Nach dem Deploy sind alle bisher Angemeldeten einmal abgemeldet: Ihr Token
  lag im Browser, eine Sitzung haben sie noch nicht.
- Wer von der Freigabeliste fällt, kommt beim nächsten Aufruf nicht mehr
  durch, auch mit einer gültigen Sitzung.
- Ohne Datenträger auf `/data` überleben Sitzungen keinen Neustart. Das gilt
  für die Datenbank ohnehin.
