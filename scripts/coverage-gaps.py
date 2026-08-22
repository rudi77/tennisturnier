"""Zeigt, was die Testläufe nicht erreicht haben.

Gelesen werden die rohen Cobertura-Dateien aller Testprojekte und selbst
zusammengeführt: pro Quellzeile zählt der beste Treffer aus allen Läufen. Jedes
Testprojekt schreibt einen eigenen Bericht, in dem alles Fremde als ungetestet
dasteht — wer eine Datei einzeln liest, misst deshalb zu wenig.

Zusammengeführt wird hier und nicht von ReportGenerator, weil dessen
lcov-Ausgabe die Zweigtreffer eines Laufs verliert, sobald dieselbe Klasse in
mehreren Berichten vorkommt: eine Bedingung, die nur die API-Tests erreichen,
stünde sonst als offen da, obwohl sie gedeckt ist.

    dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults
    python scripts/coverage-gaps.py [Namensfilter]
"""

import glob
import os
import sys
import xml.etree.ElementTree as ET

WURZEL = os.path.join("TestResults", "**", "coverage.cobertura.xml")

# Was nicht von Hand geschrieben wurde, wird auch nicht von Hand getestet.
AUSGENOMMEN = (os.sep + "obj" + os.sep, os.sep + "Migrations" + os.sep)


class Datei:
    def __init__(self, pfad: str) -> None:
        self.pfad = pfad
        self.zeilen: dict[int, int] = {}
        self.zweige: dict[int, tuple[int, int]] = {}

    def merke(self, nr: int, hits: int, abgedeckt: int | None, gesamt: int | None) -> None:
        self.zeilen[nr] = max(self.zeilen.get(nr, 0), hits)

        if gesamt:
            vorher = self.zweige.get(nr, (0, gesamt))
            self.zweige[nr] = (max(vorher[0], abgedeckt or 0), max(vorher[1], gesamt))

    @property
    def offeneZeilen(self) -> list[int]:
        return sorted(nr for nr, hits in self.zeilen.items() if hits == 0)

    @property
    def offeneZweige(self) -> list[int]:
        return sorted(nr for nr, (ab, ges) in self.zweige.items() if ab < ges)


def anteil(text: str) -> tuple[int, int]:
    """„50% (1/2)" — nur der Bruch dahinter interessiert."""
    bruch = text[text.index("(") + 1 : text.index(")")]
    ab, ges = bruch.split("/")

    return int(ab), int(ges)


def lies() -> dict[str, Datei]:
    dateien: dict[str, Datei] = {}

    for bericht in glob.glob(WURZEL, recursive=True):
        baum = ET.parse(bericht)
        wurzeln = [q.text or "" for q in baum.iter("source")]

        for klasse in baum.iter("class"):
            relativ = klasse.get("filename") or ""
            if any(teil in relativ for teil in AUSGENOMMEN):
                continue

            pfad = next(
                (os.path.join(w, relativ) for w in wurzeln if os.path.exists(os.path.join(w, relativ))),
                relativ,
            )

            datei = dateien.setdefault(pfad, Datei(pfad))

            # Nur die <lines> der Klasse selbst; die Methodenblöcke wiederholen
            # dieselben Zeilen und würden sie doppelt zählen.
            for lines in klasse.findall("lines"):
                for zeile in lines.findall("line"):
                    deckung = zeile.get("condition-coverage")
                    ab, ges = anteil(deckung) if deckung else (None, None)

                    datei.merke(int(zeile.get("number", "0")), int(zeile.get("hits", "0")), ab, ges)

    return dateien


def main() -> int:
    dateien = lies()
    if not dateien:
        print(f"Keine Berichte unter {WURZEL} — erst `dotnet test --collect:\"XPlat Code Coverage\"`.")
        return 1

    filter_ = sys.argv[1] if len(sys.argv) > 1 else ""
    wurzel = os.getcwd() + os.sep

    zeilen = getroffen = zweige = zweigeGetroffen = 0
    luecken: list[Datei] = []

    for datei in dateien.values():
        if filter_ and filter_ not in datei.pfad:
            continue

        zeilen += len(datei.zeilen)
        getroffen += sum(1 for hits in datei.zeilen.values() if hits)
        zweige += sum(ges for _, ges in datei.zweige.values())
        zweigeGetroffen += sum(ab for ab, _ in datei.zweige.values())

        if datei.offeneZeilen or datei.offeneZweige:
            luecken.append(datei)

    if zeilen == 0:
        print("Nichts gefunden.")
        return 1

    print(f"Zeilen {getroffen}/{zeilen} = {getroffen / zeilen:.2%}")
    if zweige:
        print(f"Zweige {zweigeGetroffen}/{zweige} = {zweigeGetroffen / zweige:.2%}")
    print()

    for datei in sorted(luecken, key=lambda d: -(len(d.offeneZeilen) + len(d.offeneZweige))):
        print(datei.pfad.replace(wurzel, ""))
        if datei.offeneZeilen:
            print(f"   Zeilen: {datei.offeneZeilen}")
        if datei.offeneZweige:
            print(f"   Zweige: {datei.offeneZweige}")

    return 0 if not luecken else 2


if __name__ == "__main__":
    raise SystemExit(main())
