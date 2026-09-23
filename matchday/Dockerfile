# MATCHDAY: ein Bild, ein Dienst. Die Oberfläche wird gebaut und neben die
# Anwendung gelegt, die sie ausliefert (ADR-0016).
#
# Der Kontext ist die Wurzel des Repositories, nicht dieses Verzeichnis. Railway
# baut den Dienst aus dem Wurzelverzeichnis (`railway.json` dort nennt diese
# Datei), und ein Dockerfile kann nicht aus seinem Kontext hinausgreifen. Von
# Hand also:
#
#     docker build -f matchday/Dockerfile -t matchday .

FROM node:22-alpine AS web
WORKDIR /web
COPY matchday/web/package.json matchday/web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY matchday/web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY matchday/Directory.Build.props matchday/Directory.Packages.props matchday/Matchday.slnx ./
COPY matchday/src/Matchday.Domain/Matchday.Domain.csproj src/Matchday.Domain/
COPY matchday/src/Matchday.Server/Matchday.Server.csproj src/Matchday.Server/
RUN dotnet restore src/Matchday.Server/Matchday.Server.csproj
COPY matchday/src/ src/
COPY --from=web /src/Matchday.Server/wwwroot/ src/Matchday.Server/wwwroot/
RUN dotnet publish src/Matchday.Server/Matchday.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
# Die Datenbank ist eine Datei und liegt getrennt von der Anwendung, damit ein
# angehängter Datenträger genau hierher zeigen kann. Nur angelegt, nicht als
# VOLUME deklariert: Railways Builder weist ein VOLUME zurück und verlangt
# stattdessen einen Datenträger, der im Dienst konfiguriert wird. Ohne ihn
# überlebt die Datenbank den nächsten Start nicht.
RUN mkdir -p /data
ENV ConnectionStrings__Default="Data Source=/data/matchday.db"
# Die Schlüssel des Sitzungs-Cookies gehören ebenfalls auf den Datenträger —
# sonst ist nach jedem Deploy jede Anmeldung ungültig (ADR-0025).
ENV Auth__KeysPath=/data/keys
EXPOSE 8080

# Den Port gibt die Plattform über PORT vor; 8080 ist der Rückfall für einen
# Start von Hand. Ein fest verdrahtetes ASPNETCORE_URLS würde an Railway
# vorbeihören.
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet Matchday.Server.dll --urls http://+:${PORT:-8080}"]
