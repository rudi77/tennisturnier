using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TennisTurnier.Api.Web;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Api.Pages.Tournaments;

/// <summary>
/// Bracket, Tabellen und Ergebniseingabe.
/// </summary>
[Authorize]
public sealed class DrawModel : AppPageModel
{
    private readonly ITournamentService _tournaments;
    private readonly IMatchService _matches;

    public DrawModel(ITournamentService tournaments, IMatchService matches)
    {
        _tournaments = tournaments;
        _matches = matches;
    }

    public Guid Id { get; private set; }

    public TournamentDetail Tournament { get; private set; } = null!;

    public IReadOnlyList<PhaseDetail> Phases { get; private set; } = [];

    public IReadOnlyDictionary<Guid, StandingsDetail> Standings { get; private set; } =
        new Dictionary<Guid, StandingsDetail>();

    public TournamentNav Nav => new(Tournament, "/draw");

    /// <summary>
    /// Das Format einer Phase — es bestimmt, ob eine Tabelle oder ein Bracket
    /// gezeigt wird. Die Zuordnung läuft über die Ordinalzahl, weil die
    /// eingefrorene Definition und die angelegten Phasen genau darüber
    /// zusammenhängen.
    /// </summary>
    public PhaseFormatKind FormatOf(PhaseDetail phase) =>
        Tournament.Format?.Definition.Phases.FirstOrDefault(p => p.Ordinal == phase.Ordinal)?.Format
        ?? PhaseFormatKind.Knockout;

    public async Task<IActionResult> OnGetAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        Id = tournamentId;

        return await TryAsync(() => LoadAsync(cancellationToken)) ? Page() : NotFound();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Tournament = await _tournaments.GetAsync(Id, cancellationToken);
        Phases = await _matches.GetPhasesAsync(Id, cancellationToken);

        var standings = new Dictionary<Guid, StandingsDetail>();
        foreach (var phase in Phases)
        {
            standings[phase.Id] = await _matches.GetStandingsAsync(Id, phase.Id, cancellationToken);
        }

        Standings = standings;
    }

    public async Task<IActionResult> OnPostResultAsync(
        Guid tournamentId,
        Guid matchId,
        MatchOutcome outcome,
        string? sets,
        string? abandonedSet,
        int? affectedSide,
        CancellationToken cancellationToken)
    {
        Id = tournamentId;

        var recorded = await TryAsync(() =>
        {
            var request = new RecordResultRequest(
                outcome,
                ParseSets(sets),
                ParseSet(abandonedSet),
                affectedSide);

            return _matches.RecordResultAsync(matchId, request, cancellationToken);
        });

        return recorded ? await PhasesAsync(cancellationToken) : ErrorFragment();
    }

    public async Task<IActionResult> OnPostClearAsync(
        Guid tournamentId,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        Id = tournamentId;

        var cleared = await TryAsync(() => _matches.ClearResultAsync(matchId, cancellationToken));

        return cleared ? await PhasesAsync(cancellationToken) : ErrorFragment();
    }

    private async Task<IActionResult> PhasesAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        return Partial("_Phases", this);
    }

    /// <summary>
    /// Liest „6:4 7:6(5)" als Satzfolge.
    ///
    /// Die Eingabe erfolgt als eine Zeile und nicht als zehn Zahlenfelder, weil
    /// genau so ein Ergebnis am Platz notiert und durchgesagt wird. Was hier
    /// nicht aufgeht, wird abgewiesen, bevor es die Domäne erreicht — die Meldung
    /// soll die Eingabe erklären, nicht das Satzformat.
    /// </summary>
    private static IReadOnlyList<SetScore>? ParseSets(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => ParseSet(token)!)
            .ToList();
    }

    private static SetScore? ParseSet(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var text = token.Trim();
        int? tiebreak = null;

        var open = text.IndexOf('(', StringComparison.Ordinal);
        if (open >= 0)
        {
            var close = text.IndexOf(')', open);
            if (close < 0)
            {
                throw new DomainException($"„{token}\": die Klammer für den Tiebreak ist nicht geschlossen.");
            }

            tiebreak = Number(text[(open + 1)..close], token);
            text = text[..open];
        }

        var parts = text.Split(['/', ':', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new DomainException($"„{token}\" ist kein Satz. Erwartet wird etwa 6:4 oder 7:6(5).");
        }

        return new SetScore(Number(parts[0], token), Number(parts[1], token), tiebreak);
    }

    private static int Number(string text, string token) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new DomainException($"„{token}\" enthält keine Zahl an der erwarteten Stelle.");
}
