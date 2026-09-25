# ADR-0029 — Erst Staging, dann Production

**Status:** Accepted — ergänzt ADR-0018

## Kontext

Bisher ging jeder grüne Push auf `main` direkt auf die Live-Instanz (ADR-0018).
Einen Ort, an dem eine Änderung unter echten Bedingungen laufen konnte, bevor
sie jemand am Turniertag benutzt, gab es nicht: nicht hinter Railways Proxy,
nicht mit Datenträger, nicht mit der Google-Anmeldung. Für Fehler blieb nur
eins, sie live zu finden.

Seit dem 25.09.2026 hat das Railway-Projekt eine zweite Umgebung, `staging`.

## Entscheidung

**Jede Änderung geht zuerst auf Staging. Production bekommt nur, was dort lief.**

- **Staging hängt an `main`.** Jeder grüne Push auf `main` landet dort.
- **Production hängt am Branch `production`.** Auf ihn wird nicht committet.
  Er wird nur nachgezogen, und zwar auf einen Stand, der vorher auf Staging
  lief:

  ```bash
  git push origin <geprüfter-commit>:production
  ```

  Das ist immer ein Vorspulen, nie ein Merge. Ein abgelehnter Push heißt, dass
  auf `production` etwas liegt, das nicht über `main` kam. Das wird geklärt und
  nicht überschrieben.
- **Beide Umgebungen warten auf die CI** („Wait for CI“ im Dashboard). Die
  Pipeline läuft ohnehin auf jedem Branch.

Die Zuordnung Umgebung ↔ Branch steht im Railway-Dashboard und nicht im Repo.
`railway.json` beschreibt nur den Bau, und der ist für beide gleich.

Verworfen:

- **Production ohne automatisches Deploy, von Hand angestoßen.** Das braucht
  keinen Branch. Aber welcher Commit live geht, entscheidet dann ein Klick im
  Dashboard und nicht der Git-Verlauf. Dass es derselbe ist, der auf Staging
  lief, müsste man jedes Mal selbst nachhalten.
- **Ein Branch `staging`, und Production an `main`.** Dann wäre der Normalfall,
  der Push auf `main`, der gefährliche.

## Folgen

- Was live ist, zeigt `git log origin/production`. Wann es live ging, zeigen
  die GitHub-Deployments der Umgebung `creative-renewal / production`.
- Staging braucht alles, was Production außerhalb des Repos hat, noch einmal
  eigen: den Datenträger auf `/data`, die `AZURE_OPENAI_*`- und `Auth__*`-
  Variablen und die Staging-Adresse als JavaScript-Ursprung beim
  Google-OAuth-Client.
- Ein dringender Fix geht denselben Weg, nur schneller: auf `main`, auf Staging
  ansehen, dann `production` nachziehen. Eine Abkürzung direkt auf
  `production` gibt es nicht.
