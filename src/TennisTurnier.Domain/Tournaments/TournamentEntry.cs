using System.Buffers.Text;
using System.Security.Cryptography;
using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tournaments;

/// <summary>
/// Woher eine Meldung stammt.
///
/// Nicht bloß Statistik: eine Selbstmeldung kommt von einem Menschen ohne Konto,
/// der über den Bestätigungscode zurückfindet und dessen Kontaktdaten unter die
/// Aufbewahrungsfrist fallen. Eine von der Turnierleitung erfasste Meldung tut
/// beides nicht.
/// </summary>
public enum EntryOrigin
{
    /// <summary>Die Turnierleitung hat sie erfasst.</summary>
    Organiser,

    /// <summary>Jemand hat sich über den Anmeldelink selbst gemeldet.</summary>
    SelfService,
}

/// <summary>Die Meldung eines Teilnehmers zu einem Turnier.</summary>
public sealed class TournamentEntry : Entity
{
    /// <summary>
    /// 48 Bit als Base64Url — acht Zeichen. Kurz genug, um ihn am Telefon
    /// durchzugeben, und nicht zu erraten. Er ist kein Geheimnis, das etwas
    /// schützt, sondern der Weg eines Melders ohne Konto zurück zu seiner
    /// Meldung; ohne ihn hätte er gar keinen.
    /// </summary>
    private const int ConfirmationBytes = 6;

    internal TournamentEntry(
        Guid id,
        Guid tournamentId,
        Guid participantId,
        int? seed,
        EntryOrigin origin = EntryOrigin.Organiser,
        DateTimeOffset? registeredAt = null)
        : base(id)
    {
        if (tournamentId == Guid.Empty || participantId == Guid.Empty)
        {
            throw new DomainException("Eine Meldung braucht Turnier und Teilnehmer.");
        }

        TournamentId = tournamentId;
        ParticipantId = participantId;
        Status = EntryStatus.Applied;
        Origin = origin;
        RegisteredAt = registeredAt ?? DateTimeOffset.UtcNow;
        ConfirmationCode = NewConfirmationCode();
        SetSeed(seed);
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private TournamentEntry(Guid id) : base(id) => ConfirmationCode = string.Empty;

    public Guid TournamentId { get; private set; }

    public Guid ParticipantId { get; private set; }

    /// <summary>
    /// Setzposition, 1 für den Topgesetzten. Leer, wenn ungesetzt — die weitaus
    /// häufigere Angabe, denn gesetzt wird nur ein kleiner Teil des Feldes.
    /// </summary>
    public int? Seed { get; private set; }

    public EntryStatus Status { get; private set; }

    public EntryOrigin Origin { get; private set; }

    /// <summary>
    /// Wann gemeldet wurde. Bei erschöpfter Kapazität ist die Reihenfolge der
    /// Meldungen die einzige nachvollziehbare Begründung dafür, wer nachrückt.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; private set; }

    /// <summary>
    /// Der Code, mit dem ein Melder ohne Konto seine Meldung wiederfindet und
    /// zurückziehen kann. Er entsteht für jede Meldung, auch für die von der
    /// Turnierleitung erfasste — sonst gäbe es zwei Sorten Meldung, von denen
    /// nur eine auskunftsfähig wäre.
    /// </summary>
    public string ConfirmationCode { get; private set; }

    /// <summary>
    /// Die Meldung des Teams, in dem diese Meldung spielt — leer, solange sie
    /// für sich steht. Nur im Doppel besetzt, dessen Teams die Turnierleitung
    /// bildet (<see cref="TeamFormation.ByOrganiser"/>).
    /// </summary>
    public Guid? TeamEntryId { get; private set; }

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

    /// <summary>
    /// Ordnet diese Meldung einem Team zu.
    ///
    /// Die Setzposition entfällt wie beim Rückzug: gesetzt wird, wer im Draw
    /// steht, und das ist ab jetzt das Team. Eine stehengebliebene Einzelsetzung
    /// belegte eine Position, die niemand einnimmt.
    /// </summary>
    internal void PairInto(Guid teamEntryId)
    {
        Status = EntryStatus.Paired;
        TeamEntryId = teamEntryId;
        Seed = null;
    }

    /// <summary>
    /// Löst die Zuordnung. Die Meldung steht danach wieder für sich im Feld —
    /// nicht auf der Warteliste: sie war angenommen, bevor sie ein Team bekam.
    /// </summary>
    internal void Unpair()
    {
        Status = EntryStatus.Accepted;
        TeamEntryId = null;
    }

    private static string NewConfirmationCode() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ConfirmationBytes));
}
