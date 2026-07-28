using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;

namespace TennisTurnier.Domain.Tournaments;

/// <summary>
/// Ein Turnier eines Vereins.
///
/// Der Zustandsautomat liegt hier und nicht in einem Anwendungsfall: dass ab
/// <see cref="TournamentState.DrawGenerated"/> Teilnehmerfeld und Format
/// eingefroren sind, ist eine fachliche Regel und darf nicht davon abhängen,
/// über welchen Endpunkt jemand hereinkommt.
/// </summary>
public sealed class Tournament : Entity
{
    private readonly List<TournamentEntry> _entries = [];

    public Tournament(
        Guid id,
        Guid clubId,
        string name,
        DateOnly startsOn,
        DateOnly endsOn,
        Guid formatTemplateId)
        : base(id)
    {
        if (clubId == Guid.Empty)
        {
            throw new DomainException("Ein Turnier braucht einen ausrichtenden Verein.");
        }

        if (formatTemplateId == Guid.Empty)
        {
            throw new DomainException("Ein Turnier braucht eine Formatvorlage.");
        }

        ClubId = clubId;
        Name = ValidateName(name);
        FormatTemplateId = formatTemplateId;
        SetDates(startsOn, endsOn);

        State = TournamentState.Draft;
        SchedulingMode = SchedulingMode.Planning;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private Tournament(Guid id) : base(id) => Name = string.Empty;

    public Guid ClubId { get; private set; }

    public string Name { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly EndsOn { get; private set; }

    public TournamentState State { get; private set; }

    public SchedulingMode SchedulingMode { get; private set; }

    /// <summary>Die Vorlage, aus der das Format stammt.</summary>
    public Guid FormatTemplateId { get; private set; }

    /// <summary>
    /// Die beim Auslosen eingefrorene Kopie der Formatdefinition samt ihrer
    /// Version (ADR-0001). Leer, solange nicht ausgelost wurde.
    ///
    /// Sie ist der Grund, warum eine Änderung an der Vorlage ein laufendes
    /// Turnier nicht berührt.
    /// </summary>
    public FormatSnapshot? Format { get; private set; }

    public IReadOnlyList<TournamentEntry> Entries => _entries;

    /// <summary>
    /// Zähler für optimistische Nebenläufigkeit.
    ///
    /// Bewusst fachlich gepflegt statt über <c>rowversion</c>: das verhält sich
    /// auf SQLite und PostgreSQL identisch (ADR-0006). Zwei Schiedsrichter, die
    /// gleichzeitig eintragen, sind am Turniertag der Normalfall.
    ///
    /// Jede ändernde Methode muss <c>Touch</c> aufrufen. Eine, die es vergisst,
    /// ist nicht bloß ungenau gezählt — für sie fällt der Schutz vollständig aus,
    /// weil die Persistenz den unveränderten Zähler als „niemand war schneller"
    /// liest.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Die Meldungen, die tatsächlich im Feld stehen.</summary>
    public IReadOnlyList<TournamentEntry> AcceptedEntries =>
        _entries.Where(e => e.IsInDraw).ToList();

    public bool IsFrozen => State is TournamentState.DrawGenerated
        or TournamentState.InProgress
        or TournamentState.Completed;

    public void Rename(string name)
    {
        RequireNotFinished();
        Name = ValidateName(name);
        Touch();
    }

    public void Reschedule(DateOnly startsOn, DateOnly endsOn)
    {
        RequireNotFinished();
        SetDates(startsOn, endsOn);
        Touch();
    }

    // --- Zustandsübergänge ------------------------------------------------

    public void OpenRegistration() => TransitionTo(TournamentState.RegistrationOpen, TournamentState.Draft);

    public void CloseRegistration() =>
        TransitionTo(TournamentState.RegistrationClosed, TournamentState.RegistrationOpen);

    /// <summary>
    /// Friert Format und Teilnehmerfeld ein. Ab hier verändert eine Änderung an
    /// der Vorlage dieses Turnier nicht mehr.
    /// </summary>
    public void GenerateDraw(FormatDefinition definition, int templateVersion)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Require(TournamentState.RegistrationClosed);

        // Erneut prüfen, obwohl die Vorlage beim Speichern schon geprüft wurde:
        // zwischen beiden Zeitpunkten liegt eine Bearbeitung der Vorlage.
        definition.Validate();

        if (AcceptedEntries.Count < 2)
        {
            throw new DomainException(
                $"Ein Turnier braucht mindestens zwei angenommene Meldungen, hatte {AcceptedEntries.Count}.");
        }

        RequireDistinctSeeds();

        Format = new FormatSnapshot(FormatTemplateId, templateVersion, definition);
        TransitionTo(TournamentState.DrawGenerated, TournamentState.RegistrationClosed);
    }

    /// <summary>
    /// Nimmt die Auslosung zurück, um nachzumelden. Ausdrücklich und mit Verlust
    /// des Draws — ein stilles Nachrücken in ein ausgelostes Feld gibt es nicht.
    /// </summary>
    public void ReopenRegistration()
    {
        Require(TournamentState.DrawGenerated);

        Format = null;
        TransitionTo(TournamentState.RegistrationOpen, TournamentState.DrawGenerated);
    }

    public void Start() => TransitionTo(TournamentState.InProgress, TournamentState.DrawGenerated);

    public void Complete() => TransitionTo(TournamentState.Completed, TournamentState.InProgress);

    public void Abandon()
    {
        if (State is TournamentState.Completed or TournamentState.Abandoned)
        {
            throw new DomainException($"Ein Turnier im Zustand {State} lässt sich nicht mehr abbrechen.");
        }

        TransitionTo(TournamentState.Abandoned, State);
    }

    /// <summary>
    /// Wechselt in den Turniertagbetrieb (ADR-0002). Ein ausdrücklicher Schritt,
    /// weil sich damit die Bedeutung jeder angezeigten Uhrzeit ändert: aus einer
    /// Schätzung wird eine Zusage.
    /// </summary>
    public void SwitchToMatchDay()
    {
        if (State is not (TournamentState.DrawGenerated or TournamentState.InProgress))
        {
            throw new DomainException(
                $"Der Turniertagbetrieb setzt eine Auslosung voraus, das Turnier war im Zustand {State}.");
        }

        SchedulingMode = SchedulingMode.MatchDay;
        Touch();
    }

    public void SwitchToPlanning()
    {
        SchedulingMode = SchedulingMode.Planning;
        Touch();
    }

    // --- Meldungen --------------------------------------------------------

    public TournamentEntry Enter(Guid entryId, Guid participantId, int? seed = null)
    {
        RequireRegistrationOpen();

        if (_entries.Any(e => e.ParticipantId == participantId && e.Status != EntryStatus.Withdrawn))
        {
            throw new DomainException("Dieser Teilnehmer ist bereits gemeldet.");
        }

        var entry = new TournamentEntry(entryId, Id, participantId, seed);
        _entries.Add(entry);
        Touch();

        return entry;
    }

    public void Accept(Guid entryId)
    {
        RequireNotFrozen();
        EntryOf(entryId).Accept();
        Touch();
    }

    public void MoveToWaitingList(Guid entryId)
    {
        RequireNotFrozen();
        EntryOf(entryId).MoveToWaitingList();
        Touch();
    }

    /// <summary>
    /// Rückzug. Nach der Auslosung ist er weiterhin möglich — ein Spieler, der
    /// nicht antritt, ist Alltag —, wird dann aber als Nichtantreten im Match
    /// gewertet und verändert den Draw nicht.
    /// </summary>
    public void Withdraw(Guid entryId)
    {
        RequireNotFinished();
        EntryOf(entryId).Withdraw();
        Touch();
    }

    public void SetSeed(Guid entryId, int? seed)
    {
        RequireNotFrozen();
        EntryOf(entryId).SetSeed(seed);
        RequireDistinctSeeds();
        Touch();
    }

    // --- Innere Helfer ----------------------------------------------------

    private TournamentEntry EntryOf(Guid entryId) =>
        _entries.FirstOrDefault(e => e.Id == entryId)
        ?? throw new DomainException($"Das Turnier hat keine Meldung mit der Id {entryId}.");

    private void TransitionTo(TournamentState target, TournamentState allowedFrom)
    {
        Require(allowedFrom);
        State = target;
        Touch();
    }

    private void Require(TournamentState expected)
    {
        if (State != expected)
        {
            throw new DomainException(
                $"Dieser Schritt setzt den Zustand {expected} voraus, das Turnier war im Zustand {State}.");
        }
    }

    private void RequireRegistrationOpen()
    {
        if (State != TournamentState.RegistrationOpen)
        {
            throw new DomainException(
                $"Meldungen nimmt nur ein Turnier im Zustand {TournamentState.RegistrationOpen} an, dieses war {State}.");
        }
    }

    private void RequireNotFrozen()
    {
        if (IsFrozen)
        {
            throw new DomainException(
                $"Ab der Auslosung ist das Teilnehmerfeld eingefroren (Zustand {State}). " +
                "Für eine Nachmeldung muss die Auslosung ausdrücklich zurückgenommen werden.");
        }

        RequireNotFinished();
    }

    private void RequireNotFinished()
    {
        if (State is TournamentState.Completed or TournamentState.Abandoned)
        {
            throw new DomainException($"Ein Turnier im Zustand {State} lässt sich nicht mehr ändern.");
        }
    }

    private void RequireDistinctSeeds()
    {
        var seeds = _entries
            .Where(e => e.Status != EntryStatus.Withdrawn && e.Seed is not null)
            .Select(e => e.Seed!.Value)
            .ToList();

        var duplicate = seeds.GroupBy(s => s).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomainException($"Die Setzposition {duplicate.Key} ist mehrfach vergeben.");
        }
    }

    private void SetDates(DateOnly startsOn, DateOnly endsOn)
    {
        if (endsOn < startsOn)
        {
            throw new DomainException($"Das Turnierende ({endsOn}) liegt vor dem Beginn ({startsOn}).");
        }

        StartsOn = startsOn;
        EndsOn = endsOn;
    }

    private void Touch() => Version++;

    private static string ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new DomainException("Ein Turnier braucht einen Namen.")
            : name.Trim();
}

/// <summary>
/// Die eingefrorene Formatdefinition eines Turniers samt Herkunft.
///
/// Version und Vorlagen-Id sind mitgeführt, damit später nachvollziehbar bleibt,
/// aus welchem Stand einer Vorlage ein Turnier hervorgegangen ist — ohne dass
/// die Vorlage selbst unveränderlich sein müsste.
/// </summary>
public sealed record FormatSnapshot(Guid TemplateId, int TemplateVersion, FormatDefinition Definition);
