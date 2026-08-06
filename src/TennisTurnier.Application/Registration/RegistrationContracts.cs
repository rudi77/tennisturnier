using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Registration;

/// <summary>
/// Was ein Melder ohne Konto zu sehen bekommt — absichtlich karg.
///
/// Turniername, Ort, Zeitraum, Disziplin, offen ja/nein, freie Plätze. Keine
/// Teilnehmerliste, keine Namen, keine Kontaktdaten: sonst wäre der Anmeldelink
/// ein Weg an der öffentlichen Projektion vorbei, die genau festlegt, was ohne
/// Anmeldung sichtbar sein darf (ADR-0003).
/// </summary>
/// <param name="FreeSlots">
/// Leer heißt: unbegrenzt. Null heißt: das Feld ist voll — gemeldet werden kann
/// weiterhin, die Meldung landet dann auf der Warteliste.
/// </param>
public sealed record PublicRegistrationView(
    string TournamentName,
    string VenueName,
    string? City,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    Discipline Discipline,
    bool NeedsPartner,
    bool IsOpen,
    int? FreeSlots,
    DateTimeOffset? Deadline);

/// <summary>
/// Eine Selbstmeldung.
///
/// Geburtsdatum wird nicht erhoben — es wird für nichts gebraucht, und was
/// nicht erhoben wird, muss weder geschützt noch gelöscht werden. Die
/// Partnerangaben sind im Einzel leer und im Doppel Pflicht; welche der beiden
/// Ausschreibungen gilt, sagt die Ansicht vorher.
/// </summary>
public sealed record SelfRegistrationRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? PartnerFirstName,
    string? PartnerLastName,
    string? PartnerEmail,
    string? TeamName);

/// <param name="ConfirmationCode">
/// Der Weg zurück. Ohne ihn hätte ein Melder ohne Konto keinen — weder um
/// nachzusehen, ob er angenommen wurde, noch um sich zurückzuziehen.
/// </param>
public sealed record SelfRegistrationResult(string ConfirmationCode, EntryStatus Status);
