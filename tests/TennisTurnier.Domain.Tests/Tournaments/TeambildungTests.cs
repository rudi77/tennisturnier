using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Tournaments;

/// <summary>
/// Ein Doppel, dessen Paare die Turnierleitung bildet.
///
/// Der Unterschied zum Vereinsdoppel steckt schon in der Meldung: dort bringt
/// jeder seinen Partner mit, hier meldet sich jeder für sich. Wer mit wem
/// spielt, entscheidet danach das Los oder die Turnierleitung — und erst wenn
/// niemand mehr ohne Team dasteht, lässt sich auslosen. Eine einzelne
/// Spielerin im Draw eines Doppels fiele sonst erst am Platz auf, wenn zwei
/// gegen eine antreten.
/// </summary>
public sealed class TeambildungTests
{
    private static Tournament Turnier(
        Discipline disziplin = Discipline.Doubles,
        TeamFormation bildung = TeamFormation.ByOrganiser) =>
        new(
            Guid.NewGuid(),
            "Schleiferl",
            new Venue("TC Test", null, "Maria Alm", "Europe/Vienna"),
            disziplin,
            new DateOnly(2026, 5, 16),
            new DateOnly(2026, 5, 17),
            Guid.NewGuid(),
            bildung);

    /// <summary>Ein Turnier mit <paramref name="anzahl"/> angenommenen Einzelmeldungen.</summary>
    private static (Tournament Turnier, IReadOnlyList<TournamentEntry> Meldungen) MitFeld(int anzahl)
    {
        var turnier = Turnier();
        turnier.OpenRegistration();

        var meldungen = new List<TournamentEntry>();

        for (var i = 0; i < anzahl; i++)
        {
            var meldung = turnier.Enter(Guid.NewGuid(), Guid.NewGuid());
            turnier.Accept(meldung.Id);
            meldungen.Add(meldung);
        }

        return (turnier, meldungen);
    }

    private static Guid Team(Tournament turnier, TournamentEntry erste, TournamentEntry zweite) =>
        turnier.FormTeam(Guid.NewGuid(), Guid.NewGuid(), erste.Id, zweite.Id).Id;

