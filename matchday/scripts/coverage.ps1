<#
.SYNOPSIS
    Misst die Abdeckung von MATCHDAY über beide Testprojekte hinweg.

.DESCRIPTION
    Die Projekte laufen nacheinander und rechnen ihre Treffer in dieselbe
    coverage.json (siehe scripts/coverage.runsettings). Der cobertura-Bericht des
    letzten Laufs ist die Summe aller.

    Danach steht je Datei, was offen geblieben ist — Zeilen ohne Treffer und
    Verzweigungen, von denen nur ein Ausgang gelaufen ist.
#>
[CmdletBinding()]
param(
    # Nur Dateien zeigen, deren Pfad dies enthält.
    [string]$Filter = ''
)

$ErrorActionPreference = 'Stop'
$wurzel = Split-Path $PSScriptRoot -Parent

Push-Location $wurzel
try {
    if (Test-Path 'TestResults') {
        Remove-Item 'TestResults' -Recurse -Force
    }

    New-Item -ItemType Directory -Force 'TestResults' | Out-Null
    $vorlage = Get-Content (Join-Path $PSScriptRoot 'coverage.runsettings') -Raw
    $einstellungen = Join-Path $wurzel 'TestResults/coverage.runsettings'

    $projekte = Get-ChildItem 'tests' -Directory | ForEach-Object {
        Get-ChildItem $_.FullName -Filter '*.csproj' | Select-Object -First 1
    }

    $bericht = $null
    $vorlauf = $null

    foreach ($projekt in $projekte) {
        Write-Host "→ $($projekt.BaseName)" -ForegroundColor Cyan

        # Coverlet legt seine json in einen eigenen Ordner je Lauf. MergeWith muss
        # deshalb auf die Datei des vorigen Laufs zeigen; ein fester Pfad findet
        # nie etwas, und dann zählt am Ende nur das letzte Projekt.
        $merge = if ($vorlauf) { "<MergeWith>$vorlauf</MergeWith>" } else { '' }
        ($vorlage -replace '<MergeWith>.*?</MergeWith>', $merge) | Set-Content $einstellungen

        dotnet test $projekt.FullName `
            --settings $einstellungen `
            --results-directory TestResults `
            --nologo `
            --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw "$($projekt.BaseName) ist rot."
        }

        $vorlauf = (Get-ChildItem 'TestResults' -Recurse -Filter 'coverage.json' |
            Sort-Object LastWriteTime |
            Select-Object -Last 1).FullName

        $bericht = Get-ChildItem 'TestResults' -Recurse -Filter 'coverage.cobertura.xml' |
            Sort-Object LastWriteTime |
            Select-Object -Last 1
    }

    if (-not $bericht) {
        throw 'Kein Abdeckungsbericht entstanden.'
    }

    [xml]$xml = Get-Content $bericht.FullName -Raw

    $zeilen = [double]$xml.coverage.'line-rate' * 100
    $zweige = [double]$xml.coverage.'branch-rate' * 100
    Write-Host ''
    Write-Host ("Gesamt: Zeilen {0:N2} %, Zweige {1:N2} %" -f $zeilen, $zweige) -ForegroundColor Yellow

    $luecken = [ordered]@{}

    foreach ($klasse in $xml.SelectNodes('//class')) {
        $datei = $klasse.filename
        if ($Filter -and $datei -notlike "*$Filter*") {
            continue
        }

        foreach ($zeile in $klasse.SelectNodes('.//line')) {
            $treffer = [int]$zeile.hits
            $abdeckung = $zeile.'condition-coverage'
            $offen = $treffer -eq 0 -or ($zeile.branch -eq 'True' -and $abdeckung -and -not $abdeckung.StartsWith('100%'))

            if ($offen) {
                if (-not $luecken.Contains($datei)) {
                    $luecken[$datei] = [System.Collections.Generic.SortedSet[int]]::new()
                }
                [void]$luecken[$datei].Add([int]$zeile.number)
            }
        }
    }

    if ($luecken.Count -eq 0) {
        Write-Host 'Keine Lücke.' -ForegroundColor Green
        return
    }

    Write-Host ''
    Write-Host 'Offen:' -ForegroundColor Red
    foreach ($datei in $luecken.Keys) {
        Write-Host ("  {0}: {1}" -f $datei, ($luecken[$datei] -join ', '))
    }

    # Eine Lücke ist eine Entscheidung: entweder fehlt ein Test, oder die Zeile
    # wird gelöscht. Ein dritter Ausgang existiert nicht — also rot.
    if (-not $Filter) {
        throw "$($luecken.Count) Datei(en) mit offenen Stellen."
    }
}
finally {
    Pop-Location
}
