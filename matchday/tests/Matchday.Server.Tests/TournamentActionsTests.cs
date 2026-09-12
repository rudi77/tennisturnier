using Matchday.Domain;
using Matchday.Server.Api;

namespace Matchday.Server.Tests;

public sealed class TournamentActionsTests : IDisposable
{
    private readonly Aufbau _a = new();

    public void Dispose() => _a.Dispose();

    [Fact]
    public async Task Anlegen_speichern_und_wiederfinden()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup", new DateOnly(2026, 9, 19), "Baden"));

        var geladen = await _a.Actions.GetAsync(t.Id);
        Assert.Equal("Sommercup", geladen.Name);
        Assert.Equal("Baden", geladen.Location);
        Assert.Equal(t.AdminToken, geladen.AdminToken);

        var meine = await _a.Actions.ListMineAsync(_a.Rudi);
        Assert.Single(meine);
        Assert.Empty(await _a.Actions.ListMineAsync(_a.Fremder));

        Assert.Equal(t.Id, (await _a.Actions.GetByAdminTokenAsync(t.AdminToken)).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => _a.Actions.GetByAdminTokenAsync("gibt-es-nicht"));
        await Assert.ThrowsAsync<NotFoundException>(() => _a.Actions.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Nur_Eigentuemer_oder_Verwalterlink_duerfen_aendern()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _a.Actions.AddParticipantsAsync(_a.Fremder, t.Id, ["Max"]));

        var mitLink = new Actor("browser-fremd", t.AdminToken);
        var geaendert = await _a.Actions.AddParticipantsAsync(mitLink, t.Id, ["Max"]);
        Assert.Single(geaendert.Participants);