    [Fact]
    public void Ein_Einzelturnier_hat_keine_Teams_zu_bilden()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Turnier(Discipline.Singles, TeamFormation.ByOrganiser));

        Assert.Contains("keine Teams zu bilden", fehler.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Discipline.Doubles)]
    [InlineData(Discipline.Mixed)]
    public void Wer_seine_Teams_selbst_bildet_nimmt_Einzelmeldungen(Discipline disziplin)
    {
        var turnier = Turnier(disziplin);

        Assert.True(turnier.FormsTeamsItself);
        Assert.False(turnier.NeedsPartnerOnEntry);

        // Ohne Partner geht es durch …
        turnier.RequireMatchesDiscipline(hasPartner: false);

        // … mit Partner nicht: er ginge beim Auslosen der Teams wortlos verloren.
        var fehler = Assert.Throws<DomainException>(() =>
            turnier.RequireMatchesDiscipline(hasPartner: true));

        Assert.Contains("bildet die Turnierleitung die Teams", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wo_sich_die_Paare_selbst_melden_bleibt_es_beim_Partner()
    {
        var turnier = Turnier(bildung: TeamFormation.Registered);

        Assert.False(turnier.FormsTeamsItself);
        Assert.True(turnier.NeedsPartnerOnEntry);

        turnier.RequireMatchesDiscipline(hasPartner: true);

        var fehler = Assert.Throws<DomainException>(() =>
            turnier.RequireMatchesDiscipline(hasPartner: false));

        Assert.Contains("braucht einen Partner", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Aus_zwei_Meldungen_wird_eine()
    {
        var (turnier, meldungen) = MitFeld(2);

        var teamId = Team(turnier, meldungen[0], meldungen[1]);

        // Im Draw steht das Team, nicht die beiden Meldungen dahinter.
        var imFeld = Assert.Single(turnier.AcceptedEntries);
        Assert.Equal(teamId, imFeld.Id);

        // Die Meldungen bleiben bestehen — mit ihrem Zeitpunkt und ihrer
        // Herkunft, an denen die Reihenfolge des Nachrückens hängt.
        Assert.All(meldungen, meldung =>
        {
            Assert.Equal(EntryStatus.Paired, meldung.Status);
            Assert.Equal(teamId, meldung.TeamEntryId);
        });

        Assert.Equal([teamId], turnier.FormedTeams.Select(t => t.Id));
        Assert.Empty(turnier.UnpairedEntries);
        Assert.Equal(2, turnier.MembersOf(teamId).Count);
    }

    [Fact]
    public void Eine_Setzung_wandert_nicht_ins_Team()
    {
        // Gesetzt wird, wer im Draw steht. Bliebe die Einzelsetzung stehen,
        // belegte sie eine Position, die niemand einnimmt.
        var (turnier, meldungen) = MitFeld(2);
        turnier.SetSeed(meldungen[0].Id, 1);

        var teamId = Team(turnier, meldungen[0], meldungen[1]);

        Assert.Null(meldungen[0].Seed);

        // Und danach lässt sich das Team setzen.
        turnier.SetSeed(teamId, 1);
        Assert.Equal(1, turnier.AcceptedEntries.Single().Seed);
    }

    [Fact]
    public void Ein_Team_braucht_zwei_verschiedene_Meldungen()
    {
        var (turnier, meldungen) = MitFeld(2);

        var fehler = Assert.Throws<DomainException>(() =>
            Team(turnier, meldungen[0], meldungen[0]));

        Assert.Contains("zwei verschiedene Meldungen", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nur_angenommene_Meldungen_kommen_in_ein_Team()
    {
        var (turnier, meldungen) = MitFeld(2);
        turnier.MoveToWaitingList(meldungen[1].Id);

        var fehler = Assert.Throws<DomainException>(() => Team(turnier, meldungen[0], meldungen[1]));

        Assert.Contains("Nur angenommene Meldungen", fehler.Message, StringComparison.Ordinal);
        Assert.Contains("WaitingList", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_gebildetes_Team_wird_nicht_noch_einmal_verpaart()
    {
        var (turnier, meldungen) = MitFeld(3);
        var teamId = Team(turnier, meldungen[0], meldungen[1]);

        var team = turnier.Entries.Single(e => e.Id == teamId);

        var fehler = Assert.Throws<DomainException>(() => Team(turnier, team, meldungen[2]));

        Assert.Contains("nicht noch einmal verpaaren", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wo_sich_die_Paare_selbst_melden_gibt_es_nichts_zu_bilden()
    {
        var turnier = Turnier(bildung: TeamFormation.Registered);
        turnier.OpenRegistration();

        var erste = turnier.Enter(Guid.NewGuid(), Guid.NewGuid());
        var zweite = turnier.Enter(Guid.NewGuid(), Guid.NewGuid());
        turnier.Accept(erste.Id);
        turnier.Accept(zweite.Id);

        var fehler = Assert.Throws<DomainException>(() => Team(turnier, erste, zweite));

        Assert.Contains("melden sich die Paare selbst", fehler.Message, StringComparison.Ordinal);
        Assert.Throws<DomainException>(turnier.RequireFormsTeamsItself);
    }

    [Fact]
    public void Ein_Team_laesst_sich_wieder_aufloesen()
    {
        var (turnier, meldungen) = MitFeld(2);
        var teamId = Team(turnier, meldungen[0], meldungen[1]);

        turnier.DisbandTeam(teamId);

        Assert.DoesNotContain(turnier.Entries, e => e.Id == teamId);
        Assert.All(meldungen, meldung =>
        {
            Assert.Equal(EntryStatus.Accepted, meldung.Status);
            Assert.Null(meldung.TeamEntryId);
        });

        Assert.Equal(2, turnier.UnpairedEntries.Count);
    }

    [Fact]
    public void Was_kein_Team_ist_laesst_sich_nicht_aufloesen()
    {
        var (turnier, meldungen) = MitFeld(2);

        var fehler = Assert.Throws<DomainException>(() => turnier.DisbandTeam(meldungen[0].Id));

        Assert.Contains("kein gebildetes Team", fehler.Message, StringComparison.Ordinal);

        var unbekannt = Guid.NewGuid();
        Assert.Contains(
            unbekannt.ToString(),
            Assert.Throws<DomainException>(() => turnier.DisbandTeam(unbekannt)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_vollstaendige_Teams_wird_nicht_ausgelost()
    {
        var (turnier, meldungen) = MitFeld(5);
        Team(turnier, meldungen[0], meldungen[1]);
        Team(turnier, meldungen[2], meldungen[3]);

        turnier.CloseRegistration();

        var fehler = Assert.Throws<DomainException>(() =>
            turnier.GenerateDraw(BuiltInFormats.Knockout, templateVersion: 1));

        Assert.Contains("1 Meldung(en) stehen noch ohne Team", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mit_vollstaendigen_Teams_laesst_sich_auslosen()
    {
        var (turnier, meldungen) = MitFeld(4);
        Team(turnier, meldungen[0], meldungen[1]);
        Team(turnier, meldungen[2], meldungen[3]);

        turnier.CloseRegistration();
        turnier.GenerateDraw(BuiltInFormats.Knockout, templateVersion: 1);

        Assert.Equal(TournamentState.DrawGenerated, turnier.State);
        Assert.Equal(2, turnier.AcceptedEntries.Count);
    }

    [Fact]
    public void Nach_dem_Auslosen_bleiben_die_Teams_wie_sie_sind()
    {
        var (turnier, meldungen) = MitFeld(4);
        var teamId = Team(turnier, meldungen[0], meldungen[1]);
        Team(turnier, meldungen[2], meldungen[3]);

        turnier.CloseRegistration();
        turnier.GenerateDraw(BuiltInFormats.Knockout, templateVersion: 1);

        Assert.Throws<DomainException>(() => turnier.DisbandTeam(teamId));
        Assert.Throws<DomainException>(() => Team(turnier, meldungen[0], meldungen[2]));
    }

    [Fact]
    public void Die_Kapazitaet_zaehlt_Menschen_und_nicht_Teams()
    {
        // Sonst wäre ein Feld für zehn nach fünf Teams halb leer — und der
        // elfte Melder bekäme einen Platz, den es nicht gibt.
        var (turnier, meldungen) = MitFeld(4);

        Assert.Equal(4, turnier.CountAgainstCapacity());

        Team(turnier, meldungen[0], meldungen[1]);

        Assert.Equal(4, turnier.CountAgainstCapacity());
    }
}
