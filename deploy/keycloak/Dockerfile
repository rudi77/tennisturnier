# Keycloak für den Betrieb — der Gegenpart zu docker-compose.yml, das nur die
# lokale Entwicklung bedient.
#
# Der Unterschied ist nicht Bequemlichkeit: start-dev hält alles im Speicher
# und vergisst beim Neustart jeden Benutzer, jede Sitzung und jede Verknüpfung
# zu einem Google-Konto. Hier läuft Keycloak im Produktionsmodus gegen
# PostgreSQL, und der Realm wird beim ersten Start eingespielt.

# --- Bauen ------------------------------------------------------------------
#
# Keycloak entscheidet beim Bauen, welchen Datenbanktreiber es überhaupt
# mitnimmt. Wer das dem Start überlässt, zahlt es bei jedem Start.
FROM quay.io/keycloak/keycloak:26.4 AS bauen
ENV KC_DB=postgres
RUN /opt/keycloak/bin/kc.sh build

# --- Laufzeit ---------------------------------------------------------------
FROM quay.io/keycloak/keycloak:26.4
COPY --from=bauen /opt/keycloak/ /opt/keycloak/

# Derselbe Realm wie lokal. Was sich zwischen den Umgebungen unterscheidet —
# die Adresse von MATCHDAY und die Zugangsdaten für Google — steht darin als
# Platzhalter und kommt aus der Umgebung.
COPY import/tennisturnier-realm.json /opt/keycloak/data/import/tennisturnier-realm.json

ENV KC_DB=postgres

# Die Verschlüsselung endet beim Proxy der Plattform; ohne diese beiden Zeilen
# hielte Keycloak jede Anfrage für unverschlüsselt und schriebe seine
# Weiterleitungen und den Aussteller auf http.
ENV KC_HTTP_ENABLED=true
ENV KC_PROXY_HEADERS=xforwarded

# Den Hostnamen nimmt Keycloak aus der Anfrage. Fest eintragen müsste man ihn
# erst, wenn vor Keycloak etwas steht, das ihn verfälscht.
ENV KC_HOSTNAME_STRICT=false

EXPOSE 8080

# Den Port gibt die Plattform über PORT vor.
ENTRYPOINT ["/bin/sh", "-c", "exec /opt/keycloak/bin/kc.sh start --optimized --import-realm --http-port=${PORT:-8080}"]
