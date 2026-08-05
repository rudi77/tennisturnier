using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der Turniertag über die API — die Abnahmebedingung für M7.
///
/// Die harte Randbedingung beim Tennis ist, dass die Matchdauer unbekannt ist.
/// Ein starres Zeitraster kippt beim ersten langen Match; deshalb ist am
/// Turniertag die Reihenfolge auf dem Platz die Aussage, und die Zeiten dahinter
/// sind Schätzungen, die nachgezogen werden (ADR-0002).
/// </summary>
public sealed class MatchDayApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public MatchDayApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>Ein ausgelostes Turnier mit bestätigtem Spielplan im Turniertagbetrieb.</summary>
    private async Task<(HttpClient Admin, Guid ClubId, Guid TournamentId)> MatchDayAsync(
        int participants = 8,
        int courts = 2)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            "tag-admin",
            new TurnierWunsch
            {
                Verein = "TC Turniertag",
                Teilnehmer = participants,
                Plaetze = courts,
                Oeffnungszeiten = true,
                Spielplan = true,
                Turniertag = true,

                // Die Uhr steht auf dem Morgen des ersten Turniertags. Ohne sie
                // läge das Turnier in der Vergangenheit der Systemuhr, und alles,
                // was „ab jetzt" rechnet, spränge in die Gegenwart — eine Zusage
                // für 14 Uhr wäre dann längst verstrichen, und der Test bewiese
                // nichts.
                Uhr = new DateTimeOffset(2026, 5, 16, 8, 0, 0, TimeSpan.FromHours(2)),
            });

        return (aufbau.Admin, aufbau.ClubId, aufbau.TournamentId);
    }

    private static async Task<List<CourtBoard>> BoardAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<List<CourtBoard>>(
            $"/api/tournaments/{tournamentId}/courts", Json))!;

    [Fact]
    public async Task Die_Platzuebersicht_zeigt_Warteschlangen()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();

        var board = await BoardAsync(admin, tournamentId);

        Assert.Equal(2, board.Count);
        Assert.All(board, court => Assert.Null(court.Current));
        Assert.All(board, court => Assert.NotEmpty(court.Queue));

        // Lückenlos ab 1: die Nummer wird am Platz vorgelesen.
        Assert.All(board, court => Assert.Equal(
            Enumerable.Range(1, court.Queue.Count), court.Queue.Select(q => q.SequenceOnCourt)));
    }

    [Fact]
    public async Task Ein_Match_laesst_sich_aufrufen_starten_und_beenden()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var first = (await BoardAsync(admin, tournamentId))[0].Queue[0];

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/assignments/{first.AssignmentId}/call", null)).StatusCode);

        Assert.Equal(AssignmentStatus.Called, (await BoardAsync(admin, tournamentId))[0].Current!.Status);

        await admin.PostAsync($"/api/assignments/{first.AssignmentId}/start", null);

        var running = (await BoardAsync(admin, tournamentId))[0].Current!;
        Assert.Equal(AssignmentStatus.Running, running.Status);
        Assert.NotNull(running.ActualStart);

        await admin.PostAsync($"/api/assignments/{first.AssignmentId}/finish", null);

        var after = (await BoardAsync(admin, tournamentId))[0];
        Assert.Null(after.Current);
        Assert.DoesNotContain(after.Queue, q => q.AssignmentId == first.AssignmentId);
    }

    [Fact]
    public async Task Nach_einem_beendeten_Match_rueckt_die_Warteschlange_nach()
    {
        // Der Kern des Tagesbetriebs: die Schätzungen der Wartenden werden
        // nachgezogen, sobald tatsächlich etwas passiert. Ein Plan, der nach dem
        // ersten Match noch die Zeiten von gestern zeigt, ist Fiktion.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];
        var second = court.Queue[1];

        await admin.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/start", null);

        // Es endet eine halbe Stunde früher als geschätzt.
        _factory.Clock.Advance(TimeSpan.FromMinutes(45));
        await admin.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/finish", null);

        var after = (await BoardAsync(admin, tournamentId))[0];
        var moved = after.Queue.Single(q => q.AssignmentId == second.AssignmentId);

        Assert.Equal(1, moved.SequenceOnCourt);
        Assert.Equal(_factory.Clock.Now, moved.EstimatedStart);
        Assert.True(moved.EstimatedStart < second.EstimatedStart);
    }

    [Fact]
    public async Task Eine_Zusage_wird_beim_Nachziehen_nicht_unterlaufen()
    {
        // „Nicht vor 14 Uhr" ist das Einzige, worauf sich ein Spieler verlassen
        // kann. Die Schätzung darf darunter nicht rutschen, auch wenn der Platz
        // früher frei wird.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];

        // Eine Zusage weit nach dem Zeitpunkt, zu dem der Platz frei wird.
        var promised = new DateTimeOffset(2026, 5, 16, 18, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync(
                $"/api/assignments/{court.Queue[1].AssignmentId}/promise",
                new PromiseStartRequest(promised),
                Json)).StatusCode);

        await admin.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/start", null);
        _factory.Clock.Advance(TimeSpan.FromMinutes(50));
        await admin.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/finish", null);

        var board = (await BoardAsync(admin, tournamentId))[0];
        var waiting = board.Queue.Single(q => q.AssignmentId == court.Queue[1].AssignmentId);

        // Der Platz ist um 8:50 frei — die Zusage gilt trotzdem.
        Assert.Equal(promised, waiting.EarliestStart);
        Assert.Equal(promised, waiting.EstimatedStart);
    }

    [Fact]
    public async Task Die_Warteschlange_laesst_sich_umstellen()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];
        var reversed = court.Queue.Select(q => q.AssignmentId).Reverse().ToList();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/courts/{court.CourtId}/queue",
                new ReorderQueueRequest(reversed),
                Json)).StatusCode);

        var after = (await BoardAsync(admin, tournamentId))[0];

        Assert.Equal(reversed, after.Queue.Select(q => q.AssignmentId));
        Assert.Equal(Enumerable.Range(1, after.Queue.Count), after.Queue.Select(q => q.SequenceOnCourt));
    }

    [Fact]
    public async Task Eine_unvollstaendige_Reihenfolge_wird_abgewiesen()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/courts/{court.CourtId}/queue",
            new ReorderQueueRequest([court.Queue[0].AssignmentId]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Regenguss_unterbricht_und_die_Partie_geht_woanders_weiter()
    {
        // Die Abnahmebedingung: ein Turniertag mit Unterbrechung läuft durch,
        // ohne dass der Plan als Ganzes ungültig wird. Die unterbrochene
        // Zuweisung bleibt als Historie stehen — erst beide zusammen erzählen,
        // was an diesem Tag passiert ist (ADR-0002).
        var (admin, _, tournamentId) = await MatchDayAsync();
        var board = await BoardAsync(admin, tournamentId);
        var running = board[0].Queue[0];
        var otherCourt = board[1].CourtId;

        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/suspend", null);

        // Der alte Platz ist frei — die unterbrochene Partie blockiert ihn nicht.
        var suspended = await BoardAsync(admin, tournamentId);
        Assert.Null(suspended[0].Current);
        Assert.DoesNotContain(suspended[0].Queue, q => q.AssignmentId == running.AssignmentId);

        var resumed = await admin.PostAsJsonAsync(
            $"/api/assignments/{running.AssignmentId}/resume",
            new ResumeMatchRequest(otherCourt),
            Json);

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);

        var after = await BoardAsync(admin, tournamentId);
        var current = after.Single(court => court.CourtId == otherCourt).Current;

        Assert.NotNull(current);
        Assert.Equal(running.MatchId, current.MatchId);
        Assert.Equal(AssignmentStatus.Running, current.Status);
        Assert.NotEqual(running.AssignmentId, current.AssignmentId);
    }

    [Fact]
    public async Task Eine_unterbrochene_Partie_geht_auch_auf_demselben_Platz_weiter()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var running = (await BoardAsync(admin, tournamentId))[0].Queue[0];

        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/suspend", null);

        var response = await admin.PostAsJsonAsync(
            $"/api/assignments/{running.AssignmentId}/resume", new ResumeMatchRequest(), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var current = (await BoardAsync(admin, tournamentId))[0].Current!;

        Assert.Equal(running.AssignmentId, current.AssignmentId);
        Assert.Equal(AssignmentStatus.Running, current.Status);
    }

    [Fact]
    public async Task Die_oeffentliche_Ansicht_zeigt_die_Fortsetzung_und_nicht_den_alten_Platz()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var board = await BoardAsync(admin, tournamentId);
        var running = board[0].Queue[0];
        var otherCourt = board[1].CourtId;

        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/suspend", null);
        await admin.PostAsJsonAsync(
            $"/api/assignments/{running.AssignmentId}/resume", new ResumeMatchRequest(otherCourt), Json);

        var view = await _factory.CreateClient().GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

        var match = view.GetProperty("phases").EnumerateArray().Single()
            .GetProperty("matches").EnumerateArray()
            .Single(m => m.GetProperty("id").GetGuid() == running.MatchId);

        Assert.Equal(
            board[1].CourtName,
            match.GetProperty("courtName").GetString());

        // Und das Match steht genau einmal in den Warteschlangen.
        var queued = view.GetProperty("courts").EnumerateArray()
            .SelectMany(court => court.GetProperty("queue").EnumerateArray())
            .Count(slot => slot.GetProperty("matchId").GetGuid() == running.MatchId);

        Assert.Equal(1, queued);
    }

    [Fact]
    public async Task Im_Planungsmodus_wird_nicht_aufgerufen()
    {
        // Der Wechsel in den Turniertagbetrieb ist ein ausdrücklicher Schritt: er
        // ändert die Bedeutung jeder angezeigten Uhrzeit.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var first = (await BoardAsync(admin, tournamentId))[0].Queue[0];

        await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/planning", null);

        var response = await admin.PostAsync($"/api/assignments/{first.AssignmentId}/call", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Eine_fortgesetzte_Partie_laeuft_nicht_auf_zwei_Plaetzen()
    {
        // Regression: die unterbrochene Zuweisung blieb unterbrochen stehen und
        // ließ sich beliebig oft fortsetzen. Danach stand dasselbe Match auf
        // mehreren Plätzen — und auf einem INSERT wirkt kein Zähler, also fiel
        // auch parallel nichts auf.
        var (admin, _, tournamentId) = await MatchDayAsync(courts: 3);
        var board = await BoardAsync(admin, tournamentId);
        var running = board[0].Queue[0];

        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/suspend", null);

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsJsonAsync(
                $"/api/assignments/{running.AssignmentId}/resume",
                new ResumeMatchRequest(board[1].CourtId),
                Json)).StatusCode);

        // Ein zweites Fortsetzen derselben Zuweisung gibt es nicht mehr.
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsJsonAsync(
                $"/api/assignments/{running.AssignmentId}/resume",
                new ResumeMatchRequest(board[2].CourtId),
                Json)).StatusCode);

        // Und die alte Zuweisung lässt sich auch nicht wieder starten.
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsync($"/api/assignments/{running.AssignmentId}/start", null)).StatusCode);

        var after = await BoardAsync(admin, tournamentId);

        Assert.Single(after, court => court.Current?.MatchId == running.MatchId);
    }

    [Fact]
    public async Task Ein_entschiedenes_Match_wird_nicht_wieder_auf_den_Platz_gestellt()
    {
        // Regression: die Prüfung auf feststehende Teilnehmer lief nur bei
        // „aufrufen" und „starten". Über „fortsetzen" ließ sich ein bereits
        // eingetragenes Match wieder als laufend auf einen Platz stellen.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var running = (await BoardAsync(admin, tournamentId))[0].Queue[0];

        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/suspend", null);

        await admin.PutAsJsonAsync(
            $"/api/matches/{running.MatchId}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        var response = await admin.PostAsJsonAsync(
            $"/api/assignments/{running.AssignmentId}/resume", new ResumeMatchRequest(), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Ein_aufgerufenes_naechstes_Match_verdeckt_das_laufende_nicht()
    {
        // Regression: die Platzübersicht sortierte nach dem Aufzählungstyp, in
        // dem „aufgerufen" vor „läuft" steht. Sobald das nächste Match gerufen
        // war, galt es als das laufende — und das tatsächlich laufende war über
        // die Übersicht nicht mehr zu beenden.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];

        await admin.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{court.Queue[1].AssignmentId}/call", null);

        var after = (await BoardAsync(admin, tournamentId))[0];

        Assert.Equal(court.Queue[0].AssignmentId, after.Current!.AssignmentId);
        Assert.Equal(AssignmentStatus.Running, after.Current.Status);
        Assert.Contains(after.Queue, q => q.AssignmentId == court.Queue[1].AssignmentId);
    }

    [Fact]
    public async Task Eine_Zusage_ausserhalb_des_Turnierzeitraums_wird_abgewiesen()
    {
        // Regression: die Zusage wurde ungeprüft übernommen und zog die ganze
        // Warteschlange mit — ein Tippfehler schob den halben Platz ins Jahr 2099
        // und stand dort öffentlich.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];

        var response = await admin.PostAsJsonAsync(
            $"/api/assignments/{court.Queue[0].AssignmentId}/promise",
            new PromiseStartRequest(new DateTimeOffset(2099, 1, 1, 10, 0, 0, TimeSpan.Zero)),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Im_Planungsmodus_wird_die_Warteschlange_nicht_angefasst()
    {
        // Regression: Umstellen und Zusagen rechneten ab „jetzt" und wirkten auch
        // im Planungsmodus. Ein Aufruf, der inhaltlich nichts änderte, zog damit
        // den gesamten gerechneten Spielplan eines Platzes auf die aktuelle
        // Uhrzeit.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];
        var before = court.Queue.Select(q => q.EstimatedStart).ToList();

        await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/planning", null);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/courts/{court.CourtId}/queue",
                new ReorderQueueRequest([.. court.Queue.Select(q => q.AssignmentId)]),
                Json)).StatusCode);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsJsonAsync(
                $"/api/assignments/{court.Queue[0].AssignmentId}/promise",
                new PromiseStartRequest(new DateTimeOffset(2026, 5, 16, 18, 0, 0, TimeSpan.FromHours(2))),
                Json)).StatusCode);

        await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/match-day", null);

        Assert.Equal(before, (await BoardAsync(admin, tournamentId))[0].Queue.Select(q => q.EstimatedStart));
    }

    [Fact]
    public async Task Eine_Warteschlange_jenseits_der_Oeffnungszeiten_sagt_es()
    {
        // Am Turniertag verschiebt jedes überzogene Match die Schlange nach
        // hinten, bis das Finale rechnerisch nachts stattfände. Das ist keine
        // Fehlfunktion, aber die Turnierleitung muss es sehen — sie muss dann
        // Plätze umverteilen oder vertagen.
        var (admin, _, tournamentId) = await MatchDayAsync(participants: 8, courts: 1);
        var court = (await BoardAsync(admin, tournamentId))[0];

        Assert.All(court.Queue, q => Assert.True(q.WithinOpeningHours));

        await admin.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/start", null);

        // Der erste Aufschlag fällt aus: es wird sehr spät.
        _factory.Clock.Now = new DateTimeOffset(2026, 5, 17, 19, 30, 0, TimeSpan.FromHours(2));
        await admin.PostAsync($"/api/assignments/{court.Queue[0].AssignmentId}/finish", null);

        var after = (await BoardAsync(admin, tournamentId))[0];

        Assert.Contains(after.Queue, q => !q.WithinOpeningHours);
    }

    [Fact]
    public async Task Ein_Nichtantreten_gibt_den_Platz_frei()
    {
        // Regression: nicht jedes Match wird am Platz aufgerufen. Ein
        // Nichtantreten wurde eingetragen, ohne dass jemand hinging — die
        // Zuweisung blieb mit ihrer Nummer in der Warteschlange stehen,
        // blockierte anderthalb Stunden für alles dahinter und war über den
        // Turniertag nicht mehr loszuwerden.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var court = (await BoardAsync(admin, tournamentId))[0];
        var absent = court.Queue[1];

        await admin.PutAsJsonAsync(
            $"/api/matches/{absent.MatchId}/result",
            new RecordResultRequest(MatchOutcome.Walkover, AffectedSide: 2),
            Json);

        var after = (await BoardAsync(admin, tournamentId))[0];

        Assert.DoesNotContain(after.Queue, q => q.AssignmentId == absent.AssignmentId);
        Assert.Equal(Enumerable.Range(1, after.Queue.Count), after.Queue.Select(q => q.SequenceOnCourt));
    }

    [Fact]
    public async Task Nach_einer_Unterbrechung_laesst_sich_der_Rest_weiterplanen()
    {
        // Regression: der Vorschlag bot das unterbrochene Match mit an, und die
        // Bestätigung wies daraufhin den ganzen Vorschlag ab — umplanen ging nur
        // noch mit von Hand zusammengestrichener Liste.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var running = (await BoardAsync(admin, tournamentId))[0].Queue[0];

        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/start", null);
        await admin.PostAsync($"/api/assignments/{running.AssignmentId}/suspend", null);
        await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/planning", null);

        var proposal = (await (await admin.PostAsync(
            $"/api/tournaments/{tournamentId}/schedule/proposal", null))
            .Content.ReadFromJsonAsync<SchedulePlanResult>(Json))!;

        Assert.DoesNotContain(proposal.Assignments, a =>
            a.MatchId == running.MatchId && a.Change != ProposalChange.Unchanged);

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            new ConfirmScheduleRequest([.. proposal.Assignments
                .Where(a => a.MatchId != running.MatchId)
                .Select(a => new ConfirmedAssignment(
                    a.MatchId, a.CourtId, a.SequenceOnCourt, a.PlannedStart, a.EstimatedDuration))]),
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("call")]
    [InlineData("start")]
    [InlineData("finish")]
    [InlineData("suspend")]
    public async Task Ein_Schiedsrichter_darf_am_Platz_arbeiten(string action)
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var first = (await BoardAsync(admin, tournamentId))[0].Queue[0];

        var referee = $"referee-{Guid.NewGuid():N}";
        await _factory.GrantAsync(referee, Role.Referee, ResourceScope.Tournament(tournamentId));
        var client = _factory.CreateClientAs(referee);

        // Der jeweilige Vorzustand, damit der Schritt zulässig ist.
        foreach (var before in new[] { "call", "start" }.TakeWhile(step => step != action))
        {
            await client.PostAsync($"/api/assignments/{first.AssignmentId}/{before}", null);
        }

        if (action == "finish")
        {
            await client.PostAsync($"/api/assignments/{first.AssignmentId}/start", null);
        }

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/assignments/{first.AssignmentId}/{action}", null)).StatusCode);
    }

    /// <summary>
    /// Auf einem Platz wird ein Match gespielt, nicht zwei.
    ///
    /// Die Warteschlange sagt, wer als Nächstes drankommt, hindert aber niemanden
    /// daran, ein wartendes Match unmittelbar aufzurufen. Ohne Prüfung stünden
    /// zwei Zuweisungen desselben Platzes auf „läuft", die Platzübersicht zeigte
    /// nur eine davon, und die andere wäre weder sichtbar noch zu beenden.
    /// </summary>
    [Theory]
    [InlineData("call")]
    [InlineData("start")]
    public async Task Auf_einem_belegten_Platz_beginnt_keine_zweite_Partie(string action)
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var queue = (await BoardAsync(admin, tournamentId))[0].Queue;

        await admin.PostAsync($"/api/assignments/{queue[0].AssignmentId}/start", null);

        var response = await admin.PostAsync($"/api/assignments/{queue[1].AssignmentId}/{action}", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // Und die laufende Partie steht unverändert als die des Platzes.
        var board = await BoardAsync(admin, tournamentId);
        Assert.Equal(queue[0].AssignmentId, board[0].Current!.AssignmentId);
        Assert.Equal(AssignmentStatus.Running, board[0].Current!.Status);
    }

    /// <summary>
    /// Eine Zusage gilt auch beim Aufruf. „Nicht vor 14 Uhr" ist das Einzige,
    /// worauf sich ein Spieler verlassen kann — wer die Zuweisung unmittelbar
    /// aufruft, ginge daran vorbei, und der Spieler, der sich darauf verlassen
    /// hat, ist nicht da.
    /// </summary>
    [Fact]
    public async Task Vor_der_zugesagten_Zeit_wird_nicht_aufgerufen()
    {
        var (admin, _, tournamentId) = await MatchDayAsync();
        var waiting = (await BoardAsync(admin, tournamentId))[0].Queue[0];

        var promised = _factory.Clock.Now.AddHours(6);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync(
                $"/api/assignments/{waiting.AssignmentId}/promise",
                new PromiseStartRequest(promised),
                Json)).StatusCode);

        var tooEarly = await admin.PostAsync($"/api/assignments/{waiting.AssignmentId}/call", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooEarly.StatusCode);

        // Zur zugesagten Zeit geht es.
        _factory.Clock.Now = promised;
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/assignments/{waiting.AssignmentId}/call", null)).StatusCode);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_disponiert_nicht()
    {
        // Regression: Zusagen und Fortsetzen liefen nur gegen die
        // Ergebnisberechtigung. Damit konnte der Schiedsrichter eine Zusage
        // setzen, die die Schätzungen des ganzen Platzes verschiebt, und eine
        // Partie auf einen beliebigen Platz des Vereins verlegen — beides
        // Entscheidungen der Turnierleitung.
        var (admin, _, tournamentId) = await MatchDayAsync();
        var board = await BoardAsync(admin, tournamentId);
        var first = board[0].Queue[0];

        var referee = $"referee-{Guid.NewGuid():N}";
        await _factory.GrantAsync(referee, Role.Referee, ResourceScope.Tournament(tournamentId));
        var client = _factory.CreateClientAs(referee);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync(
                $"/api/assignments/{first.AssignmentId}/promise",
                new PromiseStartRequest(new DateTimeOffset(2026, 5, 16, 18, 0, 0, TimeSpan.FromHours(2))),
                Json)).StatusCode);

        await client.PostAsync($"/api/assignments/{first.AssignmentId}/start", null);
        await client.PostAsync($"/api/assignments/{first.AssignmentId}/suspend", null);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync(
                $"/api/assignments/{first.AssignmentId}/resume",
                new ResumeMatchRequest(board[1].CourtId),
                Json)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/courts/{board[0].CourtId}/queue",
                new ReorderQueueRequest([.. board[0].Queue.Select(q => q.AssignmentId).Reverse()]),
                Json)).StatusCode);
    }

    [Fact]
    public async Task Ein_Turniertag_mit_Verzug_und_Regen_laeuft_durch()
    {
        // Die Abnahmebedingung aus dem Fahrplan, als Ablauf: alle Erstrunden
        // werden gespielt, eines davon nach einer Unterbrechung auf einem anderen
        // Platz, und danach steht der Spielplan weiterhin.
        var (admin, _, tournamentId) = await MatchDayAsync();

        var interrupted = true;

        while (true)
        {
            var board = await BoardAsync(admin, tournamentId);

            // Aufgerufen wird nur, wer feststeht. Ein Halbfinale, dessen Vorspiel
            // noch läuft, steht zwar im Plan, aber am Platz wird kein Platzhalter
            // ausgerufen.
            var next = board.FirstOrDefault(court =>
                court.Current is null && court.Queue.Any(q => q.MatchStatus == MatchStatus.Ready));

            if (next is null)
            {
                break;
            }

            var assignmentId = next.Queue.First(q => q.MatchStatus == MatchStatus.Ready).AssignmentId;
            await admin.PostAsync($"/api/assignments/{assignmentId}/call", null);
            await admin.PostAsync($"/api/assignments/{assignmentId}/start", null);

            if (interrupted)
            {
                // Der Regen: unterbrechen und auf demselben Platz fortsetzen.
                interrupted = false;
                await admin.PostAsync($"/api/assignments/{assignmentId}/suspend", null);
                await admin.PostAsJsonAsync(
                    $"/api/assignments/{assignmentId}/resume", new ResumeMatchRequest(), Json);
            }

            var current = (await BoardAsync(admin, tournamentId))
                .Single(court => court.CourtId == next.CourtId).Current!;

            await admin.PostAsync($"/api/assignments/{current.AssignmentId}/finish", null);

            // Das Ergebnis kommt getrennt — der Platz ist frei, sobald die
            // Spieler ihn verlassen.
            await admin.PutAsJsonAsync(
                $"/api/matches/{current.MatchId}/result",
                new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
                Json);
        }

        var finalBoard = await BoardAsync(admin, tournamentId);

        Assert.All(finalBoard, court => Assert.Null(court.Current));
        Assert.All(finalBoard, court => Assert.Empty(court.Queue));

        var phases = await admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);

        Assert.All(phases!.Single().Matches, match => Assert.NotNull(match.Score));
    }
}
