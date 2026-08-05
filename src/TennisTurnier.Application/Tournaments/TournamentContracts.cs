using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

// FormatDefinition und FormatSnapshot gehen unverändert über die Schnittstelle.
// Das ist die eine bewusste Ausnahme von der Regel, keine Domänentypen nach
// außen zu reichen: die Definition IST das Austauschformat aus ADR-0001, und
// eine deckungsgleiche Kopie als DTO wäre eine zweite Wahrheit, die auseinander
// läuft. Beide sind unveränderliche Wertobjekte ohne Verhalten, keine Entitäten.

/// <param name="VenueName">Die Anlage, auf der gespielt wird — „TC Maria Alm".</param>
/// <param name="TimeZoneId">
/// IANA-Zeitzone der Anlage. Ohne sie ist keine Platzzeit auf die Zeitachse
/// abzubilden; sie steht deshalb im Anlegen und nicht in einer späteren
/// Einstellung.
/// </param>
public sealed record CreateTournamentRequest(
    string Name,
    string VenueName,
    string? VenueAddress,
    string? VenueCity,
    string TimeZoneId,
    Discipline Discipline,
    DateOnly StartsOn,
    DateOnly EndsOn,
    Guid FormatTemplateId);

public sealed record UpdateTournamentRequest(
    string Name,
    string VenueName,
    string? VenueAddress,
    string? VenueCity,
    string TimeZoneId,
    Discipline Discipline,
    DateOnly StartsOn,
    DateOnly EndsOn);

// --- Plätze -----------------------------------------------------------------

public sealed record CreateCourtRequest(
    string Name,
    CourtSurface Surface,
    CourtLocation Location,
    bool IsCenterCourt = false);

public sealed record UpdateCourtRequest(string Name, bool IsCenterCourt, bool IsActive);

public sealed record CourtDetail(
    Guid Id,
    string Name,
    CourtSurface Surface,
    CourtLocation Location,
    bool IsCenterCourt,
    bool IsActive,
    IReadOnlyList<CourtWindowDetail> Windows);

public sealed record CourtWindowDetail(Guid Id, DateTimeOffset From, DateTimeOffset To);

