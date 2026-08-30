using TennisTurnier.Domain.Social;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Social;

/// <summary>
/// Eine Verabredung, wie die Oberfläche sie bekommt (ADR-0015).
/// </summary>
/// <param name="Missing">
/// Wie viele noch fehlen. Null heißt: die Runde steht. Es ist die Zahl, wegen
/// der jemand die Liste überhaupt ansieht.
/// </param>
/// <param name="IsHost">
/// Richtet der Aufrufer aus? Nur dann darf er absagen und weitere einladen.
/// </param>
/// <param name="MyResponse">
/// Die eigene Antwort — leer beim Gastgeber, der nicht sich selbst zusagt.
/// </param>
/// <param name="IsPast">
/// Vorbei. Aus der Uhr gerechnet und nicht gespeichert: ein Zustand, der von
/// selbst eintritt, will nicht gepflegt werden.
/// </param>
public sealed record PlayDateView(
    Guid Id,
    string Title,
    Discipline Discipline,
    string VenueName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Note,
    PlayDateAuthorView Host,
    IReadOnlyList<PlayDateGuestView> Guests,
    int RequiredPlayers,
    int Committed,
    int Missing,
    bool IsConfirmed,
    bool IsCancelled,
    bool IsPast,
    bool IsHost,
    InvitationResponse? MyResponse);

public sealed record PlayDateAuthorView(Guid UserId, string DisplayName, Guid? PlayerId);

public sealed record PlayDateGuestView(
    Guid UserId,
    Guid PlayerId,
    string DisplayName,
    InvitationResponse Response);

/// <summary>
/// Eine neue Verabredung.
/// </summary>
/// <param name="Invitees">
/// Spieler aus dem Kontaktgraphen. Wer kein Konto hat, wird abgewiesen und
/// nicht still übergangen — er könnte weder zusagen noch die Einladung sehen
/// (ADR-0015).
/// </param>
/// <param name="DurationMinutes">
/// Wie lange. In Minuten und nicht als Zeitspanne: das Formular fragt „60" oder
/// „90", und eine ISO-8601-Dauer im JSON wäre ein Umweg über ein Format, das
/// hier niemand liest.
/// </param>
public sealed record CreatePlayDateRequest(
    string Title,
    Discipline Discipline,
    string VenueName,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string? Note,
    IReadOnlyList<Guid> Invitees);

public sealed record InviteToPlayDateRequest(IReadOnlyList<Guid> Invitees);

public sealed record RespondToPlayDateRequest(bool Accepted);
