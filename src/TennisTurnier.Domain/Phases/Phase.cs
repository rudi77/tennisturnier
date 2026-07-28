using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Phases;

public enum PhaseStatus
{
    /// <summary>Angelegt, aber noch ohne Paarungen.</summary>
    Pending,

    /// <summary>Paarungen stehen, mindestens ein Match ist noch offen.</summary>
    Running,

    /// <summary>Alle Matches entschieden.</summary>
    Completed,
}

/// <summary>
/// Eine Phase eines Turniers mit ihren Matches.
///
/// Sie hält den Zustand, das Format rechnet. Die Trennung ist der Grund, warum
/// sich jede Paarungslogik ohne Aggregat und ohne Datenbank testen lässt.
/// </summary>
public sealed class Phase : Entity
{
    private readonly List<Match> _matches = [];

    public Phase(Guid id, Guid tournamentId, int ordinal, PhaseFormatKind format, string? name = null)
        : base(id)
    {
        if (tournamentId == Guid.Empty)
        {
            throw new DomainException("Eine Phase braucht ein Turnier.");
        }

        if (ordinal < 1)
        {
            throw new DomainException($"Eine Phase beginnt bei 1, war {ordinal}.");
        }

        TournamentId = tournamentId;
        Ordinal = ordinal;
        Format = format;
        Name = string.IsNullOrWhiteSpace(name) ? format.ToString() : name.Trim();
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private Phase(Guid id) : base(id) => Name = string.Empty;

    public Guid TournamentId { get; private set; }

    public int Ordinal { get; private set; }

    public PhaseFormatKind Format { get; private set; }

    public string Name { get; private set; }

    public IReadOnlyList<Match> Matches => _matches;

    public PhaseStatus Status =>
        _matches.Count == 0 ? PhaseStatus.Pending
        : _matches.All(m => m.Status == MatchStatus.Finished) ? PhaseStatus.Completed
        : PhaseStatus.Running;

    /// <summary>
    /// Legt die vom Format erzeugten Paarungen als Matches an und verdrahtet die
    /// Abhängigkeiten.
    ///
    /// Der Knackpunkt: das Format kennt die Match-Ids noch nicht — die entstehen
    /// erst hier. Es liefert die Struktur, diese Methode setzt die konkreten
    /// „Sieger aus …"-Referenzen ein.
    /// </summary>
    public IReadOnlyList<Match> AddPairings(IReadOnlyList<Pairing> pairings)
    {
        ArgumentNullException.ThrowIfNull(pairings);

        var added = new List<Match>(pairings.Count);

        foreach (var pairing in pairings.OrderBy(p => p.Round).ThenBy(p => p.Position))
        {
            var match = new Match(
                Guid.NewGuid(),
                TournamentId,
                Id,
                pairing.Round,
                pairing.Position,
                pairing.Side1,
                pairing.Side2,
                pairing.Label,
                pairing.Group);

            _matches.Add(match);
            added.Add(match);
        }

        WireKnockoutTree(added);
        ResolveDecided();

        return added;
    }

    /// <summary>
    /// Trägt ein Ergebnis ein und reicht es an die abhängigen Matches weiter.
    /// </summary>
    public void RecordResult(Guid matchId, Score score)
    {
        var match = MatchOf(matchId);
        match.RecordResult(score);

        ResolveDecided();
    }

    /// <summary>
    /// Nimmt ein Ergebnis zurück.
    ///
    /// Nur solange kein Folgematch bereits gespielt wurde — sonst stünde in der
    /// nächsten Runde jemand, der laut korrigiertem Ergebnis nie hätte antreten
    /// dürfen. Diese Kette muss zuerst von hinten aufgerollt werden.
    /// </summary>
    public void ClearResult(Guid matchId)
    {
        var match = MatchOf(matchId);
        if (match.Score is null)
        {
            return;
        }

        var blocking = _matches.FirstOrDefault(m =>
            m.Score is not null
            && (DependsOn(m.Side1.Origin, matchId) || DependsOn(m.Side2.Origin, matchId)));

        if (blocking is not null)
        {
            throw new DomainException(
                $"Das Ergebnis lässt sich nicht zurücknehmen, weil das Folgematch „{blocking}“ bereits entschieden ist. " +
                "Zuerst dessen Ergebnis zurücknehmen.");
        }

        match.ClearResult();
        UnresolveDependents(matchId);
    }

    /// <summary>
    /// Löst alle Referenzen auf, deren Vorgänger entschieden ist.
    ///
    /// Läuft in einer Schleife, weil ein Freilos eine Kette auslösen kann: die
    /// erste Runde entscheidet sich kampflos, damit steht die zweite fest, und
    /// bei einem sehr kleinen Feld unter Umständen auch die dritte.
    /// </summary>
    private void ResolveDecided()
    {
        bool changed;
        var guard = 0;

        do
        {
            changed = ResolveOnce();

            if (++guard > _matches.Count + 1)
            {
                throw new DomainException("Die Auflösung der Turnierreferenzen terminiert nicht.");
            }
        }
        while (changed);
    }

    private bool ResolveOnce()
    {
        var changed = false;

        // Entschiedene Matches werden nicht mehr angefasst. Ohne diese
        // Einschränkung liefe die Auflösung gegen die Sperre im Match und
        // brächte die ganze Runde zum Absturz, sobald das erste Ergebnis steht.
        foreach (var match in _matches.Where(m => m.Score is null))
        {
            changed |= ResolveSide(match, 1);
            changed |= ResolveSide(match, 2);
            changed |= DecideByeIfPossible(match);
        }

        return changed;
    }

    private bool ResolveSide(Match match, int side)
    {
        var resolved = match.Side(side).Origin switch
        {
            ParticipantRef.WinnerOf winner => Outcome(winner.MatchId, wantWinner: true),
            ParticipantRef.LoserOf loser => Outcome(loser.MatchId, wantWinner: false),
            _ => null,
        };

        return resolved is { } entryId && match.Resolve(side, entryId);
    }

    /// <summary>
    /// Der Ausgang eines Vorgängers als Referenz, sofern er feststeht.
    ///
    /// Ein Freilos-Match hat keinen Verlierer — dort bleibt die Referenz offen,
    /// statt ein Freilos weiterzureichen.
    /// </summary>
    private Guid? Outcome(Guid matchId, bool wantWinner)
    {
        var source = _matches.FirstOrDefault(m => m.Id == matchId);
        if (source?.Score is null)
        {
            return null;
        }

        return wantWinner ? source.WinnerEntryId : source.LoserEntryId;
    }

    /// <summary>
    /// Ein Match mit einem Freilos wird nie gespielt. Sobald die andere Seite
    /// feststeht, ist es entschieden.
    /// </summary>
    private static bool DecideByeIfPossible(Match match)
    {
        if (match.Score is not null || !match.HasBye)
        {
            return false;
        }

        var advancingSide = match.Side1.IsBye ? 2 : 1;
        if (!match.Side(advancingSide).IsSettled)
        {
            return false;
        }

        match.RecordResult(Score.ByeFor(advancingSide));
        return true;
    }

    /// <summary>
    /// Setzt die Seiten, die von diesem Match abhingen, wieder auf offen zurück.
    /// Ohne das bliebe nach einer Korrektur der alte Sieger stehen.
    /// </summary>
    private void UnresolveDependents(Guid matchId)
    {
        foreach (var dependent in _matches)
        {
            if (DependsOn(dependent.Side1.Origin, matchId))
            {
                dependent.Unresolve(1);
            }

            if (DependsOn(dependent.Side2.Origin, matchId))
            {
                dependent.Unresolve(2);
            }
        }
    }

    private static bool DependsOn(ParticipantRef reference, Guid matchId) =>
        reference.DependsOnMatch == matchId;

    /// <summary>
    /// Verbindet die Runden eines K.-o.-Baums: das Match auf Position p in Runde
    /// r bekommt die Sieger der Positionen 2p-1 und 2p aus Runde r-1. Das Spiel
    /// um Platz 3 bekommt stattdessen die Verlierer der Halbfinals.
    /// </summary>
    private void WireKnockoutTree(IReadOnlyList<Match> added)
    {
        if (Format != PhaseFormatKind.Knockout)
        {
            return;
        }

        var byRound = added
            .Where(m => m.Label != KnockoutFormat.ThirdPlaceLabel)
            .GroupBy(m => m.Round)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Position).ToList());

