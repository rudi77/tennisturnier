using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;

namespace TennisTurnier.Domain.Tests.Phases;

/// <summary>
/// Was beim Aufbau einer Tabelle übergangen wird.
///
/// Eine Tabelle rechnet über alle Matches der Phase, und nicht jedes davon geht
/// jeden an. Ein Freilos hat keinen Verlierer und darf deshalb nirgends als
/// direkter Vergleich auftauchen — sonst stünde in der Kette ein Sieg gegen
/// niemanden.
/// </summary>
public sealed class StandingsBuilderTests
{
    private static readonly Guid Turnier = Guid.NewGuid();

    private static Phase Phase() =>
        new(Guid.NewGuid(), Turnier, 1, PhaseFormatKind.RoundRobin);

    [Fact]
    public void Ein_fremdes_Match_zaehlt_fuer_niemanden()
    {
        var phase = Phase();
        var match = phase.AddPairings([
            new Pairing(1, 1, ParticipantRef.Of(Guid.NewGuid()), ParticipantRef.Of(Guid.NewGuid())),
        ])[0];

        Assert.Equal(1, StandingsBuilder.SideOf(match, match.Side1.EntryId!.Value));
        Assert.Equal(2, StandingsBuilder.SideOf(match, match.Side2.EntryId!.Value));

        // Wer nicht mitspielt, hat auch keine Seite — und das Match zählt für
        // ihn nicht mit.
        Assert.Null(StandingsBuilder.SideOf(match, Guid.NewGuid()));
    }

    [Fact]
    public void Eine_offene_Seite_gehoert_niemandem()
    {
        // Das Finale steht, bevor seine Teilnehmer feststehen. Wer dort nach
        // seiner Seite fragt, bekommt keine — auch nicht die leere Kennung.
        var phase = Phase();
        var match = phase.AddPairings([
            new Pairing(1, 1, ParticipantRef.Open, ParticipantRef.Open, "Finale"),
        ])[0];

        Assert.Null(StandingsBuilder.SideOf(match, Guid.NewGuid()));
        Assert.Null(StandingsBuilder.SideOf(match, Guid.Empty));
    }

    [Fact]
    public void Ein_Freilos_ist_kein_direkter_Vergleich()
    {
        var einer = Guid.NewGuid();
        var anderer = Guid.NewGuid();

        var phase = Phase();
        var matches = phase.AddPairings([
            new Pairing(1, 1, ParticipantRef.Of(einer), ParticipantRef.ByeSlot),
            new Pairing(1, 2, ParticipantRef.Of(anderer), ParticipantRef.Of(einer)),
        ]);

        // Das Freilos entscheidet sich beim Anlegen von selbst.
        Assert.Equal(MatchOutcome.Bye, matches[0].Score!.Outcome);

        // Das zweite Match bleibt offen — auch daraus entsteht kein Vergleich.
        var tabelle = new[]
        {
            Eintrag(einer, "Einer", punkte: 2),
            Eintrag(anderer, "Anderer", punkte: 0),
        };

        var kontext = StandingsBuilder.ContextOf(tabelle, phase.Matches);

        Assert.Empty(kontext.HeadToHead);
        Assert.Equal(0, kontext.Buchholz[einer]);
        Assert.Equal(0, kontext.Buchholz[anderer]);
    }

    [Fact]
    public void Eine_entschiedene_Begegnung_zaehlt_fuer_beide()
    {
        var sieger = Guid.NewGuid();
        var verlierer = Guid.NewGuid();

        var phase = Phase();
        var match = phase.AddPairings([
            new Pairing(1, 1, ParticipantRef.Of(sieger), ParticipantRef.Of(verlierer)),
        ])[0];

        phase.RecordResult(
            match.Id,
            Score.Played([new SetScore(6, 4), new SetScore(6, 3)], new MatchFormat()));

        var kontext = StandingsBuilder.ContextOf(
            [Eintrag(sieger, "Sieger", punkte: 2), Eintrag(verlierer, "Verlierer", punkte: 0)],
            phase.Matches);

        Assert.Equal(1, kontext.HeadToHead[(sieger, verlierer)]);

        // Buchholz ist die Summe der Punkte der Gegner.
        Assert.Equal(0, kontext.Buchholz[sieger]);
        Assert.Equal(2, kontext.Buchholz[verlierer]);
    }

    private static TableRecord Eintrag(Guid id, string name, int punkte) =>
        new(id, name, Group: null, Seed: null, Played: 1, Won: punkte > 0 ? 1 : 0,
            Lost: punkte > 0 ? 0 : 1, punkte, SetsWon: 0, SetsLost: 0, GamesWon: 0, GamesLost: 0);
}
