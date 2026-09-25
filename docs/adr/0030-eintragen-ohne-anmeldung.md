# ADR-0030 — Der Eintragen-Link braucht keine Anmeldung

**Status:** Accepted — ergänzt ADR-0019 (Anmeldung) und ADR-0022 (Eintragen-Link)

## Kontext

Wenn die Instanz eine Anmeldung verlangt, verlangte sie die bisher auch für den
Eintragen-Link. Der Gedanke war: Der Link schreibt, das Mitschauen nicht.

Am ersten echten Abend ging das schief. Der Eintragen-Link ging an einen
Mitspieler, dessen Google-Konto nicht auf der Freigabeliste steht (ADR-0023).
Er landete auf der Anmeldung und dann bei „nicht freigegeben“. Eintragen
konnte er nicht. Dabei war genau das der Zweck des Links.

Die Freigabeliste regelt, wer Turniere anlegt und damit Modellrechnung
verursacht. Wer am Platz Punkte zählt, gehört nicht dazu, und die
Turnierleitung soll nicht jeden Mitspieler freischalten müssen.

## Entscheidung

**Der Eintragen-Link ist ein Schlüssel wie der Mitschau-Link.** Wer ihn hat,
kommt ohne Konto herein, auch mit einem Konto, das nicht freigegeben ist.

- `GET /api/tournaments/by-scorer/{token}` ist offen.
- Ergebnis eintragen, Ergebnis löschen und live zählen nehmen den Handelnden
  über `Endpoints.ScorerOf`. Bringt die Anfrage `X-Scorer-Token` mit, und
  scheitert die Anmeldung, handelt „niemand“ (eine leere Kennung) mit diesem
  Token. Dürfen tut er nur, was `Actor.MayScore` erlaubt: zählen und
  eintragen, nicht verwalten.
- Die Oberfläche fragt auf `?s=…` nicht mehr nach der Anmeldung.

Der Verwalterlink bleibt hinter der Anmeldung. Er darf alles, auch löschen,
und wer ihn einlöst, bekommt das Turnier in die eigene Liste.

## Folgen

- Wer den Eintragen-Link hat, kann Ergebnisse eintragen und löschen, egal ob
  angemeldet oder nicht. Das war mit angemeldeten Mitspielern schon so, nur
  jetzt ohne die Hürde. Gerät der Link in falsche Hände, hilft das Rotieren
  des Verwalterlinks, das den Eintragen-Link mitnimmt (ADR-0022).
- Die Freigabeliste schützt weiterhin, was sie schützen soll: das Anlegen von
  Turnieren und das Gespräch mit dem Modell.
