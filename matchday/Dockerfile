# MATCHDAY: ein Bild, ein Dienst. Die Oberfläche wird gebaut und neben die
# Anwendung gelegt, die sie ausliefert (ADR-0016).

FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props Matchday.slnx ./
COPY src/Matchday.Domain/Matchday.Domain.csproj src/Matchday.Domain/
COPY src/Matchday.Server/Matchday.Server.csproj src/Matchday.Server/
RUN dotnet restore src/Matchday.Server/Matchday.Server.csproj
COPY src/ src/
COPY --from=web /src/Matchday.Server/wwwroot/ src/Matchday.Server/wwwroot/
RUN dotnet publish src/Matchday.Server/Matchday.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
VOLUME /data
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ConnectionStrings__Default="Data Source=/data/matchday.db"
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s CMD wget -qO- http://localhost:8080/api/health || exit 1
ENTRYPOINT ["dotnet", "Matchday.Server.dll"]
