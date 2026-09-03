using System.Text.Json;
using System.Text.Json.Nodes;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die beiden Realm-Fassungen unter <c>deploy/keycloak/</c>.
///
/// Es gibt sie zweimal, weil die Entwicklung zwei Dinge braucht, die in keiner
/// erreichbaren Instanz etwas verloren haben: drei Konten, deren Passwort ihr
/// Benutzername ist, und den Direktzugang, über den die e2e-Tests ihr Token
/// holen. Beides lag einmal in derselben Datei, die das Produktionsbild
/// mitkopiert — wer die öffentliche Adresse kannte, brauchte danach einen
/// einzigen <c>curl</c> mit <c>grant_type=password</c>.
///
/// Zwei Dateien lösen das und schaffen ein neues Problem: sie laufen
/// auseinander. Diese Prüfungen sind die Klammer — die Produktionsfassung muss
/// leer bleiben an den beiden Stellen, und sonst müssen beide dasselbe sagen.
/// </summary>
public sealed class RealmDateiTests
{
    /// <summary>
    /// Der Weg vom Testverzeichnis zur Datei im Projekt.
    ///
    /// Gesucht wird die Projektmappe und nicht ein relativer Sprung über eine
    /// gezählte Zahl von Ebenen: die ändert sich mit jedem Umbau der
    /// Verzeichnisse, und zwar stumm.
    /// </summary>
    private static JsonObject Lies(string verzeichnis)
    {
        var wurzel = new DirectoryInfo(AppContext.BaseDirectory);

        while (wurzel is not null && !File.Exists(Path.Combine(wurzel.FullName, "TennisTurnier.slnx")))
        {
            wurzel = wurzel.Parent;
        }

        Assert.NotNull(wurzel);

        var pfad = Path.Combine(
            wurzel.FullName, "deploy", "keycloak", verzeichnis, "tennisturnier-realm.json");

        Assert.True(File.Exists(pfad), $"Die Realm-Datei fehlt unter deploy/keycloak/{verzeichnis}/.");

        var wurzelknoten = JsonNode.Parse(File.ReadAllText(pfad));
        Assert.NotNull(wurzelknoten);

        return wurzelknoten.AsObject();
    }

    private static JsonObject Produktion() => Lies("import");

    private static JsonObject Entwicklung() => Lies("import-dev");

    [Fact]
    public void Die_Produktionsfassung_bringt_keine_Konten_mit()
    {
        // Der eigentliche Fund: `deploy/keycloak/Dockerfile` kopiert genau diese
        // Datei ins Bild. Steht hier ein Konto, steht es in Produktion.
        var realm = Produktion();

        Assert.False(
            realm.ContainsKey("users"),
            "Die Produktionsfassung des Realms darf keine Benutzer mitbringen.");
    }

    [Fact]
    public void Die_Produktionsfassung_kennt_keinen_Direktzugang()
    {
        // Ohne ihn braucht ein Angriff auf ein Passwort den Browser und die
        // Anmeldemaske; mit ihm genügt eine Schleife über eine Wortliste. An
        // einem öffentlichen Client, der kein Geheimnis hat, ist das der
        // Unterschied zwischen mühsam und trivial.
        foreach (var client in Produktion()["clients"]!.AsArray())
        {
            Assert.False(
                client!["directAccessGrantsEnabled"]?.GetValue<bool>() ?? false,
                $"Der Client {client["clientId"]} darf in Produktion keinen Direktzugang haben.");
        }
    }

    [Fact]
    public void Die_Produktionsfassung_bremst_das_Durchprobieren_von_Passwoertern()
    {
        Assert.True(
            Produktion()["bruteForceProtected"]?.GetValue<bool>() ?? false,
            "Die Produktionsfassung des Realms sollte bruteForceProtected setzen.");
    }

    [Fact]
    public void Beide_Fassungen_verlangen_eine_bestaetigte_Adresse()
    {
        // Die Gegenprobe zu K1: die Anwendung übernimmt eine Adresse nur mit
        // bestätigtem email_verified, und der Realm ist die Stelle, an der die
        // Bestätigung überhaupt eingefordert wird. Ohne sie käme niemand mehr
        // an seine Einladungen.
        Assert.True(Produktion()["verifyEmail"]?.GetValue<bool>() ?? false);
        Assert.True(Entwicklung()["verifyEmail"]?.GetValue<bool>() ?? false);
    }

    [Fact]
    public void Die_Entwicklungsfassung_bringt_die_Testkonten_mit()
    {
        // Die andere Richtung: ohne sie stünde jeder e2e-Lauf ohne Anmeldung da,
        // und zwar mit einer Fehlermeldung, die auf Keycloak zeigt statt auf
        // diese Datei.
        var konten = Entwicklung()["users"]!.AsArray()
            .Select(u => u!["username"]!.GetValue<string>())
            .ToList();

        Assert.Equal(["systemadmin", "clubadmin", "referee"], konten);
    }

    [Fact]
    public void Die_Entwicklungsfassung_erlaubt_den_Direktzugang()
    {
        var client = Entwicklung()["clients"]!.AsArray()
            .Single(c => c!["clientId"]!.GetValue<string>() == "tennisturnier-api");

        Assert.True(client!["directAccessGrantsEnabled"]!.GetValue<bool>());
    }

    [Fact]
    public void Sonst_sagen_beide_Fassungen_dasselbe()
    {
        // Der Preis zweier Dateien ist, dass sie auseinanderlaufen — eine neue
        // Weiterleitungsadresse, die nur in einer von beiden landet, fällt sonst
        // erst in der Instanz auf, in der sie fehlt. Verglichen wird alles außer
        // dem, was sich unterscheiden darf.
        var produktion = Produktion();
        var entwicklung = Entwicklung();

        string[] duerfenAbweichen = ["users", "bruteForceProtected"];

        foreach (var name in duerfenAbweichen)
        {
            produktion.Remove(name);
            entwicklung.Remove(name);
        }

        // Der Direktzugang steht im Client und nicht oben — er wird für den
        // Vergleich auf beiden Seiten auf denselben Wert gesetzt.
        foreach (var realm in new[] { produktion, entwicklung })
        {
            foreach (var client in realm["clients"]!.AsArray())
            {
                client!["directAccessGrantsEnabled"] = false;
            }
        }

        Assert.Equal(
            JsonSerializer.Serialize(produktion),
            JsonSerializer.Serialize(entwicklung));
    }
}
