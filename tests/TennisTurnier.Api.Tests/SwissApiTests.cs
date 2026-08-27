using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Das Schweizer System über die API — die Abnahmebedingung für M8.
///
/// Es ist das einzige Format, dessen Draw beim Auslosen unvollständig ist: nur
/// die erste Runde steht. Jede weitere entsteht aus der Tabelle, sobald die
/// vorige gespielt ist — über denselben Weg, über den eine Gruppenphase ihre
/// Endrunde besetzt (ADR-0001).
/// </summary>
public sealed class SwissApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public SwissApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, Guid TournamentId)> DrawnAsync(int participants = 8)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"swiss-{Guid.NewGuid():N}"[..20],
            new TurnierWunsch
            {
                Vorlage = BuiltInFormats.Swiss.Name,
                Name = "Schweizer Turnier",
                Anlage = "TC Schweiz",
                Teilnehmer = participants,
                Oeffentlich = true,
            });

        return (aufbau.Admin, aufbau.TournamentId);
    }

    private static async Task<PhaseDetail> PhaseAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json))!.Single();

    /// <summary>
    /// Spielt eine Runde aus. Gewonnen hat, wer alphabetisch vorne steht — die
    /// Namen sind nach Setzung nummeriert, damit ist die Tabelle vorhersagbar.
    /// </summary>
    private static async Task PlayRoundAsync(HttpClient client, Guid tournamentId, int round)
    {
        var phase = await PhaseAsync(client, tournamentId);

        foreach (var match in phase.Matches.Where(m => m.Round == round && m.Status == MatchStatus.Ready))
        {
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
    public async Task Beim_Auslosen_steht_nur_die_erste_Runde()
    {
        // Der Unterschied zu allen anderen Formaten: die zweite Runde lässt sich
        // nicht auslosen, weil sie davon abhängt, wie die erste ausgeht. Ein
        // Draw, der sie trotzdem zeigte, zeigte eine Erfindung.
        var (client, tournamentId) = await DrawnAsync();
        var phase = await PhaseAsync(client, tournamentId);

        Assert.Equal(4, phase.Matches.Count);
        Assert.All(phase.Matches, match => Assert.Equal(1, match.Round));
        Assert.All(phase.Matches, match => Assert.Equal(MatchStatus.Ready, match.Status));
    }

    [Fact]
    public async Task Jede_Runde_entsteht_erst_nach_der_vorigen()
    {
        var (client, tournamentId) = await DrawnAsync();

        // 8 Teilnehmer, keine Rundenzahl in der Vorlage: ceil(log2(8)) = 3.
        for (var round = 1; round <= 3; round++)
        {
            var before = await PhaseAsync(client, tournamentId);
            Assert.Equal(round * 4, before.Matches.Count);

            await PlayRoundAsync(client, tournamentId, round);
        }

        var phase = await PhaseAsync(client, tournamentId);

        Assert.Equal(12, phase.Matches.Count);
        Assert.All(phase.Matches, match => Assert.Equal(MatchStatus.Finished, match.Status));
    }

    [Fact]
    public async Task Ueber_alle_Runden_hinweg_trifft_niemand_zweimal_auf_denselben_Gegner()
    {
        var (client, tournamentId) = await DrawnAsync();

        for (var round = 1; round <= 3; round++)
        {
            await PlayRoundAsync(client, tournamentId, round);
        }

        var phase = await PhaseAsync(client, tournamentId);

        var pairs = phase.Matches
            .Select(m => (m.Side1.EntryId!.Value, m.Side2.EntryId!.Value))
            .Select(p => p.Item1.CompareTo(p.Item2) < 0 ? p : (p.Item2, p.Item1))
            .ToList();

        Assert.Equal(12, pairs.Count);
        Assert.Equal(pairs.Count, pairs.Distinct().Count());
    }

    [Fact]
    public async Task Das_Turnier_ist_erst_nach_der_letzten_Runde_beendet()
    {
        var (client, tournamentId) = await DrawnAsync();

        await PlayRoundAsync(client, tournamentId, 1);

        // Alle angelegten Matches sind entschieden. Wäre das schon das Ende,
        // stünde nach der ersten Runde ein Turniersieger fest.
        var afterFirst = await client.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(TournamentState.InProgress, afterFirst!.State);

        await PlayRoundAsync(client, tournamentId, 2);
        await PlayRoundAsync(client, tournamentId, 3);

        var phase = await PhaseAsync(client, tournamentId);
        Assert.Equal(12, phase.Matches.Count);
        Assert.All(phase.Matches, match => Assert.Equal(MatchStatus.Finished, match.Status));

        // Und erst jetzt ist es beendet. Der Unterschied zur ersten Runde ist
        // der Grund, warum die Frage „ist alles gespielt" an das Format geht und
        // nicht an die angelegten Matches: nach jeder Runde sind alle
        // entschieden, und nur das Format weiß, dass noch welche folgen.
        var afterLast = await client.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(TournamentState.Completed, afterLast!.State);
    }

    [Fact]
    public async Task Die_Tabelle_ordnet_nach_Punkten_und_dann_nach_Buchholz()
    {
        var (client, tournamentId) = await DrawnAsync();

        for (var round = 1; round <= 3; round++)
        {
            await PlayRoundAsync(client, tournamentId, round);
        }

        var phase = await PhaseAsync(client, tournamentId);
        var standings = await client.GetFromJsonAsync<StandingsDetail>(
            $"/api/tournaments/{tournamentId}/phases/{phase.Id}/standings", Json);

        var places = standings!.Places;

        Assert.Equal(8, places.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], places.Select(p => p.Rank));
        Assert.All(places, place => Assert.Equal(3, place.Played));

        // Punkte fallen monoton — die Vorlage vergibt einen Punkt je Sieg.
        Assert.Equal(places.Select(p => p.Points).OrderByDescending(v => v), places.Select(p => p.Points));
        Assert.Equal(3, places[0].Points);
        Assert.Equal(0, places[^1].Points);
    }

    /// <summary>
    /// Bei ungerader Teilnehmerzahl setzt jede Runde einer aus. Das Freilos
    /// zählt wie ein Sieg — sonst fiele zurück, wer nichts dafür kann.
    /// </summary>
    [Fact]
    public async Task Bei_ungerader_Teilnehmerzahl_bekommt_jede_Runde_einer_ein_Freilos()
    {
        var (client, tournamentId) = await DrawnAsync(participants: 7);

        var resting = new List<Guid>();

        for (var round = 1; round <= 3; round++)
        {
            var phase = await PhaseAsync(client, tournamentId);
            var bye = Assert.Single(phase.Matches, m => m.Round == round && m.Side2.Origin == "Freilos");

            resting.Add(bye.Side1.EntryId!.Value);
            Assert.Equal(MatchStatus.Finished, bye.Status);

            await PlayRoundAsync(client, tournamentId, round);
        }

        Assert.Equal(3, resting.Distinct().Count());
    }

    /// <summary>
    /// Der Grund, warum das Schweizer System überhaupt ein eigenes Format ist:
    /// die Paarung folgt der Tabelle. Wer gewonnen hat, trifft in der nächsten
    /// Runde auf jemanden, der ebenfalls gewonnen hat.
    /// </summary>
    [Fact]
    public async Task Die_zweite_Runde_paart_Sieger_gegen_Sieger()
    {
        var (client, tournamentId) = await DrawnAsync();
        await PlayRoundAsync(client, tournamentId, 1);

        var phase = await PhaseAsync(client, tournamentId);
        var winners = phase.Matches
            .Where(m => m.Round == 1)
            .Select(m => (m.Score!.WinnerSide == 1 ? m.Side1 : m.Side2).EntryId!.Value)
            .ToHashSet();

        foreach (var match in phase.Matches.Where(m => m.Round == 2))
        {
            var first = winners.Contains(match.Side1.EntryId!.Value);
            var second = winners.Contains(match.Side2.EntryId!.Value);

            Assert.Equal(first, second);
        }
    }

    /// <summary>
    /// Eine Korrektur ändert die Tabelle — und damit die Paarung der Runden, die
    /// aus ihr entstanden sind. Sie stehen zu lassen hieße, mit Paarungen
    /// weiterzuspielen, die niemand mehr herleiten kann.
    /// </summary>
    [Fact]
    public async Task Eine_Korrektur_setzt_die_Folgerunde_neu_an()
    {
        var (client, tournamentId) = await DrawnAsync();
        await PlayRoundAsync(client, tournamentId, 1);

        var before = await PhaseAsync(client, tournamentId);
        var second = before.Matches.Where(m => m.Round == 2).Select(m => m.Id).ToList();
        Assert.Equal(4, second.Count);

        var corrected = before.Matches.First(m => m.Round == 1);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/matches/{corrected.Id}/result")).StatusCode);

        // Die zweite Runde ist verschwunden: ihre Grundlage ist offen.
        var between = await PhaseAsync(client, tournamentId);
        Assert.DoesNotContain(between.Matches, m => m.Round == 2);

        // Mit dem anderen Ausgang entsteht sie neu — und mit anderen Paarungen.
        await client.PutAsJsonAsync(
            $"/api/matches/{corrected.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(4, 6), new SetScore(2, 6)]),
            Json);

        var after = await PhaseAsync(client, tournamentId);
        var rebuilt = after.Matches.Where(m => m.Round == 2).Select(m => m.Id).ToList();

        Assert.Equal(4, rebuilt.Count);
        Assert.Empty(rebuilt.Intersect(second));
    }

    /// <summary>
    /// Auch die Korrektur durch Überschreiben, nicht nur die durch Rücknahme.
    ///
    /// Beim Überschreiben bleibt die Runde durchgehend „vollständig" — würde die
    /// Rücknahme der Folgerunden daran hängen, spielte das Turnier mit Paarungen
    /// weiter, die zur neuen Tabelle nicht mehr passen, und die beiden
    /// Korrekturwege verhielten sich unterschiedlich.
    /// </summary>
    [Fact]
    public async Task Auch_ein_ueberschriebenes_Ergebnis_setzt_die_Folgerunde_neu_an()
    {
        var (client, tournamentId) = await DrawnAsync();
        await PlayRoundAsync(client, tournamentId, 1);

        var before = await PhaseAsync(client, tournamentId);
        var second = before.Matches.Where(m => m.Round == 2).Select(m => m.Id).ToList();
        var corrected = before.Matches.First(m => m.Round == 1);

        // Dasselbe Match, umgedrehtes Ergebnis — ohne Rücknahme.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync(
                $"/api/matches/{corrected.Id}/result",
                new RecordResultRequest(MatchOutcome.Normal, [new SetScore(4, 6), new SetScore(2, 6)]),
                Json)).StatusCode);

        var after = await PhaseAsync(client, tournamentId);
        var rebuilt = after.Matches.Where(m => m.Round == 2).ToList();

        Assert.Equal(4, rebuilt.Count);
        Assert.Empty(rebuilt.Select(m => m.Id).Intersect(second));

        // Und die neue Runde passt zur neuen Tabelle: Punktgleiche gegen
        // Punktgleiche.
        var standings = await client.GetFromJsonAsync<StandingsDetail>(
            $"/api/tournaments/{tournamentId}/phases/{after.Id}/standings", Json);
        var points = standings!.Places.ToDictionary(p => p.EntryId, p => p.Points);

        Assert.All(
            rebuilt,
            match => Assert.Equal(points[match.Side1.EntryId!.Value], points[match.Side2.EntryId!.Value]));
    }

    [Fact]
    public async Task Eine_gespielte_Folgerunde_verhindert_die_Korrektur()
    {
        var (client, tournamentId) = await DrawnAsync();
        await PlayRoundAsync(client, tournamentId, 1);
        await PlayRoundAsync(client, tournamentId, 2);

        var phase = await PhaseAsync(client, tournamentId);
        var corrected = phase.Matches.First(m => m.Round == 1);

        var response = await client.DeleteAsync($"/api/matches/{corrected.Id}/result");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // Und der Stand bleibt, wie er war — die Absage darf nicht auf halbem
        // Weg passieren. Die dritte Runde steht bereits an, weil die zweite
        // gespielt ist; zurückgenommen wurde nichts davon.
        var unchanged = await PhaseAsync(client, tournamentId);
        Assert.Equal(12, unchanged.Matches.Count);
        Assert.All(
            unchanged.Matches.Where(m => m.Round <= 2),
            match => Assert.Equal(MatchStatus.Finished, match.Status));
        Assert.Equal(
            corrected.Score!.Display,
            unchanged.Matches.Single(m => m.Id == corrected.Id).Score!.Display);
    }

    /// <summary>
    /// Der Fall, in dem die Rücknahme einer Runde am teuersten wäre: eine Partie
    /// der Folgerunde läuft bereits auf dem Platz.
    ///
    /// Sie zu verwerfen hieße, das Match samt seiner Platzzuweisung zu löschen,
    /// während zwei Spielerinnen darauf spielen — die Partie wäre in der
    /// öffentlichen Ansicht nie gewesen, und der Platz stünde als frei. Die
    /// Korrektur wird deshalb abgewiesen, nicht die Partie.
    /// </summary>
    [Fact]
    public async Task Eine_laufende_Partie_der_Folgerunde_verhindert_die_Korrektur()
    {
        _factory.Clock.Now = new DateTimeOffset(2026, 5, 16, 8, 0, 0, TimeSpan.FromHours(2));

        var (client, tournamentId) = await DrawnAsync();

        var courtId = await TurnierAufbau.CreatedIdAsync(await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/courts",
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor),
            Json));

        await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/courts/windows",
            new CreateCourtWindowsRequest(new TimeOnly(8, 0), new TimeOnly(21, 0)),
            Json);

        await PlayRoundAsync(client, tournamentId, 1);

        var phase = await PhaseAsync(client, tournamentId);
        var running = phase.Matches.First(m => m.Round == 2);

        var assignmentId = (await (await client.PostAsJsonAsync(
            $"/api/matches/{running.Id}/court",
            new AssignCourtRequest(courtId, 1, null, null, null, false),
            Json)).Content.ReadFromJsonAsync<AssignCourtResult>(Json))!.AssignmentId;

        await client.PostAsync($"/api/tournaments/{tournamentId}/scheduling/match-day", null);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/assignments/{assignmentId}/start", null)).StatusCode);

        var response = await client.DeleteAsync(
            $"/api/matches/{phase.Matches.First(m => m.Round == 1).Id}/result");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // Die laufende Partie steht unverändert am Platz.
        var board = (await client.GetFromJsonAsync<List<CourtBoard>>(
            $"/api/tournaments/{tournamentId}/courts", Json))!;

        Assert.Equal(running.Id, Assert.Single(board).Current!.MatchId);
    }

    /// <summary>
    /// Die öffentliche Ansicht kennt das Format nicht — sie zeigt, was da ist.
    /// Nach der ersten Runde ist das eine Runde, nicht ein halber Baum.
    /// </summary>
    [Fact]
    public async Task Die_oeffentliche_Ansicht_zeigt_die_bislang_gespielten_Runden()
    {
        var (client, tournamentId) = await DrawnAsync();
        await PlayRoundAsync(client, tournamentId, 1);

        var anonymous = _factory.CreateClient();
        var view = await anonymous.GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

        var matches = view.GetProperty("phases")[0].GetProperty("matches");

        Assert.Equal(8, matches.GetArrayLength());
    }
}
