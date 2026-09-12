using System.Security.Cryptography;

namespace Matchday.Domain;

public enum Mode
{
    Knockout,
    RoundRobin,
}

public enum TournamentState
{
    /// <summary>Teilnehmer werden gesammelt, noch nichts ausgelost.</summary>
    Setup,

    /// <summary>Ausgelost, es wird gespielt.</summary>
    Running,

    /// <summary>Alle Matches haben ein Ergebnis.</summary>
    Completed,
}

/// <summary>
/// Das Turnier — das einzige Aggregat. Es trägt Teilnehmer und Matches selbst
/// und wird immer als Ganzes gelesen und geschrieben (ADR-0016).
/// </summary>
public sealed class Tournament
{
    public const int MaxParticipants = 64;

    private readonly List<Participant> _participants;
    private readonly List<Match> _matches;

    private Tournament(
        Guid id,
        string name,
        DateOnly? date,
        string? location,
        Mode mode,
        MatchFormat format,
        TournamentState state,
        string ownerId,
        string adminToken,
        DateTimeOffset createdAt,
        List<Participant> participants,
        List<Match> matches)
    {
        Id = id;
        Name = name;
        Date = date;
        Location = location;
        Mode = mode;
        Format = format;
        State = state;
        OwnerId = ownerId;
        AdminToken = adminToken;
        CreatedAt = createdAt;
        _participants = participants;
        _matches = matches;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public DateOnly? Date { get; private set; }

    public string? Location { get; private set; }

    public Mode Mode { get; private set; }

    public MatchFormat Format { get; private set; }

    public TournamentState State { get; private set; }

    /// <summary>Die Browserkennung dessen, der das Turnier angelegt hat. Kein Konto.</summary>
    public string OwnerId { get; }

    /// <summary>Der geheime Teil des Verwalterlinks. Wer ihn hat, darf alles.</summary>
    public string AdminToken { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<Participant> Participants => _participants;

    public IReadOnlyList<Match> Matches => _matches;

    public static Tournament Create(
        string name,
        string ownerId,
        DateTimeOffset now,
        Mode mode = Mode.Knockout,
        MatchFormat? format = null,
        DateOnly? date = null,
        string? location = null)
    {
        format ??= MatchFormat.Standard;
        format.Validate();

        return new Tournament(
            Guid.NewGuid(),
            CleanName(name),
            date,
            CleanOptional(location),
            mode,
            format,
            TournamentState.Setup,
            RequireText(ownerId, "Eigentümer"),
            NewToken(),
            now,
            [],
            []);
    }

    // --- Stammdaten -------------------------------------------------------

    public void Rename(string name) => Name = CleanName(name);

    public void SetDate(DateOnly? date) => Date = date;

    public void SetLocation(string? location) => Location = CleanOptional(location);

    public void SetMode(Mode mode)
    {
        RequireSetup("Der Modus");
        Mode = mode;
    }

    public void SetFormat(MatchFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        RequireSetup("Das Satzformat");
        format.Validate();
        Format = format;
    }

    public void RotateAdminToken() => AdminToken = NewToken();

    // --- Teilnehmer -------------------------------------------------------

    public Participant AddParticipant(string name)
    {
        RequireSetup("Die Teilnehmerliste");
        var clean = CleanName(name);

        if (_participants.Any(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"„{clean}“ steht schon auf der Liste.");
        }

        if (_participants.Count >= MaxParticipants)
        {
            throw new DomainException($"Mehr als {MaxParticipants} Teilnehmer passen nicht in ein Turnier.");
        }

        var participant = new Participant(Guid.NewGuid(), clean);
        _participants.Add(participant);
        return participant;
    }

    public void RemoveParticipant(Guid participantId)
    {
        RequireSetup("Die Teilnehmerliste");
        var removed = _participants.RemoveAll(p => p.Id == participantId);

        if (removed == 0)
        {
            throw new DomainException("Diesen Teilnehmer gibt es nicht.");
        }
    }

    public Participant? FindParticipant(string name)
    {
        var clean = name.Trim();
        return _participants.FirstOrDefault(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase))
            ?? _participants.SingleOrDefaultSafe(p => p.Name.Contains(clean, StringComparison.OrdinalIgnoreCase));
    }

    // --- Auslosung --------------------------------------------------------

    /// <summary>
    /// Lost aus und legt alle Matches an. Die Reihenfolge der Teilnehmer wird
    /// gemischt — unter Freunden gibt es keine Setzliste. Ein fester Zufall ist
    /// nur für Tests.
    /// </summary>
    public void Draw(Random? random = null)
    {
        RequireSetup("Die Auslosung");

        if (_participants.Count < 2)
        {
            throw new DomainException("Zum Auslosen braucht es mindestens zwei Teilnehmer.");
        }

        var order = _participants.ToList();
        (random ?? Random.Shared).Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));

