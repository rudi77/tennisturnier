"""Zeigt, was die Testläufe nicht erreicht haben.

Gelesen wird der von ReportGenerator zusammengeführte lcov-Bericht — nicht die
einzelnen Cobertura-Dateien. Der Unterschied ist nicht kosmetisch: jedes
Testprojekt schreibt einen eigenen Bericht, in dem alles Fremde als ungetestet
dasteht. Wer die Dateien einzeln liest, misst deshalb zu wenig.

    dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults
    dotnet reportgenerator -reports:TestResults/**/coverage.cobertura.xml \
        -targetdir:TestResults/report -reporttypes:lcov \
        "-filefilters:-**/obj/**;-**/Migrations/**"
    python scripts/coverage-gaps.py [Namensfilter]
"""

import os
import sys

BERICHT = os.path.join("TestResults", "report", "lcov.info")


class Datei:
    def __init__(self, pfad: str) -> None:
        self.pfad = pfad
        self.zeilen: dict[int, int] = {}
        self.zweige: dict[tuple[int, str], int] = {}

    @property
    def offeneZeilen(self) -> list[int]:
        return sorted(nr for nr, hits in self.zeilen.items() if hits == 0)

    @property
    def offeneZweige(self) -> list[int]:
        return sorted({nr for (nr, _), hits in self.zweige.items() if hits == 0})


def lies(pfad: str) -> list[Datei]:
    dateien: list[Datei] = []
    aktuell: Datei | None = None

    with open(pfad, encoding="utf-8") as bericht:
        for zeile in bericht:
            zeile = zeile.strip()
            if zeile.startswith("SF:"):
                aktuell = Datei(zeile[3:])
                dateien.append(aktuell)
            elif zeile.startswith("DA:") and aktuell:
                nr, hits = zeile[3:].split(",")[:2]
                aktuell.zeilen[int(nr)] = int(hits)
            elif zeile.startswith("BRDA:") and aktuell:
                nr, block, zweig, hits = zeile[5:].split(",")
                aktuell.zweige[(int(nr), f"{block}/{zweig}")] = 0 if hits == "-" else int(hits)

    return dateien


def main() -> int:
    if not os.path.exists(BERICHT):
        print(f"{BERICHT} fehlt — erst `dotnet reportgenerator … -reporttypes:lcov`.")
        return 1

    filter_ = sys.argv[1] if len(sys.argv) > 1 else ""
    wurzel = os.getcwd() + os.sep

    zeilen = getroffen = zweige = zweigeGetroffen = 0
    luecken: list[Datei] = []

    for datei in lies(BERICHT):
        if filter_ and filter_ not in datei.pfad:
            continue

        zeilen += len(datei.zeilen)
        getroffen += sum(1 for hits in datei.zeilen.values() if hits)
        zweige += len(datei.zweige)
        zweigeGetroffen += sum(1 for hits in datei.zweige.values() if hits)

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
