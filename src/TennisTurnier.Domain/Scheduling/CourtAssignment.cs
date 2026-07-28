using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Scheduling;

/// <summary>Woher eine Zuweisung stammt (ADR-0002).</summary>
public enum AssignmentSource
{
    /// <summary>Vom Solver vorgeschlagen und bestätigt.</summary>
    Auto,

    /// <summary>Von der Turnierleitung von Hand gesetzt.</summary>
    Manual,

    /// <summary>Festgenagelt — der Solver darf sie nicht anrühren.</summary>
    Pinned,
}

/// <summary>Stand einer Zuweisung im Tagesbetrieb.</summary>
public enum AssignmentStatus
{
    /// <summary>Eingeplant.</summary>
    Planned,

    /// <summary>Aufgerufen, die Spieler sind unterwegs zum Platz.</summary>
    Called,

    /// <summary>Läuft.</summary>
    Running,

    /// <summary>Beendet.</summary>
    Finished,

    /// <summary>Unterbrochen — Regen, Verletzungspause.</summary>
    Suspended,
}

/// <summary>
/// Die Zuweisung eines Matches auf einen Platz.
///
/// Bewusst eine eigene Entität und kein Feldpaar am Match (ADR-0002): ein Match
/// kann verschoben, unterbrochen und auf einem anderen Platz fortgesetzt werden.
/// Am Turniertag ist die Frage „wo stand das vorher?" Gold wert, und sie wäre
/// unbeantwortbar, wenn jede Verschiebung die vorige überschriebe.
/// </summary>
public sealed class CourtAssignment : Entity
{
    public CourtAssignment(
        Guid id,
        Guid tournamentId,
        Guid matchId,
        Guid courtId,
        int sequenceOnCourt,
        TimeSpan estimatedDuration,
        AssignmentSource source)
        : base(id)
    {
        if (tournamentId == Guid.Empty || matchId == Guid.Empty || courtId == Guid.Empty)
        {
            throw new DomainException("Eine Platzzuweisung braucht Turnier, Match und Platz.");
        }

        if (estimatedDuration <= TimeSpan.Zero)
        {
            throw new DomainException($"Die geschätzte Dauer muss positiv sein, war {estimatedDuration}.");
        }

        TournamentId = tournamentId;
        MatchId = matchId;
        CourtId = courtId;
        Source = source;
        Status = AssignmentStatus.Planned;
        EstimatedDuration = estimatedDuration;
        SetSequence(sequenceOnCourt);
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private CourtAssignment(Guid id) : base(id)
    {
    }

    public Guid TournamentId { get; private set; }

    public Guid MatchId { get; private set; }

    public Guid CourtId { get; private set; }

    /// <summary>Position in der Warteschlange des Platzes, beginnend bei 1.</summary>
    public int SequenceOnCourt { get; private set; }

    /// <summary>
    /// Zusage „nicht vor". Im Turniertagbetrieb der einzige Zeitpunkt, den es
    /// gibt — eine feste Startzeit wäre eine Behauptung, die beim ersten langen
    /// Match zerfällt (ADR-0002).
    /// </summary>
    public DateTimeOffset? EarliestStart { get; private set; }

    /// <summary>
    /// Geplanter Beginn. Nur im Planungsmodus gefüllt, und dort eine Schätzung
    /// zur Machbarkeitsprüfung, keine Zusage.
    /// </summary>
    public DateTimeOffset? PlannedStart { get; private set; }

    public TimeSpan EstimatedDuration { get; private set; }

    public DateTimeOffset? ActualStart { get; private set; }

    public DateTimeOffset? ActualEnd { get; private set; }

    public AssignmentSource Source { get; private set; }

    public AssignmentStatus Status { get; private set; }

    /// <summary>Zähler für optimistische Nebenläufigkeit.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Das geplante Zeitfenster, sofern eine Planzeit gesetzt ist. Grundlage der
    /// Überschneidungsprüfung im Planungsmodus.
    /// </summary>
    public TimeSlot? PlannedSlot =>
        PlannedStart is { } start ? new TimeSlot(start, start + EstimatedDuration) : null;

    /// <summary>Der Solver darf eine gesetzte oder festgenagelte Zuweisung nicht verschieben.</summary>
    public bool IsFixedForSolver => Source is AssignmentSource.Manual or AssignmentSource.Pinned;

    public bool IsOver => Status is AssignmentStatus.Finished;

    public void SetSequence(int sequenceOnCourt)
    {
        if (sequenceOnCourt < 1)
        {
            throw new DomainException($"Eine Position in der Warteschlange beginnt bei 1, war {sequenceOnCourt}.");
        }

        SequenceOnCourt = sequenceOnCourt;
        Touch();
    }

    public void PlanFor(DateTimeOffset start, TimeSpan? estimatedDuration = null)
    {
        if (estimatedDuration is { } duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new DomainException($"Die geschätzte Dauer muss positiv sein, war {duration}.");
            }

            EstimatedDuration = duration;
        }

        PlannedStart = start;
        Touch();
    }