/// <summary>Eine einzelne Platzzeit, als absolutes Fenster.</summary>
public sealed record CreateCourtWindowRequest(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// Die Massenanlage: dieselbe Uhrzeitspanne an jedem Turniertag, für die
/// genannten Plätze — oder für alle, wenn keiner genannt ist.
///
/// Genau das, was der Veranstalter am Telefon vereinbart hat („beide Plätze,
/// Samstag und Sonntag, acht bis zweiundzwanzig Uhr"). Die Uhrzeiten sind lokal
/// zu verstehen, in der Zeitzone der Anlage.
/// </summary>
public sealed record CreateCourtWindowsRequest(
    TimeOnly From,
    TimeOnly To,
    IReadOnlyList<Guid>? CourtIds = null);

public sealed record TournamentSummary(
    Guid Id,
    string Name,
    string VenueName,
    Discipline Discipline,
    DateOnly StartsOn,
    DateOnly EndsOn,
    TournamentState State,
    SchedulingMode SchedulingMode,
    int AcceptedEntries);

public sealed record TournamentDetail(
    Guid Id,
    string Name,
    VenueDetail Venue,
    Discipline Discipline,
    DateOnly StartsOn,
    DateOnly EndsOn,
    TournamentState State,
    SchedulingMode SchedulingMode,
    Guid FormatTemplateId,
    FormatSnapshot? Format,
    IReadOnlyList<CourtDetail> Courts,
    IReadOnlyList<EntryDetail> Entries,
    int Version);

public sealed record VenueDetail(string Name, string? Address, string? City, string TimeZoneId);

/// <summary>
/// Der Anmeldelink samt seinen Bedingungen. Nur für die Turnierleitung — das
/// Token ist der Schlüssel zum Melden und gehört nicht in eine öffentliche
/// Antwort.
/// </summary>
public sealed record RegistrationDetail(
    string Token,
    int? Capacity,
    DateTimeOffset? Deadline,
    int Applied,
    int Accepted,
    int WaitingList);

public sealed record ConfigureRegistrationRequest(int? Capacity, DateTimeOffset? Deadline);

public sealed record EntryDetail(
    Guid Id,
    Guid ParticipantId,
    string ParticipantName,
    int? Seed,
    EntryStatus Status);

/// <summary>
/// Eine Meldung in der Meldungsverwaltung — mit allem, was die Turnierleitung
/// braucht, um über sie zu entscheiden.
/// </summary>
/// <param name="Contacts">
/// Leer ohne <c>ViewInternals</c>. Kontaktdaten sind der Teil, der niemals
/// öffentlich wird (ADR-0003/0008); wer nur Ergebnisse einträgt, braucht sie
/// nicht.
/// </param>
/// <param name="ConfirmationCode">
/// Ebenfalls nur mit <c>ViewInternals</c>. Er ist der Weg eines Melders ohne
/// Konto zu seiner Meldung — die Turnierleitung braucht ihn, wenn jemand anruft
/// und ihn verlegt hat.
/// </param>
public sealed record EntryOverview(
    Guid Id,
    Guid ParticipantId,
    string ParticipantName,
    int? Seed,
    EntryStatus Status,
    EntryOrigin Origin,
    DateTimeOffset RegisteredAt,
    string? ConfirmationCode,
    IReadOnlyList<EntryContact> Contacts);

public sealed record EntryContact(Guid PlayerId, string DisplayName, string? Email, string? Phone);

/// <summary>
/// Meldung eines bestehenden Teilnehmers. Der Teilnehmer — Einzelspieler oder
/// Doppel — wird zuvor über die Spielerverwaltung angelegt.
/// </summary>
public sealed record EnterTournamentRequest(Guid ParticipantId, int? Seed);

public sealed record SetSeedRequest(int? Seed);

// --- Spieler und Teilnehmer -------------------------------------------------

public sealed record CreatePlayerRequest(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    DateOnly? DateOfBirth);

/// <summary>
/// Ein Spieler in der Suche. Enthält bewusst keine Kontaktdaten — die Suche
/// steht jedem offen, der ein Turnier verwaltet, und dient nur dem Auffinden.
/// </summary>
public sealed record PlayerSummary(Guid Id, string DisplayName);

public sealed record PlayerDetail(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    DateOnly? DateOfBirth);

/// <summary>
/// Ein Teilnehmer entsteht aus einem Spieler (Einzel) oder zweien (Doppel).
/// </summary>
/// <summary>
/// Ein Teilnehmer: ein Spieler im Einzel, zwei im Doppel.
/// </summary>
/// <param name="TeamName">
/// Der Name, unter dem ein Doppel antritt — „Die Netzroller". Er ersetzt die
/// Spielernamen nicht, sondern steht ihnen voran: im Spielplan muss ablesbar
/// bleiben, wer tatsächlich auf dem Platz steht. Im Einzel abgewiesen.
/// </param>
public sealed record CreateParticipantRequest(
    Guid FirstPlayerId,
    Guid? SecondPlayerId,
    string? TeamName = null);

public sealed record ParticipantSummary(Guid Id, string DisplayName, IReadOnlyList<Guid> PlayerIds);

// --- Formatvorlagen ---------------------------------------------------------

public sealed record FormatTemplateSummary(
    Guid Id,
    string Name,
    int Version,
    bool IsBuiltIn,
    IReadOnlyList<string> Phases);

public sealed record FormatTemplateDetail(
    Guid Id,
    string Name,
    int Version,
    bool IsBuiltIn,
    FormatDefinition Definition);

public sealed record SaveFormatTemplateRequest(FormatDefinition Definition);

public sealed record CopyFormatTemplateRequest(string Name);
