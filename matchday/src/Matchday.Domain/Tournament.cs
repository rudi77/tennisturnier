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
        Discipline discipline,
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
        Discipline = discipline;
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

    public Discipline Discipline { get; private set; }

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
        string? location = null,
        Discipline discipline = Discipline.Singles)
    {
        format ??= MatchFormat.Standard;
        format.Validate();

        return new Tournament(
            Guid.NewGuid(),
            CleanName(name),
            date,
            CleanOptional(location),
            mode,
            discipline,
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

    /// <summary>
    /// Einzel oder Doppel. Der Wechsel geht nur mit leerer Teilnehmerliste: Ein
    /// Einzelname ist kein Team, und ein Team ist kein Einzelname — was schon
    /// auf der Liste steht, ließe sich nicht umdeuten.
    /// </summary>
    public void SetDiscipline(Discipline discipline)
    {
        RequireSetup("Die Disziplin");

        if (discipline != Discipline && _participants.Count > 0)
        {
            throw new DomainException(discipline == Discipline.Doubles
                ? "Im Doppel besteht jeder Teilnehmer aus zwei Spielern. Erst die Teilnehmerliste leeren, dann auf Doppel wechseln."
                : "Im Einzel steht ein Name je Teilnehmer. Erst die Teilnehmerliste leeren, dann auf Einzel wechseln.");
        }

        Discipline = discipline;
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

    /// <summary>
    /// Trägt einen Teilnehmer ein. Im Einzel ist das ein Name, im Doppel ein
    /// Paar — „Anna / Tom“, „Anna und Tom“, „Anna + Tom“.
    /// </summary>
    public Participant AddParticipant(string name)
    {
        RequireSetup("Die Teilnehmerliste");
        var players = Lineups.Split(RequireText(name, "Der Name")).Select(CleanName).ToList();
        RequireLineup(players);

        var clean = Lineups.Compose(players);

        if (_participants.Any(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"„{clean}“ steht schon auf der Liste.");
        }

        // Im Doppel zählt auch der einzelne Spieler: Wer in zwei Teams steht,
        // müsste gegen sich selbst spielen.
        foreach (var player in players)
        {
            if (_participants.FirstOrDefault(p => p.Has(player)) is { } other)
            {
                throw new DomainException($"„{player}“ spielt schon in „{other.Name}“ mit.");
            }
        }

        if (players.Count == 2 && string.Equals(players[0], players[1], StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("Ein Doppel braucht zwei verschiedene Spieler.");
        }

        if (_participants.Count >= MaxParticipants)
        {
            throw new DomainException($"Mehr als {MaxParticipants} Teilnehmer passen nicht in ein Turnier.");
        }

        var participant = new Participant(Guid.NewGuid(), clean, players);
        _participants.Add(participant);
        return participant;
    }

    /// <summary>So viele Spieler, wie die Disziplin verlangt — einer oder zwei.</summary>
    private void RequireLineup(IReadOnlyList<string> players)
    {
        if (players.Count == 0)
        {
            throw new DomainException("Der Name darf nicht leer sein.");
        }

        if (Discipline == Discipline.Singles && players.Count > 1)
        {
            throw new DomainException(
                $"Dieses Turnier ist ein Einzel — „{Lineups.Compose(players)}“ sind zwei Spieler. Entweder auf Doppel umstellen oder einen Namen eintragen.");
        }

        if (Discipline == Discipline.Doubles && players.Count != 2)
        {
            throw new DomainException(
                $"Im Doppel besteht ein Team aus zwei Spielern, getrennt durch „/“ — etwa „{players[0]} / Partner“.");
        }
    }

    /// <summary>
    /// Würfelt aus einzelnen Spielern Teams und trägt sie ein — das Los für die
    /// Paarungen, nur im Doppel. Gemischt wird hier, wie bei der Auslosung: Wer
    /// mit wem spielt, entscheidet über den Turnierverlauf, und das ist eine
    /// Sache der Anwendung und nicht des Modells, das sich Namen ausdenken
    /// könnte.
    ///
    /// Geprüft wird alles vor dem ersten Eintrag. Fiele der Fehler erst beim
    /// dritten Paar auf, stünden zwei erwürfelte Teams auf der Liste, die so
    /// niemand bestellt hat — und ein zweiter Versuch würfelte sie nicht neu.
    /// </summary>
    public IReadOnlyList<Participant> AddRandomTeams(IReadOnlyList<string> players, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(players);
        RequireSetup("Die Teilnehmerliste");

        if (Discipline != Discipline.Doubles)
        {
            throw new DomainException(
                "Zufällige Teams gibt es nur im Doppel — im Einzel spielt jeder für sich. Erst auf Doppel umstellen, dann lose ich die Paare aus.");
        }

        var names = players.Select(CleanName).ToList();

        if (names.Count < 2)
        {
            throw new DomainException("Zum Auslosen der Teams braucht es mindestens zwei Spieler.");
        }

        if (names.Count % 2 == 1)
        {
            throw new DomainException(
                $"{names.Count} Spieler gehen im Doppel nicht auf: ein Team sind zwei. Nimm einen heraus oder nenn mir einen weiteren.");
        }

        // Die Obergrenze steht vor der Namensprüfung: Eine Liste mit tausenden
        // Namen soll nicht erst Namen gegen Namen geprüft werden, um am Ende an
        // der Grenze zu scheitern. Danach sind es höchstens 128 Namen.
        if (_participants.Count + (names.Count / 2) > MaxParticipants)
        {
            throw new DomainException($"Mehr als {MaxParticipants} Teilnehmer passen nicht in ein Turnier.");
        }

        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            // Ein Eintrag ist ein Spieler. Stünde hier schon ein Paar, käme mit
            // dem Partner ein Dreier heraus — und das fiele erst beim Eintragen
            // auf, mitten in der halb gewürfelten Liste.
            if (Lineups.Split(name).Count != 1)
            {
                throw new DomainException($"„{name}“ ist kein einzelner Spieler. Nenn mir die Spieler einzeln, die Paare würfle ich.");
            }

            if (!gesehen.Add(name))
            {
                throw new DomainException($"„{name}“ steht zweimal in der Liste. Jeder Spieler spielt in genau einem Team.");
            }

            if (_participants.FirstOrDefault(p => p.Has(name)) is { } schon)
            {
                throw new DomainException($"„{name}“ spielt schon in „{schon.Name}“ mit.");
            }
        }

        (random ?? Random.Shared).Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(names));

        var teams = new List<Participant>();

        for (var i = 0; i < names.Count; i += 2)
        {
            teams.Add(AddParticipant(Lineups.Compose([names[i], names[i + 1]])));
        }

        return teams;
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

    /// <summary>
    /// Den Teilnehmer zu einem Namen. Im Doppel trifft auch das Paar in anderer
    /// Schreibweise („Tom/Anna“) und der einzelne Spieler („Anna“).
    /// </summary>
    public Participant? FindParticipant(string name)
    {
        var clean = name.Trim();
        var asked = Lineups.Split(clean);

        // Ohne Namen ist jeder ein Treffer — das wäre kein Fund, sondern der
        // erste in der Liste.
        if (asked.Count == 0)
        {
            return null;
        }

        return _participants.FirstOrDefault(p => p.Is(asked))
            ?? _participants.FirstOrDefault(p => p.IsCalled(clean))
            ?? _participants.SingleOrDefaultSafe(p => p.Mentions(clean));
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

        // Freilose sind entschieden, bevor ein Ball fliegt. Das Freilos steht auf
        // Seite 2 (siehe Match.IsBye), also kommt Seite 1 weiter.
        foreach (var match in _matches.Where(m => m.IsBye).ToList())
        {
            RecordResult(match.Id, Score.ByeFor(advancingSide: 1));
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

        // Jedem Match sagen, dass hier ein Sieger steht: nachrücken tut nur, wer
        // auf dieses Match wartet — das entscheidet Resolve selbst.
        foreach (var other in _matches)
        {
            other.Resolve(match.Id, match.WinnerId!.Value);
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

        foreach (var other in _matches)
        {
            other.Unresolve(match.Id);
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
            tallies[match.Side1.ParticipantId!.Value].Account(match, side: 1);
            tallies[match.Side2.ParticipantId!.Value].Account(match, side: 2);
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
        Discipline,
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
            snapshot.Discipline,
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
        SideKind.Participant => _participants.FirstOrDefault(p => p.Id == side.ParticipantId!.Value)?.Name ?? "?",
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