        _matches.Clear();
        _matches.AddRange(Mode == Mode.Knockout ? KnockoutDraw.Build(order) : RoundRobinDraw.Build(order));
        State = TournamentState.Running;

        // Freilose sind entschieden, bevor ein Ball fliegt.
        foreach (var match in _matches.Where(m => m.IsBye).ToList())
        {
            var advancing = match.Side1.Kind == SideKind.Bye ? 2 : 1;
            RecordResult(match.Id, Score.ByeFor(advancing));
        }
    }

    /// <summary>Nimmt die Auslosung zurück. Alle Matches und Ergebnisse gehen verloren.</summary>
    public void UndoDraw()
    {
        if (State == TournamentState.Setup)
        {
            throw new DomainException("Es ist noch nichts ausgelost.");
        }

        _matches.Clear();
        State = TournamentState.Setup;
    }

    // --- Ergebnisse -------------------------------------------------------

    public Match FindMatch(Guid matchId) =>
        _matches.FirstOrDefault(m => m.Id == matchId)
        ?? throw new DomainException("Dieses Match gibt es nicht.");

    public void RecordResult(Guid matchId, Score score)
    {
        ArgumentNullException.ThrowIfNull(score);
        RequireDrawn();

        var match = FindMatch(matchId);

        if (match.Status == MatchStatus.Pending && !match.IsBye)
        {
            throw new DomainException($"{Describe(match)}: die Gegner stehen noch nicht fest.");
        }

        if (match.IsBye && score.Outcome != MatchOutcome.Bye)
        {
            throw new DomainException($"{Describe(match)} ist ein Freilos, da gibt es kein Ergebnis.");
        }

        if (match.Score is not null && Dependents(match).Any(d => d.Score is not null))
        {
            throw new DomainException(
                $"{Describe(match)}: das Folgematch hat schon ein Ergebnis. Zuerst dort zurücknehmen.");
        }

        match.SetScore(score);

        foreach (var dependent in Dependents(match))
        {
            dependent.Resolve(match.Id, match.WinnerId!.Value);
        }

        State = _matches.All(m => m.Status == MatchStatus.Finished) ? TournamentState.Completed : TournamentState.Running;
    }

    public void ClearResult(Guid matchId)
    {
        RequireDrawn();
        var match = FindMatch(matchId);

        if (match.Score is null)
        {
            throw new DomainException($"{Describe(match)} hat noch kein Ergebnis.");
        }

        if (match.IsBye)
        {
            throw new DomainException($"{Describe(match)} ist ein Freilos, das bleibt.");
        }

        if (Dependents(match).Any(d => d.Score is not null))
        {
            throw new DomainException(
                $"{Describe(match)}: das Folgematch hat schon ein Ergebnis. Zuerst dort zurücknehmen.");
        }

        match.SetScore(null);

        foreach (var dependent in Dependents(match))
        {
            dependent.Unresolve(match.Id);
        }

        State = TournamentState.Running;
    }

    // --- Tabelle ----------------------------------------------------------

    /// <summary>
    /// Jeder gegen jeden: Siege, dann Satz-, dann Spieldifferenz. K.o.: Sieger,
    /// Finalist, danach die Ausgeschiedenen nach der Runde ihres Ausscheidens.
    /// </summary>
    public IReadOnlyList<Standing> Standings()
    {
        var tallies = _participants.ToDictionary(p => p.Id, p => new Tally(p));

        foreach (var match in _matches.Where(m => m.Score is { Outcome: not MatchOutcome.Bye }))
        {
            tallies[match.Side1.ParticipantId!.Value].Account(match);
            tallies[match.Side2.ParticipantId!.Value].Account(match);
        }

        return Mode == Mode.RoundRobin ? RankTable(tallies.Values) : RankPlacement(tallies.Values);
    }

    private static IReadOnlyList<Standing> RankTable(IEnumerable<Tally> tallies)
    {
        var ordered = tallies
            .OrderByDescending(t => t.Won)
            .ThenByDescending(t => t.SetsWon - t.SetsLost)
            .ThenByDescending(t => t.GamesWon - t.GamesLost)
            .ThenBy(t => t.Participant.Name, StringComparer.Ordinal)
            .ToList();

        var result = new List<Standing>(ordered.Count);
        var rank = 0;
        (int, int, int)? previous = null;

        for (var i = 0; i < ordered.Count; i++)
        {
            var t = ordered[i];
            var key = (t.Won, t.SetsWon - t.SetsLost, t.GamesWon - t.GamesLost);

            if (key != previous)
            {
                rank = i + 1;
                previous = key;
            }

            result.Add(t.ToStanding(rank));
        }

        return result;
    }

    private IReadOnlyList<Standing> RankPlacement(IEnumerable<Tally> tallies)
    {
        var final = _matches.Count == 0 ? null : _matches.MaxBy(m => m.Round);
        var maxRound = final?.Round ?? 0;

        int PlacementRank(Tally t)
        {
            if (final?.WinnerId == t.Participant.Id)
            {
                return 1;
            }

            if (final?.LoserId == t.Participant.Id)
            {
                return 2;
            }

            return t.EliminatedInRound is { } round ? 100 + (maxRound - round) : 5;
        }

        var groups = tallies
            .GroupBy(PlacementRank)
            .OrderBy(g => g.Key)
            .ToList();

        var result = new List<Standing>();
        var rank = 1;

        foreach (var group in groups)
        {
            var members = group.OrderBy(t => t.Participant.Name, StringComparer.Ordinal).ToList();
            result.AddRange(members.Select(t => t.ToStanding(rank)));
            rank += members.Count;
        }

        return result;
    }

    // --- Speicher ---------------------------------------------------------

    public TournamentSnapshot ToSnapshot() => new(
        Id,
        Name,
        Date,
        Location,
        Mode,
        Format,
        State,
        OwnerId,
        AdminToken,
        CreatedAt,
        _participants.ToList(),
        _matches.Select(m => new MatchSnapshot(
            m.Id,
            m.Round,
            m.Position,
            m.Label,
            m.Side1,
            m.Side2,
            m.Score is null ? null : new ScoreSnapshot(m.Score.Outcome, m.Score.WinnerSide, m.Score.CompletedSets, m.Score.AbandonedSet)))
        .ToList());

    public static Tournament FromSnapshot(TournamentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Tournament(
            snapshot.Id,
            snapshot.Name,
            snapshot.Date,
            snapshot.Location,
            snapshot.Mode,
            snapshot.Format,
            snapshot.State,
            snapshot.OwnerId,
            snapshot.AdminToken,
            snapshot.CreatedAt,
            snapshot.Participants.ToList(),
            snapshot.Matches.Select(m => new Match(
                m.Id,
                m.Round,
                m.Position,
                m.Label,
                m.Side1,
                m.Side2,
                m.Score is null ? null : Score.Rehydrate(m.Score.Outcome, m.Score.WinnerSide, m.Score.CompletedSets, m.Score.AbandonedSet)))
            .ToList());
    }

    // --- Hilfen -----------------------------------------------------------

    public string NameOf(Side side) => side.Kind switch
    {
        SideKind.Participant => _participants.FirstOrDefault(p => p.Id == side.ParticipantId)?.Name ?? "?",
        SideKind.Bye => "Freilos",
        _ => $"Sieger aus {FindMatch(side.SourceMatchId!.Value).Label}",
    };

    public string Describe(Match match) => $"{match.Label} ({NameOf(match.Side1)} – {NameOf(match.Side2)})";

    private IEnumerable<Match> Dependents(Match match) => _matches.Where(m => m.DependsOn(match.Id));

    private void RequireSetup(string what)
    {
        if (State != TournamentState.Setup)
        {
            throw new DomainException($"{what} lässt sich nach der Auslosung nicht mehr ändern. Erst die Auslosung zurücknehmen.");
        }
    }

    private void RequireDrawn()
    {
        if (State == TournamentState.Setup)
        {
            throw new DomainException("Es ist noch nicht ausgelost.");
        }
    }

    private static string CleanName(string name)
    {
        var clean = RequireText(name, "Der Name");

        if (clean.Length > 80)
        {
            throw new DomainException("Der Name ist zu lang (höchstens 80 Zeichen).");
        }

        return clean;
    }

    private static string? CleanOptional(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string RequireText(string? text, string what)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainException($"{what} darf nicht leer sein.");
        }

        return text.Trim();
    }

    private static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

internal static class EnumerableExtensions
{
    /// <summary>Genau ein Treffer — sonst null, auch bei mehreren.</summary>
    public static T? SingleOrDefaultSafe<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        where T : class
    {
        T? found = null;

        foreach (var item in source.Where(predicate))
        {
            if (found is not null)
            {
                return null;
            }

            found = item;
        }

        return found;
    }
}
