# ADR-0023 — Eine Freigabeliste hinter der Anmeldung

**Status:** Accepted

## Kontext

ADR-0019 hat die Anmeldung mit Google gebracht. Sie beantwortet, **wer** jemand
ist — nicht, **ob** er hier etwas darf. Mit dem Schalter an kommt jedes
Google-Konto herein, legt Turniere an und führt Gespräche mit dem Agenten. Das
Letzte kostet: Jede Nachricht ist ein Aufruf beim Modell, und die Rechnung geht
an den, der die Instanz betreibt.

Für eine Instanz, die ein Freundeskreis nutzt und eine Person bezahlt, ist
„jedes Google-Konto" zu weit.

## Entscheidung

**`Auth__AllowedEmails` nennt, wer herein darf.** E-Mail-Adressen, getrennt durch
Komma, Semikolon oder Leerraum; Groß- und Kleinschreibung zählt nicht.

- **Leer heißt jedes Konto** — so wie bisher. Die Liste ist eine Einschränkung,
  die man setzt, kein neuer Pflichtwert; wer sie nicht braucht, merkt nichts.
- **Nur bestätigte Adressen zählen.** Google schickt `email_verified` mit; ohne
  `true` dort gilt eine Adresse nicht, auch wenn sie auf der Liste steht.
- **Geprüft wird an derselben Stelle wie die Anmeldung** — in `AccountOf`, durch
  das jeder besitzergebundene Aufruf und jedes Einlösen eines Links läuft. Ein
  nicht freigegebenes Konto bekommt **403**, nicht 401: Eine neue Anmeldung mit
  demselben Konto änderte nichts, die Oberfläche soll also nicht zurück auf die
  Anmeldung springen, sondern sagen, was los ist.
- **Die Oberfläche fragt gleich nach der Anmeldung** (`/api/auth/check`), ob das
  Konto trägt. Tut es das nicht, wird das Token verworfen, und der
  Anmeldeschirm nennt den Grund.

Die Liste steht in der Konfiguration des Dienstes, nicht im Repository:
Adressen sind personenbezogen, und das Repository ist nicht der Ort dafür.

**Das Mitschauen bleibt offen** (ADR-0016). Zuschauer brauchen weder Konto noch
Freigabe.

### Verworfen

- **Rollen oder eine Tabelle im Speicher.** Ein Verwaltungsschirm für ein paar
  Adressen wäre mehr Oberfläche, als die Frage verdient. Wenn aus „ein paar"
  „ein Verein" wird, ist das ein neues ADR.
- **Die Prüfung in Googles Consent Screen** („Testnutzer" im Modus *Testing*).
  Sie hält nur bis zur Veröffentlichung der App und liegt außerhalb dessen, was
  sich hier testen lässt.

## Folgen

Wer jemanden dazunehmen will, ändert eine Variable im Dienst; Railway startet
neu. Ein Konto, das von der Liste fällt, kommt beim nächsten Aufruf nicht mehr
durch — auch mit einem noch gültigen Token.

Turniere eines Kontos, das nicht mehr freigegeben ist, bleiben erhalten und
über ihren Mitschau-Link sichtbar.
