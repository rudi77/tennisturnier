using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

// FormatDefinition und FormatSnapshot gehen unverändert über die Schnittstelle.
// Das ist die eine bewusste Ausnahme von der Regel, keine Domänentypen nach
// außen zu reichen: die Definition IST das Austauschformat aus ADR-0001, und
// eine deckungsgleiche Kopie als DTO wäre eine zweite Wahrheit, die auseinander
// läuft. Beide sind unveränderliche Wertobjekte ohne Verhalten, keine Entitäten.

public sealed record CreateTournamentRequest(
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn,
    Guid FormatTemplateId);

public sealed record UpdateTournamentRequest(string Name, DateOnly StartsOn, DateOnly EndsOn);

public sealed record TournamentSummary(
    Guid Id,
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn,
    TournamentState State,
    SchedulingMode SchedulingMode,
    int AcceptedEntries);

public sealed record TournamentDetail(
    Guid Id,
    Guid ClubId,
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn,
    TournamentState State,
    SchedulingMode SchedulingMode,
    Guid FormatTemplateId,
    FormatSnapshot? Format,
    IReadOnlyList<EntryDetail> Entries,
    int Version);

public sealed record EntryDetail(
    Guid Id,
    Guid ParticipantId,
    string ParticipantName,
    int? Seed,
    EntryStatus Status);

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
