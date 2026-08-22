<#
.SYNOPSIS
    Misst die Abdeckung des Backends über alle Testprojekte hinweg.

.DESCRIPTION
    Die Projekte laufen nacheinander und rechnen ihre Treffer in dieselbe
    coverage.json (siehe scripts/coverage.runsettings). Der cobertura-Bericht
    des letzten Laufs ist die Summe aller — nur so stimmen die Zweige: ein
    nachträgliches Zusammenführen zweier Berichte kann nicht mehr unterscheiden,
    welcher Ausgang einer Verzweigung gelaufen ist.

    Danach zeigt scripts/coverage-gaps.py, was offen geblieben ist.
#>
[CmdletBinding()]
param(
    # Nur diese Datei(en) in der Lückenliste zeigen.
    [string]$Filter = ''
)

$ErrorActionPreference = 'Stop'
$wurzel = Split-Path $PSScriptRoot -Parent

Push-Location $wurzel
try {
    if (Test-Path 'TestResults') {
        Remove-Item 'TestResults' -Recurse -Force
    }

    # MergeWith wird relativ zum Testprojekt aufgelöst, nicht zum
    # Arbeitsverzeichnis — deshalb eine Fassung mit absolutem Pfad.
    $sammelstelle = Join-Path $wurzel 'TestResults/coverage.json'
    $einstellungen = Join-Path $wurzel 'TestResults/coverage.runsettings'

    New-Item -ItemType Directory -Force 'TestResults' | Out-Null
    (Get-Content (Join-Path $PSScriptRoot 'coverage.runsettings') -Raw) `
        -replace '<MergeWith>.*?</MergeWith>', "<MergeWith>$sammelstelle</MergeWith>" |
        Set-Content $einstellungen

    $projekte = Get-ChildItem 'tests' -Directory | ForEach-Object {
        Get-ChildItem $_.FullName -Filter '*.csproj' | Select-Object -First 1
    }

    $bericht = $null

    foreach ($projekt in $projekte) {
        Write-Host "→ $($projekt.BaseName)" -ForegroundColor Cyan

        dotnet test $projekt.FullName `
            --settings $einstellungen `
            --results-directory TestResults `
            --nologo `
            --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw "$($projekt.BaseName) ist rot."
        }

        $bericht = Get-ChildItem 'TestResults' -Recurse -Filter 'coverage.cobertura.xml' |
            Sort-Object LastWriteTime |
            Select-Object -Last 1

        # Der Collector legt seine json neben den Bericht. MergeWith liest sie
        # beim nächsten Lauf von der festen Stelle — also dorthin kopieren.
        $zwischenstand = Get-ChildItem 'TestResults' -Recurse -Filter 'coverage.json' |
            Where-Object { $_.FullName -ne $sammelstelle } |
            Sort-Object LastWriteTime |
            Select-Object -Last 1

        Copy-Item $zwischenstand.FullName $sammelstelle -Force
    }

    if (-not $bericht) {
        throw 'Kein Abdeckungsbericht entstanden.'
    }

    New-Item -ItemType Directory -Force 'TestResults/merged' | Out-Null
    Copy-Item $bericht.FullName 'TestResults/merged/Cobertura.xml' -Force

    python scripts/coverage-gaps.py $Filter
}
finally {
    Pop-Location
}
