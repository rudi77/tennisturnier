# MATCHDAY als ein Bild: Anwendung und Oberfläche.
#
# Zwei Dienste wären der naheliegende Schnitt und hier der falsche. Die
# Oberfläche ist eine Single-Page-Anwendung ohne eigene Laufzeit — sie ist ein
# Ordner mit Dateien. Liefert die Anwendung sie mit aus, ist alles
# gleich-origin: kein CORS, kein zweiter Hostname im Identity Provider, keine
# Weiterleitung zwischen zwei Diensten. Der Preis ist ein Bau, der beide
# Werkzeugketten braucht — und den nimmt dieses Bild auf sich.

# --- Die Oberfläche ---------------------------------------------------------
FROM node:22-alpine AS oberflaeche
WORKDIR /app

# Erst die Abhängigkeiten, dann der Quelltext: so bleibt die Schicht mit
# `npm ci` im Zwischenspeicher, solange sich nur der Quelltext ändert.
COPY app/package.json app/package-lock.json ./
RUN npm ci

COPY app/ ./
RUN npm run build

# --- Die Anwendung ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS anwendung
WORKDIR /src

# Dasselbe Vorgehen: die Wiederherstellung hängt an den Projektdateien und den
# zentral gepflegten Paketversionen, nicht am Quelltext.
COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY src/ src/
RUN dotnet restore src/TennisTurnier.Api

RUN dotnet publish src/TennisTurnier.Api \
    --no-restore \
    --configuration Release \
    --output /out

# --- Die Laufzeit -----------------------------------------------------------
#
# Bewusst nicht die Alpine-Variante: die Domäne führt Zeitzonen als IANA-Ids
# („Europe/Vienna"), und dafür braucht es ICU. Ohne die Bibliothek ließe sich
# kein Turnier anlegen.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=anwendung /out ./
COPY --from=oberflaeche /app/dist ./wwwroot

# Die Datenbank ist eine Datei. Sie liegt getrennt von der Anwendung, damit ein
# angehängter Datenträger genau hierher zeigen kann — ohne ihn überlebt sie den
# nächsten Start nicht.
RUN mkdir -p /data
ENV ConnectionStrings__Default="Data Source=/data/matchday.db"

# Die Plattform gibt den Port über PORT vor; 8080 ist der Rückfall für einen
# Start von Hand.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["/bin/sh", "-c", "exec dotnet TennisTurnier.Api.dll --urls http://+:${PORT:-8080}"]
