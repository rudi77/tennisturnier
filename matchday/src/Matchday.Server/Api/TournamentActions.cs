using Matchday.Domain;
using Matchday.Server.Live;
using Matchday.Server.Storage;

namespace Matchday.Server.Api;

/// <summary>
/// Die Anwendungsfälle — gemeinsam für die HTTP-API und die Werkzeuge des
/// Agenten. Beide reden mit derselben Klasse, damit es eine Wahrheit gibt.
/// </summary>
public sealed class TournamentActions(TournamentStore store, LiveHub live, TimeProvider clock)
{
    public async Task<Tournament> CreateAsync(Actor actor, CreateTournamentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tournament = Tournament.Create(
            request.Name, actor.ClientId, clock.GetUtcNow(), request.Mode, request.Format, request.Date, request.Location, request.Discipline);

        // Teilnehmer gleich mit: Ein Turnier anzulegen und dann niemanden
        // eintragen zu können, wäre ein halber Weg — und über den Chat kommt
        // beides ohnehin in einem Satz.
        foreach (var name in request.Participants ?? [])
        {
            tournament.AddParticipant(name);
        }

        await store.InsertAsync(tournament, ct);
        return tournament;
    }

    public Task<IReadOnlyList<Tournament>> ListMineAsync(Actor actor, CancellationToken ct = default) =>
        store.ListByOwnerAsync(actor.ClientId, ct);

    public async Task<Tournament> GetAsync(Guid id, CancellationToken ct = default) =>
        await store.FindAsync(id, ct) ?? throw new NotFoundException("Dieses Turnier gibt es nicht.");

    public async Task<Tournament> GetByAdminTokenAsync(string adminToken, CancellationToken ct = default) =>
        await store.FindByAdminTokenAsync(adminToken, ct) ?? throw new NotFoundException("Diesen Verwalterlink gibt es nicht.");

    public Task<Tournament> UpdateAsync(Actor actor, Guid id, UpdateTournamentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MutateAsync(actor, id, t =>
        {
            if (request.Name is not null)
            {
                t.Rename(request.Name);
            }

            if (request.ClearDate)
            {
                t.SetDate(null);
            }
            else if (request.Date is not null)
            {
                t.SetDate(request.Date);
            }

            if (request.ClearLocation)
            {
                t.SetLocation(null);
            }
            else if (request.Location is not null)
            {
                t.SetLocation(request.Location);
            }

            if (request.Mode is { } mode && mode != t.Mode)
            {
                t.SetMode(mode);
            }

            if (request.Discipline is { } discipline && discipline != t.Discipline)
            {
                t.SetDiscipline(discipline);
            }

            if (request.Format is not null && request.Format != t.Format)
            {
                t.SetFormat(request.Format);
            }
        }, ct);
    }

    public Task<Tournament> AddParticipantsAsync(Actor actor, Guid id, IReadOnlyList<string> names, CancellationToken ct = default) =>
        MutateAsync(actor, id, t =>
        {
            foreach (var name in names)
            {
                t.AddParticipant(name);
            }
        }, ct);

    public Task<Tournament> RemoveParticipantAsync(Actor actor, Guid id, Guid participantId, CancellationToken ct = default) =>
        MutateAsync(actor, id, t => t.RemoveParticipant(participantId), ct);

    public Task<Tournament> DrawAsync(Actor actor, Guid id, CancellationToken ct = default) =>
        MutateAsync(actor, id, t => t.Draw(), ct);

    public Task<Tournament> UndoDrawAsync(Actor actor, Guid id, CancellationToken ct = default) =>
        MutateAsync(actor, id, t => t.UndoDraw(), ct);

    public Task<Tournament> RecordResultAsync(Actor actor, Guid id, Guid matchId, ResultRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MutateAsync(actor, id, t => t.RecordResult(matchId, ToScore(request, t.Format)), ct);
    }

    public Task<Tournament> ClearResultAsync(Actor actor, Guid id, Guid matchId, CancellationToken ct = default) =>
        MutateAsync(actor, id, t => t.ClearResult(matchId), ct);

    public Task<Tournament> RotateAdminTokenAsync(Actor actor, Guid id, CancellationToken ct = default) =>
        MutateAsync(actor, id, t => t.RotateAdminToken(), ct);

    public async Task DeleteAsync(Actor actor, Guid id, CancellationToken ct = default)
    {
        var tournament = await GetAsync(id, ct);
        RequireManage(actor, tournament);
        await store.DeleteAsync(id, ct);
        live.Publish(id, null);
    }

    /// <summary>
    /// Ein Ergebnis kommt aus Sicht des Siegers herein und wird hier auf die
    /// Seiten des Matches gedreht. Die Prüfung macht die Domäne.
    /// </summary>
    public static Score ToScore(ResultRequest request, MatchFormat format)
    {
        if (request.WinnerSide is not (1 or 2))
        {
            throw new DomainException("Der Sieger ist Seite 1 oder 2.");
        }

        var loserSide = request.WinnerSide == 1 ? 2 : 1;
        var sets = request.Sets.Select(s => Orient(s, request.WinnerSide)).ToList();

        return request.Kind switch
        {
            ResultKind.Played => Score.Played(sets, format),
            ResultKind.Walkover => Score.Walkover(absentSide: loserSide),
            ResultKind.Retired => Score.Retired(
                sets,
                request.AbandonedSet is null ? null : Orient(request.AbandonedSet, request.WinnerSide),
                retiringSide: loserSide,
                format),
            _ => throw new DomainException("Unbekannte Art von Ergebnis."),
        };
    }

    private static SetScore Orient(SetScore fromWinner, int winnerSide) =>
        winnerSide == 1 ? fromWinner : new SetScore(fromWinner.Games2, fromWinner.Games1, fromWinner.TiebreakPoints);

    private async Task<Tournament> MutateAsync(Actor actor, Guid id, Action<Tournament> action, CancellationToken ct)
    {
        var tournament = await store.MutateAsync(id, t =>
        {
            RequireManage(actor, t);
            action(t);
        }, ct);

        live.Publish(id, ViewBuilder.Build(tournament));
        return tournament;
    }

    private static void RequireManage(Actor actor, Tournament tournament)
    {
        if (!actor.MayManage(tournament))
        {
            throw new ForbiddenException("Dafür braucht es den Verwalterlink dieses Turniers.");
        }
    }
}
