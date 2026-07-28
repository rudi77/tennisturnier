using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tournaments;

/// <summary>Die Meldung eines Teilnehmers zu einem Turnier.</summary>
public sealed class TournamentEntry : Entity
{
    internal TournamentEntry(Guid id, Guid tournamentId, Guid participantId, int? seed)
        : base(id)
    {
        if (tournamentId == Guid.Empty || participantId == Guid.Empty)
        {
            throw new DomainException("Eine Meldung braucht Turnier und Teilnehmer.");
        }

        TournamentId = tournamentId;
        ParticipantId = participantId;
        Status = EntryStatus.Applied;
        SetSeed(seed);
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private TournamentEntry(Guid id) : base(id)
    {
    }

    public Guid TournamentId { get; private set; }

    public Guid ParticipantId { get; private set; }

    /// <summary>
    /// Setzposition, 1 für den Topgesetzten. Leer, wenn ungesetzt — die weitaus
    /// häufigere Angabe, denn gesetzt wird nur ein kleiner Teil des Feldes.
    /// </summary>
    public int? Seed { get; private set; }

    public EntryStatus Status { get; private set; }

    public bool IsInDraw => Status == EntryStatus.Accepted;

    public void SetSeed(int? seed)
    {
        if (seed is < 1)
        {
            throw new DomainException($"Eine Setzposition beginnt bei 1, war {seed}.");
        }

        Seed = seed;
    }

    public void Accept() => Status = EntryStatus.Accepted;

    public void MoveToWaitingList() => Status = EntryStatus.WaitingList;

    /// <summary>
    /// Rückzug. Die Setzposition entfällt dabei, sonst hinterließe ein
    /// zurückgezogener Gesetzter eine Lücke in der Setzliste.
    /// </summary>
    public void Withdraw()
    {
        Status = EntryStatus.Withdrawn;
        Seed = null;
    }
}