        await _a.Actions.RotateAdminTokenAsync(_a.Rudi, t.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => _a.Actions.AddParticipantsAsync(mitLink, t.Id, ["Anna"]));
    }

    [Fact]
    public async Task Der_ganze_Weg_bis_zum_Sieger_und_die_Live_Ansicht_geht_mit()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"));
        using var abo = _a.Live.Subscribe(t.Id, out var reader);

        await _a.Actions.AddParticipantsAsync(_a.Rudi, t.Id, ["Rudi", "Max", "Anna", "Tom"]);
        var gelost = await _a.Actions.DrawAsync(_a.Rudi, t.Id);
        Assert.Equal(TournamentState.Running, gelost.State);

        foreach (var match in gelost.Matches.Where(m => m.Round == 1))
        {
            await _a.Actions.RecordResultAsync(_a.Rudi, t.Id, match.Id,
                new ResultRequest(ResultKind.Played, 2, [new(6, 3), new(6, 3)]));
        }

        var final = (await _a.Actions.GetAsync(t.Id)).Matches.Single(m => m.Label == "Finale");
        Assert.Equal(MatchStatus.Ready, final.Status);

        var fertig = await _a.Actions.RecordResultAsync(_a.Rudi, t.Id, final.Id,
            new ResultRequest(ResultKind.Walkover, 1, []));
        Assert.Equal(TournamentState.Completed, fertig.State);

        // Sieger von Seite 2 in beiden Halbfinals, kampflos im Finale: Sätze aus Sicht des Siegers gedreht.
        var hf = fertig.Matches.First(m => m.Round == 1);
        Assert.Equal(new SetScore(3, 6), hf.Score!.Sets[0]);

        // Fünf Änderungen nach dem Abonnieren: Teilnehmer, Auslosung, zwei Halbfinals, Finale.
        var empfangen = 0;
        while (reader.TryRead(out _))
        {
            empfangen++;
        }

        Assert.Equal(5, empfangen);

        await _a.Actions.DeleteAsync(_a.Rudi, t.Id);
        Assert.True(reader.TryRead(out var letzte));
        Assert.Null(letzte);
        await Assert.ThrowsAsync<NotFoundException>(() => _a.Actions.GetAsync(t.Id));
    }

    [Fact]
    public async Task Aendern_haelt_sich_an_die_Domaene()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"));

        var geaendert = await _a.Actions.UpdateAsync(_a.Rudi, t.Id, new UpdateTournamentRequest(
            Name: "Herbstcup", Mode: Mode.RoundRobin, Format: new MatchFormat(BestOf: 1)));
        Assert.Equal("Herbstcup", geaendert.Name);
        Assert.Equal(Mode.RoundRobin, geaendert.Mode);
        Assert.Equal(1, geaendert.Format.BestOf);

        await _a.Actions.AddParticipantsAsync(_a.Rudi, t.Id, ["A", "B"]);
        await _a.Actions.DrawAsync(_a.Rudi, t.Id);
        await Assert.ThrowsAsync<DomainException>(() =>
            _a.Actions.UpdateAsync(_a.Rudi, t.Id, new UpdateTournamentRequest(Mode: Mode.Knockout)));

        var view = ViewBuilder.Build(await _a.Actions.GetAsync(t.Id));
        Assert.Equal("ein Satz", view.FormatText);
        Assert.Single(view.Matches);
        Assert.Equal(2, view.Standings.Count);
    }

    [Fact]
    public void Ein_Ergebnis_wird_auf_die_Seiten_gedreht()
    {
        var format = MatchFormat.Standard;

        var seite1 = TournamentActions.ToScore(new ResultRequest(ResultKind.Played, 1, [new(6, 4), new(7, 6, 3)]), format);
        Assert.Equal(1, seite1.WinnerSide);

        var seite2 = TournamentActions.ToScore(new ResultRequest(ResultKind.Played, 2, [new(6, 4), new(7, 6, 3)]), format);
        Assert.Equal(2, seite2.WinnerSide);
        Assert.Equal(new SetScore(4, 6), seite2.Sets[0]);
        Assert.Equal(new SetScore(6, 7, 3), seite2.Sets[1]);

        var aufgabe = TournamentActions.ToScore(new ResultRequest(ResultKind.Retired, 2, [new(6, 4)], new(2, 1)), format);
        Assert.Equal(MatchOutcome.Retirement, aufgabe.Outcome);
        Assert.Equal(new SetScore(1, 2), aufgabe.AbandonedSet);

        var aufgabeOhneSatz = TournamentActions.ToScore(new ResultRequest(ResultKind.Retired, 1, [new(6, 4)]), format);
        Assert.Equal(MatchOutcome.Retirement, aufgabeOhneSatz.Outcome);
        Assert.Null(aufgabeOhneSatz.AbandonedSet);

        Assert.Throws<DomainException>(() => TournamentActions.ToScore(new ResultRequest(ResultKind.Played, 3, []), format));

        // Eine Art, die es nicht gibt — etwa aus einer fremden Anfrage.
        Assert.Throws<DomainException>(() => TournamentActions.ToScore(new ResultRequest((ResultKind)99, 1, []), format));
    }

    [Fact]
    public async Task Eine_zurueckgenommene_Auslosung_gibt_die_Teilnehmerliste_frei()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"));
        await _a.Actions.AddParticipantsAsync(_a.Rudi, t.Id, ["Rudi", "Max"]);
        await _a.Actions.DrawAsync(_a.Rudi, t.Id);

        var zurueck = await _a.Actions.UndoDrawAsync(_a.Rudi, t.Id);

        Assert.Equal(TournamentState.Setup, zurueck.State);
        Assert.Empty(zurueck.Matches);

        // Und nun geht wieder, was vorher gesperrt war.
        var mitDritter = await _a.Actions.AddParticipantsAsync(_a.Rudi, t.Id, ["Anna"]);
        Assert.Equal(3, mitDritter.Participants.Count);
    }

    [Fact]
    public async Task Ein_Turnier_das_es_nicht_gibt_laesst_sich_nicht_aendern()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _a.Store.MutateAsync(Guid.NewGuid(), _ => Assert.Fail("Hier kommt niemand hin.")));
    }
}
