using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Matches;

public enum MatchStatus
{
    /// <summary>Mindestens eine Seite steht noch nicht fest.</summary>
    Pending,

    /// <summary>Beide Seiten stehen fest, das Match kann angesetzt werden.</summary>
    Ready,

    /// <summary>Ergebnis eingetragen.</summary>
    Finished,
}

/// <summary>
/// Eine Seite eines Matches: woher der Teilnehmer kommt und — sobald bekannt —
/// wer es ist.
///
/// Die Trennung ist wesentlich. Würde die aufgelöste Meldung die strukturelle
/// Referenz überschreiben, ginge die Herkunft verloren: nach einer Korrektur
/// wüsste niemand mehr, dass diese Seite „Sieger aus Match 7" war, und die
/// Rücknahme eines Ergebnisses könnte den falschen Namen stehen lassen.
/// </summary>
public sealed record MatchSide(ParticipantRef Origin, Guid? EntryId)
{
    /// <summary>Eine Seite, die von Beginn an feststeht.</summary>
    public static MatchSide From(ParticipantRef origin) => origin switch
    {
        ParticipantRef.Entry entry => new MatchSide(origin, entry.EntryId),
        _ => new MatchSide(origin, null),
    };

    public bool IsBye => Origin is ParticipantRef.Bye;

    /// <summary>Steht fest, wer hier spielt — oder dass niemand spielt?</summary>
    public bool IsSettled => EntryId is not null || IsBye;

    public MatchSide ResolvedTo(Guid entryId) => this with { EntryId = entryId };

    public MatchSide Cleared() => this with { EntryId = null };

    public override string ToString() =>
        EntryId is { } id && Origin is not ParticipantRef.Entry
            ? $"{Origin} → {id}"
            : Origin.ToString();
}

/// <summary>
/// Eine Begegnung im Turnierbaum.
///
/// Ein Match existiert, sobald der Draw steht — auch wenn beide Seiten noch
/// „Sieger aus …" sind. Das ist der Grund, warum <see cref="ParticipantRef"/>
/// ein Summentyp ist: erst dadurch lässt sich der ganze Baum aufbauen, bevor
/// ein einziger Ball gespielt wurde.
/// </summary>
public sealed class Match : Entity
{
    internal Match(
        Guid id,
        Guid tournamentId,
        Guid phaseId,
        int round,
        int position,
        ParticipantRef side1,
        ParticipantRef side2,
        string? label = null,
        string? group = null)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(side1);
        ArgumentNullException.ThrowIfNull(side2);

        if (tournamentId == Guid.Empty || phaseId == Guid.Empty)
        {
            throw new DomainException("Ein Match braucht Turnier und Phase.");
        }

        if (round < 1)
        {
            throw new DomainException($"Eine Runde beginnt bei 1, war {round}.");
        }

        if (position < 1)
        {
            throw new DomainException($"Eine Position beginnt bei 1, war {position}.");
        }

        TournamentId = tournamentId;
        PhaseId = phaseId;
        Round = round;
        Position = position;
        Side1 = MatchSide.From(side1);
        Side2 = MatchSide.From(side2);
        Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        Group = string.IsNullOrWhiteSpace(group) ? null : group.Trim();
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private Match(Guid id) : base(id)
    {
        Side1 = MatchSide.From(ParticipantRef.Open);
        Side2 = MatchSide.From(ParticipantRef.Open);
    }

    public Guid TournamentId { get; private set; }

    public Guid PhaseId { get; private set; }

    /// <summary>Runde innerhalb der Phase, 1 für die erste.</summary>
    public int Round { get; private set; }

    /// <summary>Position innerhalb der Runde, 1 für die oberste.</summary>
    public int Position { get; private set; }

    /// <summary>Sprechende Bezeichnung wie „Finale" oder „Spiel um Platz 3".</summary>
    public string? Label { get; private set; }

    /// <summary>
    /// Wie das Match genannt wird, wo ein Name gebraucht wird — im Feed, in
    /// einer Historie.
    ///
    /// Die mitgelieferten Formate vergeben alle eine Bezeichnung; eine eigene
    /// Formatdefinition muss das nicht. Der Ausweg steht deshalb hier und
    /// nicht an jeder Aufrufstelle, die ihn sonst für sich fände — und die
    /// Runde ist das, was ohne Bezeichnung übrig bleibt.
    /// </summary>
    public string Name => Label ?? $"Runde {Round}";

    /// <summary>Gruppe in einer Round-Robin-Phase.</summary>
    public string? Group { get; private set; }

    public MatchSide Side1 { get; private set; }

    public MatchSide Side2 { get; private set; }

    public Score? Score { get; private set; }

    public MatchStatus Status =>
        Score is not null ? MatchStatus.Finished
        : Side1.IsSettled && Side2.IsSettled ? MatchStatus.Ready
        : MatchStatus.Pending;

