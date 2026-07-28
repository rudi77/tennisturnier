#!/usr/bin/env bash
# SessionStart-Hook: stellt sicher, dass in einer frischen Session gebaut und
# getestet werden kann. Idempotent — bei bereits vorhandenem SDK ist er ein No-op.
#
# Hinweis zur Umgebung: die offizielle Bezugsquelle des .NET-Installers
# (builds.dotnet.microsoft.com) wird von der Egress-Policy dieser Umgebung
# geblockt. Das Ubuntu-Paket dotnet-sdk-10.0 liefert dasselbe SDK und ist
# erreichbar, deshalb der Weg über apt.
set -uo pipefail

log() { printf '[setup] %s\n' "$1" >&2; }

if command -v dotnet >/dev/null 2>&1; then
    log ".NET SDK bereits vorhanden: $(dotnet --version 2>/dev/null)"
else
    log "Installiere .NET 10 SDK über apt ..."
    if [ "$(id -u)" -eq 0 ]; then
        APT="apt-get"
    elif sudo -n true 2>/dev/null; then
        APT="sudo apt-get"
    else
        log "Weder Root noch passwortloses sudo — SDK-Installation übersprungen."
        exit 0
    fi

    if ! $APT update -qq >/dev/null 2>&1; then
        log "apt-get update fehlgeschlagen — versuche Installation trotzdem."
    fi
    if $APT install -y -qq dotnet-sdk-10.0 >/dev/null 2>&1; then
        log ".NET SDK installiert: $(dotnet --version 2>/dev/null)"
    else
        log "Installation des .NET SDK fehlgeschlagen. Build und Tests stehen nicht zur Verfügung."
        exit 0
    fi
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

cd "$(dirname "$0")/../.." || exit 0

log "Stelle NuGet-Pakete wieder her ..."
if dotnet restore >/dev/null 2>&1; then
    log "Restore abgeschlossen. 'dotnet build' und 'dotnet test' sind einsatzbereit."
else
    log "Restore fehlgeschlagen — 'dotnet restore' manuell ausführen und Ausgabe prüfen."
fi

exit 0
