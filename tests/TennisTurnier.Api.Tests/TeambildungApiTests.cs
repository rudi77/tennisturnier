using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Registration;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der ganze Weg eines Doppels, dessen Paare die Turnierleitung bildet:
/// ausschreiben, sich einzeln melden, Teams stellen, auslosen.
///
/// Das ist der Schleiferl- oder Mixed-Abend, und er unterscheidet sich vom
/// Vereinsdoppel an genau einer Stelle — niemand bringt einen Partner mit. Alles
/// Weitere folgt daraus: das Meldeformular fragt keinen, der Import erwartet
/// eine Person je Zeile, und der Draw wartet, bis niemand mehr allein dasteht.
/// </summary>
public sealed class TeambildungApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public TeambildungApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private Task<AufgebautesTurnier> SchleiferlAsync(int teilnehmer, bool auslosen = false) =>
        _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch
            {
                Disziplin = Discipline.Doubles,
                Teambildung = TeamFormation.ByOrganiser,
                Teilnehmer = teilnehmer,
                Setzen = false,
                Auslosen = auslosen,
            });

    private static async Task<TournamentDetail> DetailAsync(AufgebautesTurnier turnier) =>
        (await turnier.Admin.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{turnier.TournamentId}", Json))!;

    private static async Task<string> ProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return problem.GetProperty("detail").GetString() ?? string.Empty;
    }

    private static async Task<Guid> TeamAsync(
        AufgebautesTurnier turnier,
        Guid erste,
        Guid zweite,
        string? name = null)
    {
        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/teams",
            new FormTeamRequest(erste, zweite, name),
            Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Ein_Doppel_mit_eigener_Teambildung_nimmt_Einzelmeldungen()
    {
        var turnier = await SchleiferlAsync(4);
        var detail = await DetailAsync(turnier);

        Assert.Equal(Discipline.Doubles, detail.Discipline);
        Assert.Equal(TeamFormation.ByOrganiser, detail.TeamFormation);
        Assert.Equal(4, detail.Entries.Count);
        Assert.All(detail.Entries, meldung => Assert.Null(meldung.TeamEntryId));
    }

    [Fact]
    public async Task Das_Meldeformular_fragt_keinen_Partner()
    {
        var turnier = await SchleiferlAsync(2);

        var link = await turnier.Admin.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{turnier.TournamentId}/registration", Json);

        var ansicht = await _factory.CreateClient()
            .GetFromJsonAsync<PublicRegistrationView>($"/public/registrations/{link!.Token}", Json);

        Assert.NotNull(ansicht);
        Assert.Equal(Discipline.Doubles, ansicht.Discipline);
        Assert.False(ansicht.NeedsPartner);
    }

    [Fact]
    public async Task Wer_sich_selbst_meldet_kommt_ohne_Partner_ins_Feld()
    {
        var turnier = await SchleiferlAsync(2);

        var link = await turnier.Admin.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{turnier.TournamentId}/registration", Json);

        var client = _factory.CreateClient();

        var allein = await client.PostAsJsonAsync(
            $"/public/registrations/{link!.Token}",
            new SelfRegistrationRequest(
                "Anna", "Neu", "anna.neu@example.invalid", null, null, null, null, null),
            Json);

        Assert.Equal(HttpStatusCode.OK, allein.StatusCode);

        // Und mit Partner nicht: er ginge beim Auslosen der Teams verloren.
        var mitPartner = await client.PostAsJsonAsync(
            $"/public/registrations/{link.Token}",
            new SelfRegistrationRequest(
                "Eva", "Zweit", "eva.zweit@example.invalid", null, "Lisa", "Dritt", null, null),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, mitPartner.StatusCode);
        Assert.Contains(
            "bildet die Turnierleitung die Teams",
            await ProblemAsync(mitPartner),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_Import_erwartet_eine_Person_je_Zeile()
    {
        // Im Vereinsdoppel stünde in Spalte drei und vier der Partner. Hier
        // steht dort die E-Mail — die Spaltenzuordnung folgt der Ausschreibung.
        var turnier = await SchleiferlAsync(2);

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/entries/import",
            new ImportEntriesRequest("Lisa;Dritt;lisa@example.invalid\nSara;Viert;sara@example.invalid\n"),
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bericht = await response.Content.ReadFromJsonAsync<ImportEntriesResult>(Json);
        Assert.Equal(2, bericht!.Imported);
        Assert.Empty(bericht.Problems);
    }

    [Fact]
    public async Task Von_Hand_gestellte_Teams_tragen_ihre_beiden_Namen()
    {
        var turnier = await SchleiferlAsync(2);
        var detail = await DetailAsync(turnier);
        var meldungen = detail.Entries.OrderBy(e => e.ParticipantName).ToList();

        var teamId = await TeamAsync(turnier, meldungen[0].Id, meldungen[1].Id, "Die Unbeugsamen");

        var danach = await DetailAsync(turnier);
        var team = danach.Entries.Single(e => e.Id == teamId);

        Assert.Equal(EntryStatus.Accepted, team.Status);
        Assert.Equal(
            $"Die Unbeugsamen · {meldungen[0].ParticipantName} / {meldungen[1].ParticipantName}",
            team.ParticipantName);

        // Die beiden Meldungen bleiben stehen und zeigen auf ihr Team.
        Assert.All(
            danach.Entries.Where(e => e.Id != teamId),
            meldung =>
            {
                Assert.Equal(EntryStatus.Paired, meldung.Status);
                Assert.Equal(teamId, meldung.TeamEntryId);
            });
    }

    [Fact]
    public async Task Ohne_eigenen_Namen_heisst_ein_Team_nach_seinen_Meldungen()
    {
        var turnier = await SchleiferlAsync(2);
        var meldungen = (await DetailAsync(turnier)).Entries.OrderBy(e => e.ParticipantName).ToList();

        var teamId = await TeamAsync(turnier, meldungen[0].Id, meldungen[1].Id);

        var team = (await DetailAsync(turnier)).Entries.Single(e => e.Id == teamId);

        Assert.Equal(
            $"{meldungen[0].ParticipantName} / {meldungen[1].ParticipantName}",
            team.ParticipantName);
    }

    [Fact]
    public async Task Das_Los_stellt_alle_offenen_Meldungen_zusammen()
    {
        var turnier = await SchleiferlAsync(6);

        var response = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/teams/draw", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ergebnis = await response.Content.ReadFromJsonAsync<DrawTeamsResult>(Json);

        Assert.Equal(3, ergebnis!.Formed);
        Assert.Equal(0, ergebnis.LeftOver);

        var danach = await DetailAsync(turnier);
        var teams = danach.Entries.Where(e => e.Status == EntryStatus.Accepted).ToList();
        var gepaart = danach.Entries.Where(e => e.Status == EntryStatus.Paired).ToList();

        Assert.Equal(3, teams.Count);
        Assert.Equal(6, gepaart.Count);

        // Jede Meldung steckt in genau einem Team, und jedes Team hat zwei.
        Assert.All(gepaart, meldung => Assert.Contains(teams, team => team.Id == meldung.TeamEntryId));
        Assert.All(teams, team => Assert.Equal(2, gepaart.Count(m => m.TeamEntryId == team.Id)));
    }

    [Fact]
    public async Task Bei_ungerader_Zahl_bleibt_eine_Meldung_uebrig()
    {
        var turnier = await SchleiferlAsync(5);

        var response = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/teams/draw", null);

        var ergebnis = await response.Content.ReadFromJsonAsync<DrawTeamsResult>(Json);

        Assert.Equal(2, ergebnis!.Formed);
        Assert.Equal(1, ergebnis.LeftOver);

        // Und der Draw sagt, woran es liegt, statt sie einzeln anzusetzen.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await turnier.Admin.PostAsync(
                $"/api/tournaments/{turnier.TournamentId}/registration/close", null)).StatusCode);

        var auslosen = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/draw", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, auslosen.StatusCode);
        Assert.Contains("ohne Team", await ProblemAsync(auslosen), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_zweites_Los_laesst_die_bestehenden_Teams_stehen()
    {
        var turnier = await SchleiferlAsync(4);

        await turnier.Admin.PostAsync($"/api/tournaments/{turnier.TournamentId}/teams/draw", null);

        var zweites = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/teams/draw", null);

        var ergebnis = await zweites.Content.ReadFromJsonAsync<DrawTeamsResult>(Json);

        Assert.Equal(0, ergebnis!.Formed);
        Assert.Equal(0, ergebnis.LeftOver);
        Assert.Equal(2, (await DetailAsync(turnier)).Entries.Count(e => e.Status == EntryStatus.Accepted));
    }

    [Fact]
    public async Task Ein_Team_laesst_sich_wieder_aufloesen()
    {
        var turnier = await SchleiferlAsync(2);
        var meldungen = (await DetailAsync(turnier)).Entries.ToList();
        var teamId = await TeamAsync(turnier, meldungen[0].Id, meldungen[1].Id);

        var response = await turnier.Admin.DeleteAsync(
            $"/api/tournaments/{turnier.TournamentId}/teams/{teamId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var danach = await DetailAsync(turnier);

        Assert.DoesNotContain(danach.Entries, e => e.Id == teamId);
        Assert.All(danach.Entries, meldung =>
        {
            Assert.Equal(EntryStatus.Accepted, meldung.Status);
            Assert.Null(meldung.TeamEntryId);
        });
    }

    [Fact]
    public async Task Ein_ausgelostes_Feld_laesst_sich_auslosen_und_ausspielen()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch
            {
                Disziplin = Discipline.Doubles,
                Teambildung = TeamFormation.ByOrganiser,
                Teilnehmer = 8,
                Setzen = false,
                TeamsAuslosen = true,
            });

        var phasen = await turnier.Admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{turnier.TournamentId}/phases", Json);

        var phase = Assert.Single(phasen!);

        // Acht Meldungen ergeben vier Teams: zwei Halbfinale, ein Finale und
        // das Spiel um Platz drei aus der mitgelieferten Vorlage.
        Assert.Equal(4, phase.Matches.Count);

        // Und im Baum stehen die Teams, nicht die Einzelnen: jeder Name nennt
        // zwei Menschen.
        var erste = phase.Matches.Where(m => m.Round == 1).ToList();
        Assert.Equal(2, erste.Count);
        Assert.All(
            erste,
            match =>
            {
                Assert.Contains(" / ", match.Side1.ParticipantName!, StringComparison.Ordinal);
                Assert.Contains(" / ", match.Side2.ParticipantName!, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Wo_sich_die_Paare_selbst_melden_gibt_es_keine_Teambildung()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teams = ["Erste", "Zweite"], Auslosen = false });

        var meldungen = (await DetailAsync(turnier)).Entries.ToList();

        var vonHand = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/teams",
            new FormTeamRequest(meldungen[0].Id, meldungen[1].Id, null),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, vonHand.StatusCode);
        Assert.Contains("melden sich die Paare selbst", await ProblemAsync(vonHand), StringComparison.Ordinal);

        var ausgelost = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/teams/draw", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ausgelost.StatusCode);
    }

    [Fact]
    public async Task Eine_Meldung_die_es_nicht_gibt_kommt_in_kein_Team()
    {
        var turnier = await SchleiferlAsync(2);
        var meldungen = (await DetailAsync(turnier)).Entries.ToList();

        var response = await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/teams",
            new FormTeamRequest(meldungen[0].Id, Guid.NewGuid(), null),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Teams_stellt_nur_zusammen_wer_das_Turnier_verwaltet()
    {
        var turnier = await SchleiferlAsync(2);
        var meldungen = (await DetailAsync(turnier)).Entries.ToList();
        var fremder = _factory.CreateClientAs($"fremder-{Guid.NewGuid():N}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fremder.PostAsJsonAsync(
                $"/api/tournaments/{turnier.TournamentId}/teams",
                new FormTeamRequest(meldungen[0].Id, meldungen[1].Id, null),
                Json)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fremder.PostAsync(
                $"/api/tournaments/{turnier.TournamentId}/teams/draw", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fremder.DeleteAsync(
                $"/api/tournaments/{turnier.TournamentId}/teams/{Guid.NewGuid()}")).StatusCode);
    }
}
