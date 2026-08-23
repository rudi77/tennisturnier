"""Macht sichtbar, was ein CI-Lauf ergeben hat.

GitHub zeigt von einem Lauf zunächst nur grün oder rot. Alles Weitere — wie
viele Tests liefen, wie hoch die Abdeckung ist, welche Warnungen der Compiler
hatte — steht im Protokoll, und dort sieht es niemand nach. Dieses Skript
schreibt es stattdessen in die Zusammenfassung des Laufs, die auf der
Übersichtsseite steht.

Ausgegeben wird Markdown auf die Standardausgabe; der Arbeitsablauf hängt es an
`$GITHUB_STEP_SUMMARY` an. Warnungen kommen zusätzlich als Annotation heraus —
damit stehen sie im Pull Request an der Zeile, um die es geht.

    python scripts/ci-bericht.py warnungen build.log
    python scripts/ci-bericht.py tests TestResults
    python scripts/ci-bericht.py abdeckung-frontend app/coverage/coverage-summary.json
    python scripts/ci-bericht.py e2e app/playwright-report/ergebnisse.json
"""

import json
import os
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

# „Pfad(12,34): warning CS0168: Text [Projekt.csproj]" — dieselbe Form für
# Fehler. Der Projektanhang hinten interessiert nicht.
MELDUNG = re.compile(
    r"^(?P<datei>[^\s(].*?)\((?P<zeile>\d+),(?P<spalte>\d+)\):\s+"
    r"(?P<art>warning|error)\s+(?P<code>[A-Z]+\d+):\s+(?P<text>.*?)(?:\s+\[[^\]]+\])?$"
)


def relativ(pfad: str) -> str:
    """Annotationen brauchen den Pfad relativ zur Repository-Wurzel."""
    try:
        return str(pathlib.Path(pfad).resolve().relative_to(pathlib.Path.cwd()).as_posix())
    except ValueError:
        return pfad


def warnungen(protokoll: str) -> int:
    """Annotationen und ein Abschnitt zu dem, was der Compiler zu sagen hatte."""
    gefunden: dict[tuple[str, str, str, str], str] = {}

    with open(protokoll, encoding="utf-8", errors="replace") as datei:
        for zeile in datei:
            treffer = MELDUNG.match(zeile.strip())
            if not treffer:
                continue

            schluessel = (
                treffer["art"],
                relativ(treffer["datei"]),
                treffer["zeile"],
                treffer["code"],
            )

            # MSBuild wiederholt dieselbe Meldung je Zielframework und je
            # abhängigem Projekt. Einmal genügt.
            gefunden.setdefault(schluessel, f"{treffer['code']}: {treffer['text']}")
            print(
                f"::{treffer['art']} file={schluessel[1]},"
                f"line={treffer['zeile']},col={treffer['spalte']}::{gefunden[schluessel]}",
                file=sys.stderr,
            )

    print("## Compiler\n")

    if not gefunden:
        print("Keine Warnungen, keine Fehler.")
        print()
        print(
            "> `TreatWarningsAsErrors` steht auf `true`: eine Warnung bricht den Bau ab. "
            "Dieser Abschnitt ist deshalb im Normalfall leer — und genau das ist die Aussage."
        )
        return 0

    print("| Art | Datei | Zeile | Meldung |")
    print("|---|---|---:|---|")

    for (art, datei, zeile, _), text in sorted(gefunden.items()):
        print(f"| {art} | `{datei}` | {zeile} | {text} |")

    return len(gefunden)


