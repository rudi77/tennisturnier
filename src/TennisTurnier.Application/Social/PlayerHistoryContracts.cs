using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Social;

/// <summary>
/// Ein entschiedenes Match aus der Sicht eines bestimmten Spielers.
///
/// „Eigene Seite" und „Gegenseite" statt „Seite 1" und „Seite 2": wer das
/// Profil eines Spielers ansieht, will nicht wissen, auf welcher Hälfte des
/// Baums er stand.
/// </summary>
/// <param name="PlayedAt">
/// Wann tatsächlich gespielt wurde, sofern das Match über einen Platz lief.
/// Leer bei einem Ergebnis, das ohne Platzzuweisung eingetragen wurde — der
/// Normalfall bei einem Turnier, das seine Plätze nicht verwaltet.
/// </param>
/// <param name="Partner">
/// Der Doppelpartner. Leer im Einzel — und das ist die einzige Stelle, an der
/// ein Profil den Unterschied überhaupt bemerkt.
/// </param>
public sealed record PlayedMatch(
    Guid MatchId,
    Guid TournamentId,
    string TournamentName,
    Discipline Discipline,
    DateOnly? TournamentStartsOn,
    string PhaseName,
    string MatchName,
    Guid OwnEntryId,
    string OwnDisplayName,
    Guid? Partner,
    Guid OpponentEntryId,
    string OpponentDisplayName,
    IReadOnlyList<Guid> OpponentPlayerIds,
    bool Won,
    MatchOutcome Outcome,
    IReadOnlyList<SetScore> Sets,
    int SetsWon,
    int SetsLost,
    DateTimeOffset? PlayedAt);

/// <summary>
/// Eine Meldung dieses Spielers, ohne Rücksicht darauf, ob schon gespielt
/// wurde.
/// </summary>
public sealed record PlayerEntry(
    Guid TournamentId,
    string TournamentName,
    Discipline Discipline,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    TournamentState State,
    EntryStatus Status,
    string ParticipantDisplayName);
