using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// „Jeder gegen jeden" mit kurzen Sätzen — der Nachmittag, für den das alles
/// gedacht ist.
///
/// Sechs Doppel, jeder gegen jeden, zwei Plätze, um sechs ist zu. Drei
/// Einstellungen bringen das zusammen, und alle drei sind hier geprüft: die
/// Vorlage „Jeder gegen jeden", Sätze bis vier statt bis sechs und der
/// Champions-Tiebreak statt eines dritten Satzes.
///
/// Der eigentliche Beweis steht in
/// <see cref="Ein_kurzer_Satz_wird_angenommen_ein_langer_nicht"/>: die
/// Einstellung ist erst dann etwas wert, wenn die Ergebniseingabe ihr folgt.
/// </summary>
public sealed class SatzformatApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sätze bis vier, Champions-Tiebreak statt des dritten.</summary>
    private static readonly MatchFormat Kurz =
        new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 4);

    private readonly TennisTurnierApiFactory _factory;

    public SatzformatApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>Ein Mixed-Doppel, jeder gegen jeden, mit dem gewünschten Satzformat.</summary>
    private async Task<AufgebautesTurnier> KurzesTurnierAsync(
        string benutzer,
        MatchFormat? satzformat = null,
        bool auslosen = true)
    {
        return await _factory.NeuesTurnierAsync(
            benutzer,
            new TurnierWunsch
            {
                Vorlage = BuiltInFormats.RoundRobin.Name,
                Anlage = "TC Kurzer Nachmittag",
                Name = "Mixed-Nachmittag",
                Disziplin = Domain.Tournaments.Discipline.Mixed,
                Teams = ["Nord", "Süd", "Ost", "West"],
                Satzformat = satzformat ?? Kurz,
                Auslosen = auslosen,
            });
    }

    private static async Task<List<PhaseDetail>> PhasesAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json))!;

    private static async Task<TournamentDetail> TournamentAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<TournamentDetail>($"/api/tournaments/{tournamentId}", Json))!;

    /// <summary>
    /// Jeder gegen jeden heißt: jede Paarung genau einmal. Bei vier Teilnehmern
    /// sind das sechs Matches — die Liga daneben spielte zwölf.
    /// </summary>
    [Fact]
    public async Task Jeder_gegen_jeden_setzt_jede_Paarung_genau_einmal_an()
    {
        var aufbau = await KurzesTurnierAsync("rr-admin");

        var phase = Assert.Single(await PhasesAsync(aufbau.Admin, aufbau.TournamentId));

        Assert.Equal(BuiltInFormats.RoundRobin.Phases[0].Name, phase.Name);
        Assert.Equal(6, phase.Matches.Count);

        // Keine Paarung doppelt, und niemand gegen sich selbst.
        var paarungen = phase.Matches
            .Select(m => string.CompareOrdinal(m.Side1.ParticipantName, m.Side2.ParticipantName) < 0
                ? (m.Side1.ParticipantName, m.Side2.ParticipantName)
                : (m.Side2.ParticipantName, m.Side1.ParticipantName))
            .ToList();

        Assert.Equal(paarungen.Count, paarungen.Distinct().Count());
    }

    /// <summary>Das Doppel ist eine Sache der Ausschreibung, nicht des Modus.</summary>
    [Fact]
    public async Task Jeder_gegen_jeden_geht_auch_im_Mixed()
    {
        var aufbau = await KurzesTurnierAsync("rr-mixed-admin");

        var tournament = await TournamentAsync(aufbau.Admin, aufbau.TournamentId);

        Assert.Equal(Domain.Tournaments.Discipline.Mixed, tournament.Discipline);

        var phase = Assert.Single(await PhasesAsync(aufbau.Admin, aufbau.TournamentId));
        Assert.All(phase.Matches, match => Assert.Contains('/', match.Side1.ParticipantName!));
    }

    [Fact]
    public async Task Das_eingestellte_Satzformat_steht_im_eingefrorenen_Format()
    {
        var aufbau = await KurzesTurnierAsync("sf-admin");

        var tournament = await TournamentAsync(aufbau.Admin, aufbau.TournamentId);

        Assert.Equal(Kurz, tournament.Format!.Definition.MatchFormat);
        Assert.Equal(Kurz, tournament.EffectiveMatchFormat);
    }

    /// <summary>
    /// Die Einstellung ist erst dann etwas wert, wenn die Ergebniseingabe ihr
    /// folgt. 4:1 ist unter Sätzen bis vier ein gewonnener Satz; 6:1 ist es
    /// nicht — und ohne die Einstellung wäre es genau umgekehrt.
    /// </summary>
    [Fact]
    public async Task Ein_kurzer_Satz_wird_angenommen_ein_langer_nicht()
    {
        var aufbau = await KurzesTurnierAsync("sf-eingabe-admin");
        var phase = Assert.Single(await PhasesAsync(aufbau.Admin, aufbau.TournamentId));
        var match = phase.Matches.First(m => m.Status == MatchStatus.Ready);

        var abgewiesen = await aufbau.Admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 1), new SetScore(6, 1)]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, abgewiesen.StatusCode);

        var angenommen = await aufbau.Admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(4, 1), new SetScore(4, 2)]),
            Json);

        Assert.Equal(HttpStatusCode.NoContent, angenommen.StatusCode);
    }

    /// <summary>
    /// Der dritte Satz ist keiner: er geht bis 10. Ein 4:1 an dieser Stelle
    /// wäre unter demselben Format ein gültiger erster Satz — die Stelle
    /// entscheidet.
    /// </summary>
    [Fact]
    public async Task Der_Entscheidungssatz_ist_ein_Champions_Tiebreak()
    {
        var aufbau = await KurzesTurnierAsync("sf-tiebreak-admin");
        var phase = Assert.Single(await PhasesAsync(aufbau.Admin, aufbau.TournamentId));
        var match = phase.Matches.First(m => m.Status == MatchStatus.Ready);

        var abgewiesen = await aufbau.Admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(
                MatchOutcome.Normal,
                [new SetScore(4, 1), new SetScore(2, 4), new SetScore(4, 1)]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, abgewiesen.StatusCode);

        var angenommen = await aufbau.Admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(
                MatchOutcome.Normal,
                [new SetScore(4, 1), new SetScore(2, 4), new SetScore(10, 7)]),
            Json);

        Assert.Equal(HttpStatusCode.NoContent, angenommen.StatusCode);
    }

    [Fact]
    public async Task Ohne_eigene_Angabe_gilt_das_Satzformat_der_Vorlage()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            "sf-vorlage-admin",
            new TurnierWunsch
            {
                Vorlage = BuiltInFormats.RoundRobin.Name,
                Anlage = "TC Vorlage",
                Teilnehmer = 4,
            });

        var tournament = await TournamentAsync(aufbau.Admin, aufbau.TournamentId);

        Assert.Null(tournament.MatchFormat);
        Assert.Equal(BuiltInFormats.RoundRobin.MatchFormat, tournament.EffectiveMatchFormat);
    }

    [Fact]
    public async Task Das_Satzformat_laesst_sich_bis_zur_Auslosung_aendern()
    {
        var aufbau = await KurzesTurnierAsync("sf-aendern-admin", auslosen: false);

        var response = await aufbau.Admin.PutAsJsonAsync(
            $"/api/tournaments/{aufbau.TournamentId}/match-format",
            new SetMatchFormatRequest(new MatchFormat(BestOf: 1, FinalSetMode.Regular, TiebreakAt: 8)),
            Json);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var tournament = await TournamentAsync(aufbau.Admin, aufbau.TournamentId);
        Assert.Equal(8, tournament.EffectiveMatchFormat.TiebreakAt);
        Assert.Equal(1, tournament.EffectiveMatchFormat.BestOf);
    }

    [Fact]
    public async Task Ein_leeres_Satzformat_nimmt_die_Einstellung_zurueck()
    {
        var aufbau = await KurzesTurnierAsync("sf-zuruecknehmen-admin", auslosen: false);

        await aufbau.Admin.PutAsJsonAsync(
            $"/api/tournaments/{aufbau.TournamentId}/match-format",
            new SetMatchFormatRequest(null),
            Json);

        var tournament = await TournamentAsync(aufbau.Admin, aufbau.TournamentId);

        Assert.Null(tournament.MatchFormat);
        Assert.Equal(BuiltInFormats.RoundRobin.MatchFormat, tournament.EffectiveMatchFormat);
    }

    /// <summary>
    /// Nach der Auslosung hinge an einer Änderung jedes bereits eingetragene
    /// Ergebnis — es wurde gegen genau dieses Format geprüft.
    /// </summary>
    [Fact]
    public async Task Nach_der_Auslosung_wird_eine_Aenderung_abgewiesen()
    {
        var aufbau = await KurzesTurnierAsync("sf-eingefroren-admin");

        var response = await aufbau.Admin.PutAsJsonAsync(
            $"/api/tournaments/{aufbau.TournamentId}/match-format",
            new SetMatchFormatRequest(new MatchFormat(BestOf: 3, FinalSetMode.Regular, TiebreakAt: 6)),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_ungueltiges_Satzformat_wird_abgewiesen()
    {
        var aufbau = await KurzesTurnierAsync("sf-ungueltig-admin", auslosen: false);

        var response = await aufbau.Admin.PutAsJsonAsync(
            $"/api/tournaments/{aufbau.TournamentId}/match-format",
            new SetMatchFormatRequest(new MatchFormat(BestOf: 2, FinalSetMode.Regular, TiebreakAt: 6)),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
