using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Membership;

/// <summary>
/// Was jemand sieht, der einem Beitrittslink folgt — absichtlich karg.
///
/// Turniername, Ort, Zeitraum, Disziplin, offen ja/nein, freie Plätze. Keine
/// Teilnehmerliste, keine Namen, keine Kontaktdaten: sonst wäre der Link ein
/// Weg an der öffentlichen Projektion vorbei, die genau festlegt, was außerhalb
/// des Turniers sichtbar sein darf (ADR-0003). Dass der Aufrufer angemeldet
/// ist, ändert daran nichts — angemeldet ist noch nicht dabei.
/// </summary>
/// <param name="FreeSlots">
/// Leer heißt: unbegrenzt. Null heißt: das Feld ist voll — gemeldet werden kann
/// weiterhin, die Meldung landet dann auf der Warteliste.
/// </param>
/// <param name="AlreadyMember">
/// Wer schon dabei ist, soll das erfahren, statt ein zweites Mal beizutreten.
/// </param>
public sealed record JoinView(
    Guid TournamentId,
    string TournamentName,
    string VenueName,
    string? City,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    Discipline Discipline,
    bool NeedsPartner,
    bool IsOpen,
    int? FreeSlots,
    DateTimeOffset? Deadline,
    bool AlreadyMember);

/// <summary>
/// Ein Beitritt.
///
/// Die E-Mail-Adresse fehlt hier, und das ist der Unterschied zur früheren
/// Selbstmeldung: sie kommt aus dem Konto und nicht aus dem Formular. Wer sich
/// unter fremder Adresse melden wollte, müsste sich zuerst unter ihr anmelden.
///
/// Geburtsdatum wird nicht erhoben — es wird für nichts gebraucht, und was
/// nicht erhoben wird, muss weder geschützt noch gelöscht werden.
/// </summary>
/// <param name="Play">
/// Mitspielen oder nur zusehen. Wer beitritt, ohne zu melden, gehört trotzdem
/// dazu — genau dafür ist ein Turnier eine Gruppe: der Partner ohne Meldung,
/// der Vereinskollege, der nur den Spielplan sehen will.
/// </param>
public sealed record JoinRequest(
    bool Play,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? PartnerFirstName,
    string? PartnerLastName,
    string? PartnerEmail,
    string? TeamName);

/// <param name="EntryId">
/// Leer, wenn jemand beigetreten ist, ohne zu melden — oder wenn die Meldung
/// bereits zu ist.
/// </param>
public sealed record JoinResult(Guid TournamentId, Guid? EntryId, EntryStatus? Status);
