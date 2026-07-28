using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Gruppenphase mit anschließendem K.o. über die API — die Abnahmebedingung für
/// M5.
///
/// Der Punkt daran ist nicht, dass es zwei Phasen gibt, sondern dass es dafür
/// kein eigenes Format braucht: „group-then-ko" ist eine Komposition aus einer
/// Round-Robin- und einer K.-o.-Phase, und der Übergang zwischen ihnen ist
/// derselbe Mechanismus wie der vom Viertel- ins Halbfinale (ADR-0001).
/// </summary>
public sealed class GroupThenKnockoutApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public GroupThenKnockoutApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>16 Teilnehmer, vier Gruppen, die besten zwei kommen weiter.</summary>
    private async Task<(HttpClient Client, Guid TournamentId)> DrawnAsync(int participants = 16)
    {
        await _factory.GrantAsync("gk-admin", Role.SystemAdmin, ResourceScope.Global);
        var client = _factory.CreateClientAs("gk-admin");

        var clubId = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest($"TC Gruppen {Guid.NewGuid():N}", "Europe/Vienna", null),
            Json));

        var templates = await client.GetFromJsonAsync<List<FormatTemplateSummary>>(
            $"/api/clubs/{clubId}/format-templates", Json);
        var templateId = templates!.Single(t => t.Name == BuiltInFormats.GroupThenKnockout.Name).Id;

        var tournamentId = await CreatedIdAsync(await client.PostAsJsonAsync(
            $"/api/clubs/{clubId}/tournaments",
            new CreateTournamentRequest(
                "Clubmeisterschaft", new DateOnly(2026, 5, 16), new DateOnly(2026, 5, 17), templateId),
            Json));

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);

        for (var i = 1; i <= participants; i++)
        {
            var playerId = await CreatedIdAsync(await client.PostAsJsonAsync(
                "/api/players",
                new CreatePlayerRequest($"Vorname{i:00}", $"N{Guid.NewGuid():N}"[..10], null, null, null),
                Json));

            var participant = await (await client.PostAsJsonAsync(
                "/api/participants", new CreateParticipantRequest(playerId, null), Json))
                .Content.ReadFromJsonAsync<ParticipantSummary>(Json);

            var entryId = await CreatedIdAsync(await client.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/entries",
                new EnterTournamentRequest(participant!.Id, i),
                Json));

            await client.PostAsync($"/api/tournaments/{tournamentId}/entries/{entryId}/accept", null);
        }

        await client.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/tournaments/{tournamentId}/draw", null)).StatusCode);

        return (client, tournamentId);
    }

    private static async Task<List<PhaseDetail>> PhasesAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json))!;

    /// <summary>
    /// Spielt alle offenen Matches der Gruppenphase aus. Gewonnen hat immer die
    /// Seite mit der besseren Setzung — damit ist die Tabelle vorhersagbar.
    /// </summary>
    private static async Task PlayGroupsAsync(HttpClient client, Guid tournamentId)
    {
        var groups = (await PhasesAsync(client, tournamentId)).Single(p => p.Ordinal == 1);

        foreach (var match in groups.Matches.Where(m => m.Status == MatchStatus.Ready))
        {
            // Setzung 1 ist die stärkste; sie steht auf Seite 1, wenn ihr Name
            // alphabetisch vorne liegt — die Namen sind aufsteigend nummeriert.
            var side = string.CompareOrdinal(match.Side1.ParticipantName, match.Side2.ParticipantName) < 0 ? 1 : 2;

            var response = await client.PutAsJsonAsync(
                $"/api/matches/{match.Id}/result",
                new RecordResultRequest(
                    MatchOutcome.Normal,
                    side == 1
                        ? [new SetScore(6, 4), new SetScore(6, 2)]
                        : [new SetScore(4, 6), new SetScore(2, 6)]),
                Json);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    [Fact]
    public async Task Beim_Auslosen_entstehen_beide_Phasen()
    {
        var (client, tournamentId) = await DrawnAsync();
        var phases = await PhasesAsync(client, tournamentId);

        Assert.Equal(2, phases.Count);
        Assert.Equal("Gruppenphase", phases[0].Name);
        Assert.Equal("Endrunde", phases[1].Name);

        // Vier Gruppen zu je vier: 6 Spiele je Gruppe.
        Assert.Equal(24, phases[0].Matches.Count);
        Assert.Equal(4, phases[0].Matches.Select(m => m.Group).Distinct().Count());
    }

    [Fact]
    public async Task Die_Endrunde_steht_bevor_die_Gruppen_gespielt_sind()
    {
        // Der Kern von ADR-0001: das Bracket der Endrunde existiert, obwohl noch
        // niemand qualifiziert ist. Ohne den Summentyp gäbe es hier nichts zu
        // zeigen — und die öffentliche Ansicht bliebe bis zum letzten
        // Gruppenspiel leer.
        var (client, tournamentId) = await DrawnAsync();
        var final = (await PhasesAsync(client, tournamentId)).Single(p => p.Ordinal == 2);

        // Acht Qualifikanten: Viertelfinale, Halbfinale, Finale und Spiel um
        // Platz 3.
        Assert.Equal(8, final.Matches.Count);
        Assert.All(final.Matches, match => Assert.Null(match.Side1.EntryId));

        var quarterFinals = final.Matches.Where(m => m.Round == 1).ToList();
        Assert.All(quarterFinals, match =>
        {
            Assert.Contains("der Gruppe", match.Side1.Origin, StringComparison.Ordinal);
            Assert.Contains("der Gruppe", match.Side2.Origin, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Kein_Viertelfinale_wiederholt_eine_Gruppenpaarung()
    {
        // Der Zweck der Überkreuzung: ein Gruppensieger trifft auf den Zweiten
        // einer anderen Gruppe. Läuft die Zuordnung der Setzungen falsch herum,
        // spielen A1 und A2 sofort wieder gegeneinander — und die Gruppenphase
        // war für dieses Paar umsonst.
        var (client, tournamentId) = await DrawnAsync();
        var final = (await PhasesAsync(client, tournamentId)).Single(p => p.Ordinal == 2);

        foreach (var match in final.Matches.Where(m => m.Round == 1))
        {
            Assert.NotEqual(GroupOf(match.Side1.Origin), GroupOf(match.Side2.Origin));
        }
    }

    private static string GroupOf(string origin) => origin[(origin.IndexOf("der ", StringComparison.Ordinal) + 4)..];

    [Fact]
    public async Task Die_Tabelle_der_Gruppenphase_ordnet_nach_Punkten()
    {
        var (client, tournamentId) = await DrawnAsync();
        await PlayGroupsAsync(client, tournamentId);

        var groups = (await PhasesAsync(client, tournamentId)).Single(p => p.Ordinal == 1);
        var standings = await client.GetFromJsonAsync<StandingsDetail>(
            $"/api/tournaments/{tournamentId}/phases/{groups.Id}/standings", Json);

        Assert.Equal(16, standings!.Places.Count);

        foreach (var group in standings.Places.GroupBy(p => p.Group))
        {
            var ordered = group.OrderBy(p => p.Rank).ToList();

            Assert.Equal(6, ordered[0].Points);
            Assert.Equal(0, ordered[^1].Points);
            Assert.All(ordered, place => Assert.Equal(3, place.Played));
            Assert.Equal([1, 2, 3, 4], ordered.Select(p => p.Rank - ordered[0].Rank + 1));
        }
    }

    [Fact]
    public async Task Nach_der_Gruppenphase_stehen_die_Qualifizierten_in_der_Endrunde()
    {
        var (client, tournamentId) = await DrawnAsync();
        await PlayGroupsAsync(client, tournamentId);

        var phases = await PhasesAsync(client, tournamentId);
        var final = phases.Single(p => p.Ordinal == 2);
        var quarterFinals = final.Matches.Where(m => m.Round == 1).ToList();

        Assert.All(quarterFinals, match =>
        {
            Assert.NotNull(match.Side1.EntryId);
            Assert.NotNull(match.Side2.EntryId);
            Assert.Equal(MatchStatus.Ready, match.Status);
        });

        // Acht verschiedene Teilnehmer, keiner doppelt.
        var qualified = quarterFinals
            .SelectMany(m => new[] { m.Side1.EntryId!.Value, m.Side2.EntryId!.Value })
            .ToList();

        Assert.Equal(8, qualified.Distinct().Count());
    }

    [Fact]
    public async Task Die_Herkunft_bleibt_neben_dem_aufgeloesten_Teilnehmer_stehen()
    {
        // Ginge sie verloren, wüsste nach einer Korrektur niemand mehr, dass
        // dieser Platz „Erster der Gruppe A" war.
        var (client, tournamentId) = await DrawnAsync();
        await PlayGroupsAsync(client, tournamentId);

        var final = (await PhasesAsync(client, tournamentId)).Single(p => p.Ordinal == 2);
        var match = final.Matches.First(m => m.Round == 1);

        Assert.NotNull(match.Side1.EntryId);
        Assert.Contains("der Gruppe", match.Side1.Origin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Das_Turnier_laesst_sich_bis_zum_Finale_durchspielen()
    {
        var (client, tournamentId) = await DrawnAsync();
        await PlayGroupsAsync(client, tournamentId);

        for (var round = 1; round <= 3; round++)
        {
            var final = (await PhasesAsync(client, tournamentId)).Single(p => p.Ordinal == 2);

            foreach (var match in final.Matches.Where(m => m.Status == MatchStatus.Ready))
            {
                var response = await client.PutAsJsonAsync(
                    $"/api/matches/{match.Id}/result",
                    new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
                    Json);

                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }
        }

        var phases = await PhasesAsync(client, tournamentId);

        Assert.All(phases, phase => Assert.Equal(PhaseStatus.Completed, phase.Status));

        var standings = await client.GetFromJsonAsync<StandingsDetail>(
            $"/api/tournaments/{tournamentId}/phases/{phases[1].Id}/standings", Json);

        Assert.Equal(8, standings!.Places.Count);
        Assert.Equal(1, standings.Places[0].Rank);
    }

    [Fact]
    public async Task Die_oeffentliche_Ansicht_zeigt_Gruppen_und_Endrunde()
    {
        var (client, tournamentId) = await DrawnAsync();
        await PlayGroupsAsync(client, tournamentId);

        var view = await _factory.CreateClient().GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

        var phases = view.GetProperty("phases").EnumerateArray().ToList();

        Assert.Equal(2, phases.Count);
        Assert.Equal(16, phases[0].GetProperty("standings").GetArrayLength());
        Assert.Equal(8, phases[1].GetProperty("matches").GetArrayLength());

        // Und die Endrunde trägt die Namen der Qualifizierten.
        var quarterFinal = phases[1].GetProperty("matches").EnumerateArray()
            .First(m => m.GetProperty("round").GetInt32() == 1);

        Assert.NotNull(quarterFinal.GetProperty("side1").GetProperty("name").GetString());
    }
}