def tests(verzeichnis: str) -> int:
    """Zählt die Testergebnisse aus den trx-Dateien zusammen."""
    raum = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
    zeilen: list[tuple[str, int, int, int, str]] = []
    gesamt = fehler = 0

    for pfad in sorted(pathlib.Path(verzeichnis).rglob("*.trx")):
        baum = ET.parse(pfad)
        zusammenfassung = baum.find(f"{raum}ResultSummary/{raum}Counters")

        if zusammenfassung is None:
            continue

        bestanden = int(zusammenfassung.get("passed", "0"))
        gescheitert = int(zusammenfassung.get("failed", "0"))
        uebersprungen = int(zusammenfassung.get("notExecuted", "0"))

        # Der Name des Laufs ist ein Zeitstempel und sagt nichts; der
        # Dateiname trägt das Testprojekt (siehe scripts/coverage.ps1).
        zeilen.append((pfad.stem, bestanden, gescheitert, uebersprungen, pfad.name))
        gesamt += bestanden + gescheitert + uebersprungen
        fehler += gescheitert

    print("## Tests\n")

    if not zeilen:
        print("Keine Testergebnisse gefunden.")
        return 0

    print("| Testprojekt | Bestanden | Gescheitert | Übersprungen |")
    print("|---|---:|---:|---:|")

    for name, bestanden, gescheitert, uebersprungen, _ in sorted(zeilen):
        print(f"| {name} | {bestanden} | {gescheitert} | {uebersprungen} |")

    print(f"\n**{gesamt} Tests**, davon {fehler} gescheitert.")

    return fehler


def abdeckungFrontend(pfad: str) -> int:
    """Die vier Kennzahlen aus dem json-summary von Vitest."""
    with open(pfad, encoding="utf-8") as datei:
        bericht = json.load(datei)

    gesamt = bericht["total"]
    benennung = {
        "lines": "Zeilen",
        "statements": "Anweisungen",
        "branches": "Zweige",
        "functions": "Funktionen",
    }

    print("## Abdeckung Frontend\n")
    print("| | Abgedeckt | Gesamt | Anteil |")
    print("|---|---:|---:|---:|")

    for schluessel, name in benennung.items():
        wert = gesamt[schluessel]
        print(f"| {name} | {wert['covered']} | {wert['total']} | {wert['pct']:.2f} % |")

    # Dateien ohne ausführbare Zeile — reine Typdeklarationen — stehen mit
    # null Prozent da. Sie sind nicht ungetestet, sie sind nichts zu testen.
    offen = [
        datei
        for datei, wert in bericht.items()
        if datei != "total" and wert["lines"]["total"] > 0 and wert["lines"]["pct"] < 100
    ]

    if offen:
        print("\n**Nicht vollständig abgedeckt:**\n")
        for datei in sorted(offen):
            print(f"- `{os.path.relpath(datei)}`")

    return 0


def abdeckungE2E(pfad: str) -> int:
    """Was der Durchlauf im Browser ergeben hat, aus dem JSON-Bericht."""
    with open(pfad, encoding="utf-8") as datei:
        bericht = json.load(datei)

    stand = {"expected": 0, "unexpected": 0, "flaky": 0, "skipped": 0}
    namen: list[str] = []

    def durchlaufen(eintrag: dict) -> None:
        for datei in eintrag.get("suites", []):
            durchlaufen(datei)

        for fall in eintrag.get("specs", []):
            for lauf in fall.get("tests", []):
                stand[lauf.get("status", "expected")] = (
                    stand.get(lauf.get("status", "expected"), 0) + 1
                )

                if lauf.get("status") == "unexpected":
                    namen.append(f"{fall.get('file', '?')} — {fall.get('title', '?')}")

    for suite in bericht.get("suites", []):
        durchlaufen(suite)

    print("## Ende zu Ende\n")
    print("| Bestanden | Gescheitert | Wackelig | Übersprungen |")
    print("|---:|---:|---:|---:|")
    print(
        f"| {stand['expected']} | {stand['unexpected']} "
        f"| {stand['flaky']} | {stand['skipped']} |"
    )

    if namen:
        print("\n**Gescheitert:**\n")
        for name in namen:
            print(f"- {name}")

    return 0


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 1

    befehl, ziel = sys.argv[1], sys.argv[2]

    if befehl == "warnungen":
        warnungen(ziel)
        return 0

    if befehl == "tests":
        # Gescheiterte Tests lassen den Schritt davor schon rot werden; hier
        # zählt nur, dass der Bericht entsteht.
        tests(ziel)
        return 0

    if befehl == "abdeckung-frontend":
        return abdeckungFrontend(ziel)

    if befehl == "e2e":
        return abdeckungE2E(ziel)

    print(f"Unbekannt: {befehl}")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