        foreach (var (round, matches) in byRound.Where(r => r.Key > 1))
        {
            if (!byRound.TryGetValue(round - 1, out var previous))
            {
                continue;
            }

            for (var index = 0; index < matches.Count; index++)
            {
                matches[index].SetOrigin(1, ParticipantRef.FromWinnerOf(previous[index * 2].Id));
                matches[index].SetOrigin(2, ParticipantRef.FromWinnerOf(previous[(index * 2) + 1].Id));
            }
        }

        WireThirdPlaceMatch(added, byRound);
    }

    private static void WireThirdPlaceMatch(
        IReadOnlyList<Match> added,
        Dictionary<int, List<Match>> byRound)
    {
        var thirdPlace = added.FirstOrDefault(m => m.Label == KnockoutFormat.ThirdPlaceLabel);
        if (thirdPlace is null)
        {
            return;
        }

        var finalRound = byRound.Keys.Max();
        if (!byRound.TryGetValue(finalRound - 1, out var semiFinals) || semiFinals.Count < 2)
        {
            return;
        }

        thirdPlace.SetOrigin(1, ParticipantRef.FromLoserOf(semiFinals[0].Id));
        thirdPlace.SetOrigin(2, ParticipantRef.FromLoserOf(semiFinals[1].Id));
    }

    private Match MatchOf(Guid matchId) =>
        _matches.FirstOrDefault(m => m.Id == matchId)
        ?? throw new DomainException($"Die Phase hat kein Match mit der Id {matchId}.");
}
