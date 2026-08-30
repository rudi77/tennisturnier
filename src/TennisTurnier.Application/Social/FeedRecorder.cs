using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Social;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Social;

/// <summary>
/// Der Chronist: schreibt die Ereignisse eines Turniers in seinen Feed
/// (ADR-0014).
///
/// Getrennt von <see cref="FeedService"/> und ohne eine einzige
/// Berechtigungsprüfung — und das ist der Grund für die Trennung. Was hier
/// entsteht, ist keine Handlung eines Benutzers, sondern die Aufzeichnung
/// dessen, was das Turnier getan hat. Ein <c>Require</c> stünde hier falsch:
/// wer ein Ergebnis eintragen darf, hat damit nicht zusätzlich das Recht
/// erworben, dass es protokolliert wird — es wird protokolliert, weil es
/// passiert ist.
///
/// Geschrieben wird in die laufende Arbeitseinheit, nicht daneben. Scheitert
/// das Eintragen des Ergebnisses, entsteht auch kein Eintrag.
/// </summary>
public sealed class FeedRecorder
{
    private readonly IFeedRepository _feed;
    private readonly IPostCommitQueue _postCommit;
    private readonly ITournamentNotifier _notifier;
    private readonly IClock _clock;

    public FeedRecorder(
        IFeedRepository feed,
        IPostCommitQueue postCommit,
        ITournamentNotifier notifier,
        IClock clock)
    {
        _feed = feed;
        _postCommit = postCommit;
        _notifier = notifier;
        _clock = clock;
    }

    public void Record(Guid tournamentId, PostKind kind, string text, Guid? matchId = null)
    {
        _feed.Add(TournamentPost.Event(NextId(), tournamentId, kind, text, _clock.Now, matchId));
        Announce(tournamentId);
    }

    /// <summary>
    /// Ein Zustandswechsel des Turniers.
    ///
    /// Nicht jeder verdient eine Zeile: „Entwurf" und „abgebrochen" sind
    /// Verwaltungsvorgänge, aber „Meldung offen", „ausgelost", „läuft" und
    /// „beendet" sind das, worauf eine Gruppe wartet. Was nichts zu melden
    /// hat, meldet nichts — ein Feed, der jede Attributänderung mitschreibt,
    /// wird nicht gelesen.
    /// </summary>
    public void RecordStateChange(Tournament tournament, TournamentState before)
    {
        ArgumentNullException.ThrowIfNull(tournament);

        if (tournament.State == before)
        {
            return;
        }

        var text = tournament.State switch
        {
            TournamentState.RegistrationOpen when before == TournamentState.Draft =>
                $"Die Meldung für „{tournament.Name}“ ist offen.",
            TournamentState.RegistrationOpen =>
                "Die Meldung ist wieder offen — der Draw wurde dafür verworfen.",
            TournamentState.RegistrationClosed => "Meldeschluss. Das Feld steht.",
            TournamentState.InProgress => "Das Turnier läuft.",
            TournamentState.Completed => "Das Turnier ist beendet.",
            _ => null,
        };

        if (text is not null)
        {
            Record(tournament.Id, PostKind.StateChanged, text);
        }
    }

    /// <summary>
    /// Ein eingetragenes Ergebnis.
    ///
    /// Der Text nennt Sieger, Unterlegenen und den Stand aus Sicht des
    /// Siegers — so, wie man es am Platz erzählt. Ein Nichtantreten hat keinen
    /// Stand und sagt das, statt eine leere Klammer zu hinterlassen.
    /// </summary>
    public void RecordResult(
        Guid tournamentId,
        Guid matchId,
        string matchName,
        string winner,
        string loser,
        Score score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var stand = score.Outcome switch
        {
            MatchOutcome.Walkover => "kampflos",
            MatchOutcome.Disqualification => "nach Disqualifikation",
            MatchOutcome.Retirement => $"{SetsFrom(score, score.WinnerSide)} (Aufgabe)",
            _ => SetsFrom(score, score.WinnerSide),
        };

        var prefix = string.IsNullOrWhiteSpace(matchName) ? string.Empty : $"{matchName}: ";

        Record(
            tournamentId,
            PostKind.ResultRecorded,
            $"{prefix}{winner} schlägt {loser} {stand}",
            matchId);
    }

    /// <summary>
    /// Der Spielstand aus Sicht einer Seite. Steht der Sieger auf Seite zwei,
    /// werden die Sätze gedreht — „6:4" liest sich vom Sieger her, und ein
    /// „4:6" hinter „schlägt" wäre schlicht falsch herum.
    /// </summary>
    private static string SetsFrom(Score score, int side) =>
        string.Join(
            ' ',
            score.Sets.Select(set => side == 1
                ? set.ToString()
                : new SetScore(set.Games2, set.Games1, set.TiebreakPoints).ToString()));

    /// <summary>
    /// Zeitgeordnete Kennungen (UUIDv7) und keine zufälligen.
    ///
    /// Der Feed wird nach <c>CreatedAt</c> sortiert, und zwei Einträge können
    /// denselben Zeitstempel tragen — ein Beitritt und die Meldung dazu
    /// entstehen im selben Aufruf, und eine gestellte Uhr macht es zum
    /// Normalfall. Als Stichentscheid taugt eine zufällige Guid nicht: sie
    /// dreht die Reihenfolge bei jedem Lauf anders herum. Eine UUIDv7 trägt
    /// ihre Entstehungszeit vorn und ordnet damit richtig — auch als Text, in
    /// dem SQLite sie ablegt.
    /// </summary>
    private static Guid NextId() => Guid.CreateVersion7();

    /// <summary>
    /// Der Hinweis geht erst nach dem Speichern hinaus — vorher gäbe es
    /// nichts abzuholen, und nach einem Rollback hätte er nie gestimmt.
    /// </summary>
    private void Announce(Guid tournamentId) =>
        _postCommit.Enqueue(ct => _notifier.FeedChangedAsync(tournamentId, ct));
}
