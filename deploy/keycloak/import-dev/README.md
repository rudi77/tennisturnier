# Der Realm für die Entwicklung

Dieselbe Realm-Definition wie in `../import/`, mit genau zwei Zusätzen:

- **die drei Testkonten** `systemadmin`, `clubadmin`, `referee`, deren Passwort
  jeweils ihr Benutzername ist;
- **`directAccessGrantsEnabled`** am Client `tennisturnier-api`, damit sich die
  e2e-Tests ihr Token über den Direktzugang holen können, statt jedes Mal durch
  die Anmeldemaske zu gehen.

Beides gehört in keine erreichbare Instanz, und genau daran lag der Fehler, den
diese Aufteilung behebt: die Datei mit den Konten wurde vom Produktionsbild
mitkopiert. Wer die öffentliche Adresse kannte, brauchte danach einen einzigen
`curl` mit `grant_type=password` und `systemadmin/systemadmin`.

`docker-compose.yml` hängt dieses Verzeichnis ein, `deploy/keycloak/Dockerfile`
das andere. `RealmDateiTests` hält beide Fassungen aneinander: sie dürfen sich
nur in diesen beiden Punkten unterscheiden, und die Produktionsfassung muss
ohne Konten und ohne Direktzugang bleiben.
