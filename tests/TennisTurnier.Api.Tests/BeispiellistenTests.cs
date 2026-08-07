using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die Beispiellisten unter <c>docs/beispiele/</c> gehen durch den Import.
///
/// Acht Teilnehmer und vier Paare, beides Zweierpotenzen — der Baum geht damit
/// ohne ein einziges Freilos auf. Bei zehn Teilnehmern sind es sechs, und dann
/// stehen sechs Namen in der zweiten Runde, ohne dass gespielt wurde. Fachlich
/// richtig, zum Ausprobieren aber die schlechtere Vorlage.
///
/// Sie sind das Erste, was jemand hochlädt, um zu sehen, ob die Sache
/// funktioniert — eine Beispieldatei, die selbst abgewiesen wird, ist
/// schlimmer als gar keine. Und sie sind zugleich die einzige Stelle, an der
/// das dokumentierte Spaltenformat als Datei existiert: läuft es und der Leser
/// auseinander, fällt es hier auf und nicht beim Veranstalter.
/// </summary>
public sealed class BeispiellistenTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public BeispiellistenTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>
    /// Der Weg vom Testverzeichnis zur Datei im Projekt.
    ///
    /// Gesucht wird die Projektmappe und nicht ein relativer Sprung über eine
    /// gezählte Zahl von Ebenen: die ändert sich mit jedem Umbau der
    /// Verzeichnisse, und zwar stumm.
    /// </summary>
    private static string BeispielPfad(string dateiname)
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

        while (verzeichnis is not null && !File.Exists(Path.Combine(verzeichnis.FullName, "TennisTurnier.slnx")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        Assert.NotNull(verzeichnis);

        var pfad = Path.Combine(verzeichnis.FullName, "docs", "beispiele", dateiname);
        Assert.True(File.Exists(pfad), $"Die Beispieldatei {dateiname} fehlt unter docs/beispiele/.");

        return pfad;
    }

    private async Task<(HttpClient Admin, Guid Id)> TurnierAsync(Discipline disziplin)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"beispiel-{Guid.NewGuid():N}",
            new TurnierWunsch
            {
                Beginn = null,
                Ende = null,
                Platzzeiten = false,
                Spielplan = false,
                Teilnehmer = 0,
                Auslosen = false,
                Disziplin = disziplin,
            });

        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/registration/open", null);

        return (aufbau.Admin, aufbau.TournamentId);
    }

    private static async Task<ImportEntriesResult> ImportAsync(HttpClient client, Guid id, string dateiname)
    {
        var csv = await File.ReadAllTextAsync(BeispielPfad(dateiname));

        var antwort = await client.PostAsJsonAsync(
            $"/api/tournaments/{id}/entries/import", new ImportEntriesRequest(csv), Json);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<ImportEntriesResult>(Json))!;
    }

    private static async Task<List<EntryDetail>> MeldungenAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<List<EntryDetail>>($"/api/tournaments/{id}/entries", Json))!;

    [Fact]
    public async Task Die_Einzelliste_ergibt_acht_Meldungen()
    {
        var (admin, id) = await TurnierAsync(Discipline.Singles);

        var bericht = await ImportAsync(admin, id, "teilnehmer-einzel.csv");

        Assert.Empty(bericht.Problems);
        Assert.Equal(8, bericht.Imported);

        var meldungen = await MeldungenAsync(admin, id);

        Assert.Equal(8, meldungen.Count);
        Assert.All(meldungen, meldung => Assert.Equal(EntryStatus.Accepted, meldung.Status));
        Assert.Contains(meldungen, meldung => meldung.ParticipantName == "Reiter, Anna");
    }

    [Fact]
    public async Task Die_Doppelliste_ergibt_vier_Paare()
    {
        var (admin, id) = await TurnierAsync(Discipline.Doubles);

        var bericht = await ImportAsync(admin, id, "teilnehmer-doppel.csv");

        Assert.Empty(bericht.Problems);
        Assert.Equal(4, bericht.Imported);

        var meldungen = await MeldungenAsync(admin, id);

        Assert.Equal(4, meldungen.Count);

        // Mit Teamname steht er dem Paar voran, ohne ihn stehen nur die Namen.
        Assert.Contains(meldungen, m => m.ParticipantName == "Die Netzroller · Reiter, Anna / Steiner, Bernhard");
        Assert.Contains(meldungen, m => m.ParticipantName == "Wallner, Eva / Aigner, Florian");
    }

    /// <summary>
    /// Die Einzelliste in ein Doppelturnier geladen — und umgekehrt. Beide Wege
    /// werden benannt, statt halbe Paare oder Namen in der E-Mail-Spalte
    /// anzulegen.
    /// </summary>
    [Fact]
    public async Task Die_falsche_Liste_wird_benannt_und_nicht_verarbeitet()
    {
        var (adminDoppel, doppel) = await TurnierAsync(Discipline.Doubles);
        var einzelInsDoppel = await ImportAsync(adminDoppel, doppel, "teilnehmer-einzel.csv");

        Assert.Equal(0, einzelInsDoppel.Imported);
        Assert.Equal(8, einzelInsDoppel.Problems.Count);

        var (adminEinzel, einzel) = await TurnierAsync(Discipline.Singles);
        var doppelInsEinzel = await ImportAsync(adminEinzel, einzel, "teilnehmer-doppel.csv");

        Assert.Equal(0, doppelInsEinzel.Imported);
        Assert.Equal(4, doppelInsEinzel.Problems.Count);
    }
}