    /// <summary>
    /// Zähler für optimistische Nebenläufigkeit. Zwei Schiedsrichter, die
    /// dasselbe Ergebnis eintragen, sind am Turniertag der Normalfall.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Die siegreiche Meldung, sofern das Match entschieden ist.</summary>
    public Guid? WinnerEntryId => Score is null ? null : Side(Score.WinnerSide).EntryId;

    public Guid? LoserEntryId => Score is null ? null : Side(Score.LoserSide).EntryId;

    /// <summary>
    /// Ein Match, in dem eine Seite ein Freilos ist. Es wird nie gespielt,
    /// sondern beim Aufbau des Baums entschieden.
    /// </summary>
    public bool HasBye => Side1.IsBye || Side2.IsBye;

    /// <summary>
    /// Hat hier jemand ein Ergebnis eingetragen?
    ///
    /// Nicht dasselbe wie „hat einen Spielstand": ein Freilos trägt auch einen,
    /// aber niemand hat dafür einen Ball geschlagen. Der Unterschied ist überall
    /// dort entscheidend, wo etwas zurückgenommen werden soll — ein Freilos
    /// steht keiner Korrektur im Weg, ein gespieltes Match sehr wohl.
    ///
    /// Es war dieselbe Frage, an der eine Korrektur im Schweizer System mit
    /// ungerader Teilnehmerzahl unmöglich wurde: die Folgerunde enthielt ein
    /// Freilos, das als gespielt zählte, und ließ sich deshalb nicht mehr
    /// zurücknehmen — sein Ergebnis aber auch nicht.
    /// </summary>
    public bool IsPlayed => Score is not null && Score.Outcome != MatchOutcome.Bye;

    public MatchSide Side(int side) => side == 1 ? Side1 : Side2;

    /// <summary>
    /// Legt fest, woher eine Seite kommt. Nur beim Aufbau des Baums, solange
    /// noch niemand feststeht.
    /// </summary>
    internal void SetOrigin(int side, ParticipantRef origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        if (Score is not null)
        {
            throw new DomainException("Ein entschiedenes Match lässt sich nicht mehr umbauen.");
        }

        Assign(side, MatchSide.From(origin));
    }

    /// <summary>
    /// Trägt die Meldung ein, die eine Referenz aufgelöst hat. Meldet, ob sich
    /// etwas geändert hat.
    /// </summary>
    internal bool Resolve(int side, Guid entryId)
    {
        if (entryId == Guid.Empty)
        {
            throw new DomainException("Eine aufgelöste Seite braucht eine Meldung.");
        }

        if (Score is not null)
        {
            throw new DomainException(
                "Ein bereits entschiedenes Match lässt sich nicht mehr umbesetzen. " +
                "Dafür ist zuerst das Ergebnis zurückzunehmen.");
        }

        var current = Side(side);
        if (current.EntryId == entryId)
        {
            return false;
        }

        Assign(side, current.ResolvedTo(entryId));
        Touch();
        return true;
    }

    /// <summary>
    /// Nimmt die Auflösung einer Seite zurück. Die Herkunft bleibt bestehen —
    /// „Sieger aus Match 7" ist weiterhin die Regel, nur ist der Sieger wieder
    /// offen.
    /// </summary>
    internal bool Unresolve(int side)
    {
        var current = Side(side);
        if (current.EntryId is null || current.Origin is ParticipantRef.Entry)
        {
            return false;
        }

        Assign(side, current.Cleared());
        Touch();
        return true;
    }

    public void RecordResult(Score score)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (score.Outcome == MatchOutcome.Bye)
        {
            RequireByeIsPlausible(score);
        }
        else if (Status == MatchStatus.Pending)
        {
            throw new DomainException(
                "Für ein Match, dessen Teilnehmer noch nicht feststehen, lässt sich kein Ergebnis eintragen.");
        }
        else if (HasBye)
        {
            throw new DomainException(
                "Ein Match mit einem Freilos wird nicht gespielt und bekommt kein reguläres Ergebnis.");
        }

        Score = score;
        Touch();
    }

    /// <summary>
    /// Nimmt das Ergebnis zurück.
    ///
    /// Ob die Folgematches das noch erlauben, entscheidet die Phase — hier fehlt
    /// der Überblick darüber.
    /// </summary>
    public void ClearResult()
    {
        if (Score is null)
        {
            return;
        }

        Score = null;
        Touch();
    }

    private void Assign(int side, MatchSide value)
    {
        if (side == 1)
        {
            Side1 = value;
        }
        else if (side == 2)
        {
            Side2 = value;
        }
        else
        {
            throw new DomainException($"Eine Seite ist 1 oder 2, war {side}.");
        }
    }

    private void RequireByeIsPlausible(Score score)
    {
        if (!HasBye)
        {
            throw new DomainException("Ein Freilos setzt voraus, dass eine Seite frei ist.");
        }

        if (Side(score.WinnerSide).IsBye)
        {
            throw new DomainException("Ein Freilos kann nicht gewinnen.");
        }
    }

    private void Touch() => Version++;

    public override string ToString() =>
        $"{Label ?? $"R{Round}/{Position}"}: {Side1} – {Side2}" + (Score is null ? string.Empty : $" ({Score})");
}
