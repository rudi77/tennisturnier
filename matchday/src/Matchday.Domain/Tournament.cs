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
        List<Match> matches,
        TimeOnly? startTime,
        DateTimeOffset? startedAt)
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
        StartTime = startTime;
        StartedAt = startedAt;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public DateOnly? Date { get; private set; }

    /// <summary>
    /// Wann es losgehen soll — die Uhrzeit am Ort, ohne Zeitzone. Unter
    /// Freunden stehen alle am selben Platz; worauf der Countdown zählt,
    /// rechnet das Gerät des Zuschauers in seiner eigenen Zeit (ADR-0024).
    /// </summary>
    public TimeOnly? StartTime { get; private set; }

    /// <summary>
    /// Wann die Turnierleitung auf „Start“ gedrückt hat. Erst ab da wird
    /// gezählt und eingetragen, und erst ab da steht der Rahmen fest.
    /// </summary>
    public DateTimeOffset? StartedAt { get; private set; }

    public string? Location { get; private set; }

    public Mode Mode { get; private set; }

    public Discipline Discipline { get; private set; }

    public MatchFormat Format { get; private set; }

    public TournamentState State { get; private set; }

    /// <summary>Die Browserkennung dessen, der das Turnier angelegt hat. Kein Konto.</summary>
    public string OwnerId { get; }

    /// <summary>Der geheime Teil des Verwalterlinks. Wer ihn hat, darf alles.</summary>
    public string AdminToken { get; private set; }

    /// <summary>
    /// Der geheime Teil des Eintragen-Links: Wer ihn hat, darf Spielstände und
    /// Ergebnisse eintragen, sonst nichts. Er ist aus dem Verwaltertoken
    /// abgeleitet und nicht gespeichert — so wird er mit dem Verwalterlink
    /// zusammen rotiert, und aus ihm lässt sich der Verwalterlink nicht
    /// zurückrechnen.
    /// </summary>
    public string ScorerToken => Derive("matchday-scorer:" + AdminToken);

    /// <summary>
    /// Ob gespielt wird: Ab dem ausdrücklichen Start stehen Teilnehmer, Modus
    /// und Format fest (ADR-0024). Bis dahin ist auch ein ausgelostes Turnier
    /// noch zu ändern — die Auslosung wird dann neu gemacht.
    /// </summary>
    public bool IsStarted => StartedAt is not null;

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
        Discipline discipline = Discipline.Singles,
        TimeOnly? startTime = null)
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
            [],
            startTime,
            null);
    }

    // --- Stammdaten -------------------------------------------------------

    public void Rename(string name) => Name = CleanName(name);

    public void SetDate(DateOnly? date) => Date = date;

    public void SetStartTime(TimeOnly? startTime) => StartTime = startTime;

    public void SetLocation(string? location) => Location = CleanOptional(location);

    public void SetMode(Mode mode) => Change("Der Modus", () => Mode = mode);

    /// <summary>
    /// Einzel oder Doppel. Der Wechsel geht nur mit leerer Teilnehmerliste: Ein
    /// Einzelname ist kein Team, und ein Team ist kein Einzelname — was schon
    /// auf der Liste steht, ließe sich nicht umdeuten.
    /// </summary>
    public void SetDiscipline(Discipline discipline)
    {
        RequireNotStarted("Die Disziplin");

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
        RequireNotStarted("Das Satzformat");
        format.Validate();

        // Die Paarungen hängen nicht am Format: Neu gelost wird dafür nicht.
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
        Participant? added = null;
        Change("Die Teilnehmerliste", () => added = Enter(name));
        return added!;
    }

    private Participant Enter(string name)
    {
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
        RequireNotStarted("Die Teilnehmerliste");

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

        Change("Die Teilnehmerliste", () =>
        {
            for (var i = 0; i < names.Count; i += 2)
            {
                teams.Add(Enter(Lineups.Compose([names[i], names[i + 1]])));
            }
        });

        return teams;
    }

    public void RemoveParticipant(Guid participantId)
    {
        if (_participants.All(p => p.Id != participantId))
        {
            throw new DomainException("Diesen Teilnehmer gibt es nicht.");
        }

        Change("Die Teilnehmerliste", () => _participants.RemoveAll(p => p.Id == participantId));
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
        if (State != TournamentState.Setup)
        {
            throw new DomainException("Es ist schon ausgelost. Wer neu losen will, nimmt die Auslosung erst zurück.");
        }

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
            Settle(match.Id, Score.ByeFor(advancingSide: 1));
        }
    }

    /// <summary>
    /// Der Anpfiff. Ausdrücklich, nicht mit dem ersten Punkt: Bis hierher
    /// zählt der Countdown, ab hier wird gespielt und eingetragen (ADR-0024).
    /// </summary>
    public void Start(DateTimeOffset now)
    {
        if (State == TournamentState.Setup)
        {
            throw new DomainException("Erst auslosen, dann starten.");
        }

        if (IsStarted)
        {
            throw new DomainException("Das Turnier läuft schon.");
        }

        StartedAt = now;
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
        StartedAt = null;
    }

    // --- Ergebnisse -------------------------------------------------------

    public Match FindMatch(Guid matchId) =>
        _matches.FirstOrDefault(m => m.Id == matchId)
        ?? throw new DomainException("Dieses Match gibt es nicht.");

    /// <summary>
    /// Trägt ein ganzes Ergebnis ein. Was bis dahin live mitgezählt wurde, ist
    /// damit ersetzt: Das Ergebnis ist, was jemand ausdrücklich gesagt hat.
    /// </summary>
    public void RecordResult(Guid matchId, Score score)
    {
        ArgumentNullException.ThrowIfNull(score);
        RequireStarted();
        Settle(matchId, score);
        FindMatch(matchId).SetLive(null);
    }

    private void Settle(Guid matchId, Score score)
    {
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

    /// <summary>Nimmt ein Ergebnis zurück — und mit ihm, was live dazu gezählt wurde.</summary>
    public void ClearResult(Guid matchId)
    {
        Unsettle(matchId);
        FindMatch(matchId).SetLive(null);
    }

    private void Unsettle(Guid matchId)
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

    // --- Live ------------------------------------------------------------

    /// <summary>
    /// Ein Punkt oder ein Spiel während des Matches. Ist das Match damit
    /// entschieden, steht das Ergebnis — so, als hätte es jemand eingetragen.
    /// </summary>
    public LiveState ScoreLive(Guid matchId, LiveEvent liveEvent)
    {
        ArgumentNullException.ThrowIfNull(liveEvent);
        RequireStarted();
        var match = FindMatch(matchId);

        if (match.IsBye)
        {
            throw new DomainException($"{Describe(match)} ist ein Freilos, da wird nicht gespielt.");
        }

        if (match.Status == MatchStatus.Pending)
        {
            throw new DomainException($"{Describe(match)}: die Gegner stehen noch nicht fest.");
        }

        if (match.Score is not null && match.Live is null)
        {
            throw new DomainException($"{Describe(match)} hat schon ein Ergebnis. Erst zurücknehmen, dann live zählen.");
        }

        var events = new List<LiveEvent>(match.Live ?? []) { liveEvent };
        var state = LiveScoring.Replay(events, Format);
        match.SetLive(events);

        if (state.WinnerSide is not null)
        {
            Settle(match.Id, Score.Played(state.CompletedSets, Format));
        }

        return state;
    }

    /// <summary>
    /// Nimmt das letzte Live-Ereignis zurück. War das Match damit entschieden,
    /// ist das Ergebnis wieder weg — sofern das Folgematch noch keines hat.
    /// </summary>
    public LiveState UndoLive(Guid matchId)
    {
        RequireDrawn();
        var match = FindMatch(matchId);

        if (match.Live is not { } events)
        {
            throw new DomainException($"{Describe(match)}: hier wurde nichts live gezählt.");
        }

        if (match.Score is not null)
        {
            Unsettle(match.Id);
        }

        var rest = events.Take(events.Count - 1).ToList();
        match.SetLive(rest);
        return LiveScoring.Replay(rest, Format);
    }

    /// <summary>Der laufende Stand eines Matches — oder null, wenn niemand mitzählt.</summary>
    public LiveState? LiveStateOf(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.Live is { } events ? LiveScoring.Replay(events, Format) : null;
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
            m.Score is null ? null : new ScoreSnapshot(m.Score.Outcome, m.Score.WinnerSide, m.Score.CompletedSets, m.Score.AbandonedSet),
            m.Live))
        .ToList(),
        StartTime,
        StartedAt);

    public static Tournament FromSnapshot(TournamentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var matches = snapshot.Matches.Select(m => new Match(
                m.Id,
                m.Round,
                m.Position,
                m.Label,
                m.Side1,
                m.Side2,
                m.Score is null ? null : Score.Rehydrate(m.Score.Outcome, m.Score.WinnerSide, m.Score.CompletedSets, m.Score.AbandonedSet),
                m.Live))
            .ToList();

        // Vor ADR-0024 begann ein Turnier mit dem ersten Punkt. Wo schon
        // gespielt wurde, gilt es als gestartet — wann genau, weiß niemand
        // mehr; das Anlegen ist die ehrlichste Näherung.
        var startedAt = snapshot.StartedAt ?? (matches.Any(m => m.HasBegun) ? snapshot.CreatedAt : null);

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
            matches,
            snapshot.StartTime,
            startedAt);
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

    /// <summary>
    /// Eine Änderung an dem, was die Auslosung trägt. Vor der Auslosung geht sie
    /// einfach; danach, solange nicht gestartet ist, wird neu gelost —
    /// die alten Paarungen passten nicht mehr zur neuen Liste oder zum neuen
    /// Modus. Reichen die Teilnehmer dafür nicht mehr, bleibt es bei der
    /// Vorbereitung.
    /// </summary>
    private void Change(string what, Action change)
    {
        RequireNotStarted(what);
        var drawn = State != TournamentState.Setup;

        if (drawn)
        {
            _matches.Clear();
            State = TournamentState.Setup;
        }

        change();

        if (drawn && _participants.Count >= 2)
        {
            Draw();
        }
    }

    private void RequireNotStarted(string what)
    {
        if (IsStarted)
        {
            throw new DomainException(
                $"{what} lässt sich nicht mehr ändern: Das Turnier ist gestartet. Wer den Rahmen ändern will, nimmt erst die Auslosung zurück.");
        }
    }

    private void RequireStarted()
    {
        RequireDrawn();

        if (!IsStarted)
        {
            throw new DomainException("Das Turnier ist noch nicht gestartet. Erst starten, dann wird gezählt.");
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

    private static string Derive(string text)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(hash, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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
