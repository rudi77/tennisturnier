namespace Matchday.Domain;

/// <summary>Eine Zeile der Tabelle beziehungsweise der Platzierung.</summary>
public sealed record Standing(
    int Rank,
    Guid ParticipantId,
    string Name,
    int Played,
    int Won,
    int Lost,
    int SetsWon,
    int SetsLost,
    int GamesWon,
    int GamesLost)
{
    public int SetDifference => SetsWon - SetsLost;

    public int GameDifference => GamesWon - GamesLost;
}

internal sealed class Tally(Participant participant)
{
    public Participant Participant { get; } = participant;

    public int Played { get; private set; }

    public int Won { get; private set; }

    public int Lost { get; private set; }

    public int SetsWon { get; private set; }

    public int SetsLost { get; private set; }

    public int GamesWon { get; private set; }

    public int GamesLost { get; private set; }

    public int? EliminatedInRound { get; private set; }

    public void Account(Match match)
    {
        var score = match.Score!;
        var side = match.Side1.ParticipantId == Participant.Id ? 1 : 2;
        var other = side == 1 ? 2 : 1;

        Played++;
        SetsWon += score.SetsWonBy(side);
        SetsLost += score.SetsWonBy(other);
        GamesWon += score.GamesWonBy(side);
        GamesLost += score.GamesWonBy(other);

        if (score.WinnerSide == side)
        {
            Won++;
        }
        else
        {
            Lost++;
            EliminatedInRound = match.Round;
        }
    }

    public Standing ToStanding(int rank) => new(
        rank, Participant.Id, Participant.Name, Played, Won, Lost, SetsWon, SetsLost, GamesWon, GamesLost);
}