    public void PromiseNotBefore(DateTimeOffset earliestStart)
    {
        EarliestStart = earliestStart;
        Touch();
    }

    public void MarkAs(AssignmentSource source)
    {
        Source = source;
        Touch();
    }

    /// <summary>
    /// Setzt die gesamte Planung einer noch nicht aufgerufenen Zuweisung neu.
    ///
    /// Bewusst alle Angaben auf einmal statt einzeln: eine Umplanung ohne
    /// Planzeit soll keine alte Planzeit stehen lassen. Und bewusst dieselbe
    /// Zuweisung statt einer neuen — der Zähler <see cref="Version"/>
    /// wirkt nur auf einer bestehenden Zeile. Zwei gleichzeitige Zuweisungen
    /// desselben Matches liefen sonst beide durch und hinterließen zwei aktive
    /// Zuweisungen, von denen die zweite die erste stillschweigend verdrängt.
    /// </summary>
    public void Replan(
        Guid courtId,
        int sequenceOnCourt,
        DateTimeOffset? plannedStart,
        DateTimeOffset? earliestStart,
        TimeSpan estimatedDuration,
        AssignmentSource source)
    {
        RequireStatus(AssignmentStatus.Planned);

        if (courtId == Guid.Empty)
        {
            throw new DomainException("Eine Platzzuweisung braucht einen Platz.");
        }

        if (estimatedDuration <= TimeSpan.Zero)
        {
            throw new DomainException($"Die geschätzte Dauer muss positiv sein, war {estimatedDuration}.");
        }

        CourtId = courtId;
        PlannedStart = plannedStart;
        EarliestStart = earliestStart;
        EstimatedDuration = estimatedDuration;
        Source = source;
        SetSequence(sequenceOnCourt);
    }

    // --- Tagesbetrieb ------------------------------------------------------

    public void Call()
    {
        RequireStatus(AssignmentStatus.Planned, AssignmentStatus.Suspended);
        Status = AssignmentStatus.Called;
        Touch();
    }

    public void Start(DateTimeOffset at)
    {
        RequireStatus(AssignmentStatus.Planned, AssignmentStatus.Called, AssignmentStatus.Suspended);

        ActualStart ??= at;
        Status = AssignmentStatus.Running;
        Touch();
    }

    public void Finish(DateTimeOffset at)
    {
        RequireStatus(AssignmentStatus.Running, AssignmentStatus.Suspended);

        if (ActualStart is { } start && at < start)
        {
            throw new DomainException($"Das Ende ({at:O}) liegt vor dem Beginn ({start:O}).");
        }

        ActualEnd = at;
        Status = AssignmentStatus.Finished;
        Touch();
    }

    /// <summary>
    /// Unterbricht. Die begonnene Zeit bleibt stehen — bei einer Fortsetzung auf
    /// einem anderen Platz entsteht eine neue Zuweisung, und erst beide zusammen
    /// erzählen, was an diesem Tag passiert ist.
    /// </summary>
    public void Suspend()
    {
        RequireStatus(AssignmentStatus.Called, AssignmentStatus.Running);
        Status = AssignmentStatus.Suspended;
        Touch();
    }

    private void RequireStatus(params AssignmentStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException(
                $"Dieser Schritt setzt eine Zuweisung im Zustand [{string.Join(", ", allowed)}] voraus, " +
                $"sie war {Status}.");
        }
    }

    private void Touch() => Version++;

    public override string ToString() =>
        $"Match {MatchId} auf Platz {CourtId}, Position {SequenceOnCourt} ({Status})";
}
