using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der zweite Weg ins Feld: eine Teilnehmerliste am Stück.
///
/// Wer sein Feld schon kennt — aus der Vereinsliste, aus dem Vorjahr, aus einer
/// Tabelle —, soll es nicht Zeile für Zeile abtippen müssen.
/// </summary>
public sealed class CsvImportApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public CsvImportApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static TurnierWunsch Offen(Discipline disziplin) => new()
    {
        Beginn = null,
        Ende = null,
        Platzzeiten = false,
        Spielplan = false,
        Teilnehmer = 0,
        Auslosen = false,
        Disziplin = disziplin,
    };

    /// <summary>
    /// Ein leeres Turnier mit offener Meldung — der Ausgangspunkt eines Imports.
    ///
    /// Der Aufbau öffnet die Meldung nur, wenn er Teilnehmer anzulegen hat; hier
    /// legt sie der Import an, und deshalb steht das Öffnen hier.
    /// </summary>
    private async Task<(HttpClient Admin, Guid Id)> TurnierAsync(Discipline disziplin = Discipline.Singles)
    {
        var aufbau = await _factory.NeuesTurnierAsync($"csv-{Guid.NewGuid():N}", Offen(disziplin));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await aufbau.Admin.PostAsync(
                $"/api/tournaments/{aufbau.TournamentId}/registration/open", null)).StatusCode);

        return (aufbau.Admin, aufbau.TournamentId);
    }

    private static async Task<ImportEntriesResult> ImportAsync(HttpClient client, Guid id, string csv)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/tournaments/{id}/entries/import", new ImportEntriesRequest(csv), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportEntriesResult>(Json))!;
    }

    private static async Task<List<EntryDetail>> EntriesAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<List<EntryDetail>>($"/api/tournaments/{id}/entries", Json))!;

    /// <summary>
    /// Kopfzeile und Semikolon — das, was aus deutschem Excel herauskommt.
    /// Importierte Meldungen stehen sofort im Feld: die Turnierleitung lädt
    /// ihre eigene Liste hoch und muss sie nicht sich selbst bestätigen.
    /// </summary>
    [Fact]
    public async Task Eine_Einzelliste_kommt_ins_Feld()
    {
        var (admin, id) = await TurnierAsync();

        var result = await ImportAsync(admin, id, """
            Vorname;Nachname;E-Mail;Telefon
            Anna;Müller;anna@example.invalid;+43 1 234
            Bea;Berger;;
            Chris;Christl
            """);

        Assert.Equal(3, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.Problems);

        var entries = await EntriesAsync(admin, id);
        Assert.Equal(3, entries.Count);
        Assert.All(entries, entry => Assert.Equal(EntryStatus.Accepted, entry.Status));
        Assert.Contains(entries, entry => entry.ParticipantName == "Müller, Anna");
    }

    /// <summary>
    /// Komma statt Semikolon, ohne Kopfzeile. Der Trennzeichenstreit ist der
    /// häufigste Grund, warum eine Liste beim ersten Versuch nicht durchgeht —
    /// er wird deshalb nicht dem Hochladenden überlassen.
    /// </summary>
    [Fact]
    public async Task Das_Trennzeichen_wird_erraten()
    {
        var (admin, id) = await TurnierAsync();

        var result = await ImportAsync(admin, id, "Anna,Müller\nBea,Berger\n");

        Assert.Equal(2, result.Imported);
        Assert.Empty(result.Problems);
    }

    /// <summary>
    /// Im Doppel stehen die beiden Namenspaare vorn und die freiwilligen
    /// Angaben hinten — „Anna;Müller;Bea;Berger" ist ein vollständiges Doppel,
    /// ohne dass jemand leere Felder abzählen müsste.
    /// </summary>
    [Fact]
    public async Task Eine_Doppelliste_kommt_paarweise_ins_Feld()
    {
        var (admin, id) = await TurnierAsync(Discipline.Doubles);

        var result = await ImportAsync(admin, id, """
            Anna;Müller;Bea;Berger
            Carla;Christl;Dora;Danner;carla@example.invalid;dora@example.invalid;Die Netzroller
            """);

        Assert.Equal(2, result.Imported);
        Assert.Empty(result.Problems);

        // Der Teamname steht dem Paar voran und ersetzt es nicht: am Aushang
        // sucht man nach „Die Netzroller", am Platz nach den Namen.
        var entries = await EntriesAsync(admin, id);
        Assert.Contains(entries, entry => entry.ParticipantName == "Die Netzroller · Christl, Carla / Danner, Dora");
        Assert.Contains(entries, entry => entry.ParticipantName == "Müller, Anna / Berger, Bea");
    }

    /// <summary>
    /// Dieselbe Liste ein zweites Mal ist der Normalfall nach einer Korrektur.
    /// Sie darf nichts verdoppeln — und das ist kein Fehler, sondern ein
    /// eigener Ausgang im Bericht.
    /// </summary>
    [Fact]
    public async Task Derselbe_Import_ein_zweites_Mal_verdoppelt_nichts()
    {
        var (admin, id) = await TurnierAsync();
        const string csv = "Anna;Müller;anna@example.invalid\nBea;Berger;bea@example.invalid\n";

        Assert.Equal(2, (await ImportAsync(admin, id, csv)).Imported);

        var zweiter = await ImportAsync(admin, id, csv);

        Assert.Equal(0, zweiter.Imported);
        Assert.Equal(2, zweiter.Skipped);
        Assert.Equal(2, (await EntriesAsync(admin, id)).Count);
    }

    /// <summary>
    /// Dieselbe Aufstellung zweimal in <em>derselben</em> Datei.
    ///
    /// Der schwierigere der beiden Fälle: die erste der beiden Zeilen ist beim
    /// Prüfen der zweiten noch nicht gespeichert, sondern steht nur im
    /// Änderungsverfolger. Eine Abfrage fände sie nicht — der Index, der
    /// mitwächst, findet sie.
    /// </summary>
    [Fact]
    public async Task Zwei_gleiche_Zeilen_in_einer_Datei_ergeben_eine_Meldung()
    {
        var (admin, id) = await TurnierAsync();

        var result = await ImportAsync(admin, id, """
            Anna;Müller;anna@example.invalid
            Bea;Berger
            Anna;Müller;anna@example.invalid
            """);

        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(2, (await EntriesAsync(admin, id)).Count);
    }

    /// <summary>
    /// Wer dreißig Namen hochlädt und beim achtundzwanzigsten einen Tippfehler
    /// hat, will keine Absage für alle dreißig. Die krumme Zeile wird benannt,
    /// mit Nummer und Wortlaut — die Nummer allein hilft nicht, wenn die Datei
    /// inzwischen in einer Tabelle offen ist.
    /// </summary>
    [Fact]
    public async Task Eine_krumme_Zeile_kippt_die_Datei_nicht()
    {
        var (admin, id) = await TurnierAsync();

        var result = await ImportAsync(admin, id, """
            Anna;Müller
            ;Ohnevorname
            Bea;Berger
            """);

        Assert.Equal(2, result.Imported);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(2, problem.Line);
        Assert.Contains("Ohnevorname", problem.Text, StringComparison.Ordinal);
        Assert.Contains("Vorname", problem.Reason, StringComparison.Ordinal);

        Assert.Equal(2, (await EntriesAsync(admin, id)).Count);
    }

    /// <summary>
    /// Ein Einzelner in einer Doppelausschreibung. Die Zeile wird benannt, statt
    /// ihn als halbes Paar anzulegen.
    /// </summary>
    [Fact]
    public async Task Eine_Zeile_ohne_Partner_faellt_im_Doppel_auf()
    {
        var (admin, id) = await TurnierAsync(Discipline.Doubles);

        var result = await ImportAsync(admin, id, "Anna;Müller;Bea;Berger\nCarla;Christl\n");

        Assert.Equal(1, result.Imported);
        Assert.Single(result.Problems);
        Assert.Contains("Partner", result.Problems[0].Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Der Zustand wird einmal vorn geprüft und nicht je Zeile — sonst stünde
    /// derselbe Satz fünfzigmal im Bericht und der Grund ginge darin unter.
    /// </summary>
    [Fact]
    public async Task Ohne_offene_Meldung_sagt_der_Import_was_fehlt()
    {
        var (admin, id) = await TurnierAsync();
        await admin.PostAsync($"/api/tournaments/{id}/registration/close", null);

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{id}/entries/import",
            new ImportEntriesRequest("Anna;Müller"),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Meldung", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_leere_Datei_wird_benannt()
    {
        var (admin, id) = await TurnierAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{id}/entries/import",
            new ImportEntriesRequest("Vorname;Nachname\n\n   \n"),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>
    /// Ein Schiedsrichter trägt Ergebnisse ein und verändert das Feld nicht.
    /// </summary>
    [Fact]
    public async Task Ein_Schiedsrichter_importiert_nicht()
    {
        var (_, id) = await TurnierAsync();

        var referee = $"referee-{Guid.NewGuid():N}";
        await _factory.GrantAsync(referee, Role.Referee, ResourceScope.Tournament(id));

        var response = await _factory.CreateClientAs(referee).PostAsJsonAsync(
            $"/api/tournaments/{id}/entries/import",
            new ImportEntriesRequest("Anna;Müller"),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
