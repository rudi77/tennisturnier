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
    private readonly List<TournamentCourt> _courts = [];

    public Tournament(
        Guid id,
        string name,
        Venue venue,
        Discipline discipline,
        DateOnly startsOn,
        DateOnly endsOn,
        Guid formatTemplateId)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(venue);

        if (formatTemplateId == Guid.Empty)
        {
            throw new DomainException("Ein Turnier braucht eine Formatvorlage.");
        }

        Name = ValidateName(name);
        Venue = venue;
        Discipline = discipline;
        FormatTemplateId = formatTemplateId;
        SetDates(startsOn, endsOn);

        // Der Anmeldelink entsteht mit dem Turnier und nicht erst beim Öffnen
        // der Meldung: so lässt er sich vorbereiten — auf den Aushang, in die
        // Vereinszeitung —, und er überlebt eine zurückgenommene Auslosung.
        Registration = RegistrationLink.New();

        State = TournamentState.Draft;
        SchedulingMode = SchedulingMode.Planning;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private Tournament(Guid id) : base(id)
    {
        Name = string.Empty;
        Venue = null!;
        Registration = null!;
    }

    public string Name { get; private set; }

    /// <summary>Wo gespielt wird — samt der Zeitzone, in der die Platzzeiten gelten.</summary>
    public Venue Venue { get; private set; }

    public Discipline Discipline { get; private set; }

    /// <summary>
    /// Die Plätze, die diesem Turnier zur Verfügung stehen.
    ///
    /// Reserviert werden sie außerhalb dieser Anwendung; hier steht nur, was
    /// zugesagt ist. Sie gehören dem Turnier und keinem Verein — ein Platzstamm,
    /// der über Turniere hinweg gepflegt werden müsste, stünde zwischen dem
    /// Veranstalter und seiner Ausschreibung.
    /// </summary>
    public IReadOnlyList<TournamentCourt> Courts => _courts;

    /// <summary>Der Weg, auf dem sich jemand ohne Konto zu diesem Turnier meldet.</summary>
    public RegistrationLink Registration { get; private set; }

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

    public void MoveTo(Venue venue)
    {
        ArgumentNullException.ThrowIfNull(venue);
        RequireNotFinished();
        Venue = venue;
        Touch();
    }

    /// <summary>
    /// Die Disziplin lässt sich nur ändern, solange niemand gemeldet ist.
    ///
    /// Ein Einzelturnier, das nachträglich zum Doppel erklärt wird, hätte ein
    /// Feld aus Einzelspielern — und umgekehrt Paare, von denen die Hälfte
    /// nicht mehr antreten dürfte.
    /// </summary>
    public void ChangeDiscipline(Discipline discipline)
    {
        RequireNotFinished();

        if (_entries.Count > 0 && discipline != Discipline)
        {
            throw new DomainException(
                $"Die Disziplin lässt sich nicht mehr ändern, es stehen bereits {_entries.Count} Meldungen im Feld.");
        }

        Discipline = discipline;
        Touch();
    }

    // --- Plätze -----------------------------------------------------------

    /// <summary>
    /// Nimmt einen Platz auf. Der Name muss innerhalb des Turniers eindeutig
    /// sein — nur die Wurzel kennt die Geschwisterplätze, und ein belegter Name
    /// soll als fachlicher Fehler auffallen, nicht als Datenbankfehler.
    /// </summary>
    public TournamentCourt AddCourt(
        Guid courtId,
        string name,
        CourtSurface surface,
        CourtLocation location)
    {
        RequireNotFinished();
        RequireFreeCourtName(name, exceptCourtId: null);

        var court = new TournamentCourt(courtId, Id, name, surface, location);
        _courts.Add(court);
        Touch();

        return court;
    }

    public void RenameCourt(Guid courtId, string name)
    {
        RequireNotFinished();
        RequireFreeCourtName(name, exceptCourtId: courtId);
        CourtOf(courtId).Rename(name);
        Touch();
    }

    /// <summary>
    /// Entfernt einen Platz samt seiner Zeiten.
    ///
    /// Ausdrücklich etwas anderes als das Stilllegen: wer einen Platz
    /// stillgelegt, hat ihn nicht mehr, aber was auf ihm gespielt wurde, bleibt
    /// lesbar. Entfernen geht deshalb nur, solange keine Ansetzung darauf zeigt
    /// — das kann dieses Aggregat nicht wissen und prüft der Anwendungsfall.
    /// </summary>
    public void RemoveCourt(Guid courtId)
    {
        RequireNotFinished();
        _courts.Remove(CourtOf(courtId));
        Touch();
    }

    public TournamentCourt CourtOf(Guid courtId) =>
        _courts.FirstOrDefault(c => c.Id == courtId)
        ?? throw new DomainException($"Das Turnier hat keinen Platz mit der Id {courtId}.");

    /// <summary>
    /// Weist ein Turnier ab, für das keine Platzzeit erfasst ist.
    ///
    /// Bislang war das der Fall eines Vereins ohne Öffnungszeiten und damit
    /// selten. Jetzt ist es der Normalfall eines frisch angelegten Turniers, und
    /// ohne diese Prüfung säße der Veranstalter vor einem leeren Spielplan, in
    /// dem jedes Match als „nicht angesetzt" steht, ohne dass irgendwo stünde,
    /// warum.
    /// </summary>
    public void RequireCourtTimesRecorded()
    {
        if (!_courts.Any(court => court.IsActive && court.Windows.Count > 0))
        {
            throw new DomainException(
                "Für dieses Turnier ist keine Platzzeit erfasst. Ohne sie gibt es nichts, worin ein " +
                "Spielplan liegen könnte — zuerst Plätze anlegen und ihre Zeiten eintragen.");
        }
    }

    /// <summary>
    /// Weist eine Meldung ab, die nicht zur Ausschreibung passt: ein Einzelner
    /// im Doppel, ein Paar im Einzel.
    ///
    /// Bislang fiel ein Doppel ohne Partner erst beim Anlegen des Teilnehmers
    /// auf, und zwar ohne die Disziplin überhaupt zu kennen — das Turnier war,
    /// was der erste Melder daraus machte. Seit sich jemand selbst melden kann,
    /// muss die Ausschreibung das entscheiden.
    /// </summary>
    public void RequireMatchesDiscipline(bool hasPartner)
    {
        if (Discipline.NeedsPartner() && !hasPartner)
        {
            throw new DomainException(
                $"Dieses Turnier wird im {Discipline} gespielt. Eine Meldung braucht einen Partner.");
        }

        if (!Discipline.NeedsPartner() && hasPartner)
        {
            throw new DomainException(
                "Dieses Turnier wird im Einzel gespielt. Eine Meldung nennt genau eine Spielerin oder einen Spieler.");
        }
    }

    // --- Anmeldung --------------------------------------------------------

    public void ConfigureRegistration(int? capacity, DateTimeOffset? deadline)
    {
        RequireNotFinished();
        Registration = Registration.With(capacity, deadline);
        Touch();
    }

    /// <summary>Neues Token; das alte ist damit sofort wertlos.</summary>
    public void RotateRegistrationLink()
    {
        RequireNotFinished();
        Registration = Registration.Rotated();
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

    /// <summary>
    /// Zurück in den Planungsmodus. Anders als der Wechsel zum Turniertag setzt
    /// das keine Auslosung voraus — der Planungsmodus ist der Ausgangszustand.
    /// </summary>
    public void SwitchToPlanning()
    {
        RequireNotFinished();
        SchedulingMode = SchedulingMode.Planning;
        Touch();
    }

    // --- Meldungen --------------------------------------------------------

    public TournamentEntry Enter(
        Guid entryId,
        Guid participantId,
        int? seed = null,
        EntryOrigin origin = EntryOrigin.Organiser,
        DateTimeOffset? registeredAt = null)
    {
        RequireRegistrationOpen();

        if (_entries.Any(e => e.ParticipantId == participantId && e.Status != EntryStatus.Withdrawn))
        {
            throw new DomainException("Dieser Teilnehmer ist bereits gemeldet.");
        }

        // Auch beim Melden prüfen, nicht erst beim nachträglichen Setzen: sonst
        // schlägt der Konflikt erst bei der Auslosung zu, also nach Meldeschluss,
        // wenn er sich nur noch durch Nacharbeit an fremden Meldungen lösen lässt.
        //
        // Die Prüfung läuft vor dem Hinzufügen. Andernfalls bliebe die abgewiesene
        // Meldung im Aggregat zurück — der Aufrufer sähe einen Fehler und hätte
        // trotzdem eine halbe Änderung.
        RequireSeedIsFree(seed);

        var entry = new TournamentEntry(entryId, Id, participantId, seed, origin, registeredAt);
        _entries.Add(entry);
        Touch();

        return entry;
    }

    /// <summary>
    /// Die Meldung eines Teilnehmers, sofern sie nicht zurückgezogen wurde.
    ///
    /// Der Weg der Idempotenz: dieselbe Person, die ein zweites Mal auf
    /// „Absenden" drückt, bekommt keine zweite Meldung, sondern dieselbe samt
    /// ihrem Bestätigungscode zurück.
    /// </summary>
    public TournamentEntry? ActiveEntryOf(Guid participantId) =>
        _entries.FirstOrDefault(e => e.ParticipantId == participantId && e.Status != EntryStatus.Withdrawn);

    /// <summary>
    /// Die Zahl der Meldungen, die für die Kapazität zählen: alles, was nicht
    /// zurückgezogen und nicht schon auf der Warteliste ist. Die Warteliste
    /// zählt nicht mit — sie ist ja gerade das, was entsteht, wenn voll ist.
    /// </summary>
    public int CountAgainstCapacity() =>
        _entries.Count(e => e.Status is EntryStatus.Applied or EntryStatus.Accepted);

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

    private void RequireFreeCourtName(string name, Guid? exceptCourtId)
    {
        var candidate = (name ?? string.Empty).Trim();

        var taken = _courts.Any(c =>
            c.Id != exceptCourtId
            && string.Equals(c.Name, candidate, StringComparison.OrdinalIgnoreCase));

        if (taken)
        {
            throw new DomainException($"Das Turnier hat bereits einen Platz mit dem Namen „{candidate}“.");
        }
    }

    private void RequireSeedIsFree(int? seed)
    {
        if (seed is null)
        {
            return;
        }

        var taken = _entries.Any(e => e.Status != EntryStatus.Withdrawn && e.Seed == seed);
        if (taken)
        {
            throw new DomainException($"Die Setzposition {seed} ist bereits vergeben.");
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

    /// <summary>
    /// Hält fest, dass der Spielplan dieses Turniers geändert wurde.
    ///
    /// Die Zuweisungen sind ein eigenes Aggregat, ihr Zähler wirkt aber nur auf
    /// einer bestehenden Zeile. Ein Spielplan, der zum ersten Mal bestätigt wird,
    /// legt lauter neue an — zwei gleichzeitige Bestätigungen liefen beide durch
    /// und hinterließen jedes Match doppelt in der Platzbelegung. Der Zähler des
    /// Turniers ist die Klammer um den ganzen Plan und fängt genau das ab.
    /// </summary>
    public void MarkScheduleChanged() => Touch();

    /// <summary>
    /// Der Zeitraum des Turniers auf der absoluten Zeitachse, in der Zeitzone
    /// seines Ortes und mit einem Tag Luft nach jeder Seite.
    ///
    /// Die Luft ist kein Schlendrian: ein Abendspiel kann über Mitternacht
    /// gehen, und ein Ort westlich von UTC beginnt seinen ersten Turniertag
    /// nach UTC-Mitternacht. Vor allem aber steht die Rechnung damit an genau
    /// einer Stelle — sonst hielte die Spielplanprüfung eine Ansetzung für
    /// zulässig, die der Solver nie vorgeschlagen hätte, oder umgekehrt.
    /// </summary>
    public TimeSlot Period()
    {
        var local = new LocalTime(Venue.TimeZone);

        return new TimeSlot(local.Midnight(StartsOn.AddDays(-1)), local.Midnight(EndsOn.AddDays(2)));
    }

    /// <summary>
    /// Weist einen Zeitpunkt zurück, der nicht im Turnierzeitraum liegt.
    ///
    /// Ein Tag Luft nach jeder Seite: ein Abendspiel kann über Mitternacht
    /// gehen, und die Zeitzone des Vereins verschiebt die Grenzen. Ohne diese
    /// Schranke wandert ein vertippter Termin unbemerkt ins Jahr 2099 — und
    /// steht dort öffentlich, weil die Warteschlange alles dahinter mitzieht.
    /// </summary>
    public void RequireScheduledWithin(DateTimeOffset when)
    {
        var from = new DateTimeOffset(StartsOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var until = new DateTimeOffset(EndsOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2);

        if (when < from || when >= until)
        {
            throw new DomainException(
                $"Der Zeitpunkt {when:g} liegt außerhalb des Turnierzeitraums " +
                $"({StartsOn:d} bis {EndsOn:d}).");
        }
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
