using TennisTurnier.Application.Ports;

namespace TennisTurnier.Application.Social;

public interface IConnectionService
{
    /// <summary>
    /// Die Spieler, mit denen der Aufrufer gespielt hat — als Partner oder als
    /// Gegner. Leer, solange zu seinem Konto kein Spieler gehört oder er noch
    /// nichts gespielt hat.
    /// </summary>
    Task<IReadOnlyList<ConnectionView>> ListMineAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Der Kontaktgraph (ADR-0013).
///
/// Er wird über dieselbe Rechnung gebildet wie das Profil und erbt damit dessen
/// Sichtbarkeitsregel: gezählt wird über die Turniere, die der Aufrufer sehen
/// darf. Das ist hier weniger eine Einschränkung als eine Selbstverständlichkeit
/// — mit wem man gespielt hat, weiß man ohnehin.
/// </summary>
public sealed class ConnectionService : IConnectionService
{
    private readonly IPlayerHistoryStore _history;
    private readonly IUserContext _userContext;

    public ConnectionService(IPlayerHistoryStore history, IUserContext userContext)
    {
        _history = history;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<ConnectionView>> ListMineAsync(
        CancellationToken cancellationToken = default)
    {
        var user = _userContext.Current;

        if (!user.IsAuthenticated)
        {
            return [];
        }

        if (await _history.FindPlayerIdOfAccountAsync(user.UserId, cancellationToken) is not { } me)
        {
            return [];
        }

        var matches = await _history.ListForPlayerAsync(me, cancellationToken);

        if (matches.Count == 0)
        {
            return [];
        }

        var tallies = new Dictionary<Guid, Tally>();

        foreach (var match in matches)
        {
            // Der Partner steht auf derselben Seite — er wird gezählt, aber
            // nicht als Gegner. Ein Doppel bringt damit drei Verbindungen mit
            // einem Match, und genau das ist es, was ein Doppel tut.
            if (match.Partner is { } partner)
            {
                Of(tallies, partner).AddTogether(match);
            }

            foreach (var opponent in match.OpponentPlayerIds)
            {
                Of(tallies, opponent).AddAgainst(match);
            }
        }

        var names = await _history.DisplayNamesAsync([.. tallies.Keys], cancellationToken);

        return [.. tallies
            .Select(pair => pair.Value.ToView(pair.Key, names.GetValueOrDefault(pair.Key, "Unbekannt")))
            // Wer zuletzt mitgespielt hat, steht oben: das ist die Reihenfolge,
            // in der man jemanden sucht. Die Zahl der Matches entscheidet nur
            // dort, wo kein Datum vorliegt.
            .OrderByDescending(view => view.LastPlayedOn ?? DateOnly.MinValue)
            .ThenByDescending(view => view.Together + view.Against)
            .ThenBy(view => view.DisplayName)];
    }

    private static Tally Of(Dictionary<Guid, Tally> tallies, Guid playerId)
    {
        if (!tallies.TryGetValue(playerId, out var tally))
        {
            tally = new Tally();
            tallies[playerId] = tally;
        }

        return tally;
    }

    /// <summary>
    /// Die laufende Zählung zu einem Mitspieler. Veränderlich und nicht als
    /// Datensatz: sie wird je Match fortgeschrieben, und ein Datensatz je
    /// Schritt hieße, für jedes Match eine Kopie anzulegen.
    /// </summary>
    private sealed class Tally
    {
        private readonly HashSet<Guid> _tournaments = [];

        private int _together;
        private int _against;
        private int _won;
        private int _lost;
        private DateOnly? _lastPlayedOn;
        private string _lastTournament = string.Empty;

        public void AddTogether(PlayedMatch match)
        {
            _together++;
            Touch(match);
        }

        public void AddAgainst(PlayedMatch match)
        {
            _against++;

            if (match.Won)
            {
                _won++;
            }
            else
            {
                _lost++;
            }

            Touch(match);
        }

        public ConnectionView ToView(Guid playerId, string displayName) =>
            new(playerId, displayName, _together, _against, _won, _lost,
                _lastPlayedOn, _lastTournament, _tournaments.Count);

        /// <summary>
        /// Die Matches kommen jüngste zuerst — das erste, das hier ankommt, ist
        /// deshalb auch das letzte gespielte. Ein späteres überschreibt es
        /// nicht.
        /// </summary>
        private void Touch(PlayedMatch match)
        {
            _tournaments.Add(match.TournamentId);

            if (_lastTournament.Length == 0)
            {
                _lastTournament = match.TournamentName;
                _lastPlayedOn = match.PlayedAt?.UtcDateTime is { } instant
                    ? DateOnly.FromDateTime(instant)
                    : match.TournamentStartsOn;
            }
        }
    }
}
