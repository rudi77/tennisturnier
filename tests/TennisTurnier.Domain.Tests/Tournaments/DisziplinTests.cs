using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Tournaments;

/// <summary>
/// Die Disziplin steht in der Ausschreibung.
///
/// Bislang ergab sie sich nur daraus, was die Turnierleitung als Teilnehmer
/// anlegte — ein Doppelturnier war eines, dessen Teilnehmer zufällig Paare
/// waren. Das trug, solange nur die Turnierleitung meldete. Sobald sich jemand
/// selbst melden kann, entschiede sonst der erste Melder, was für ein Turnier es
/// wird.
/// </summary>
public sealed class DisziplinTests
{
    private static Tournament Turnier(Discipline disziplin) => new(
        Guid.NewGuid(),
        "Clubmeisterschaft",
        new Venue("TC Test", null, "Maria Alm", "Europe/Vienna"),
        disziplin,
        new DateOnly(2026, 5, 16),
        new DateOnly(2026, 5, 17),
        Guid.NewGuid());

    [Theory]
    [InlineData(Discipline.Doubles)]
    [InlineData(Discipline.Mixed)]
    public void Ein_Doppelturnier_weist_eine_Meldung_ohne_Partner_ab(Discipline disziplin)
    {
        var ex = Assert.Throws<DomainException>(
            () => Turnier(disziplin).RequireMatchesDiscipline(hasPartner: false));

        Assert.Contains("Partner", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Discipline.Doubles)]
    [InlineData(Discipline.Mixed)]
    public void Ein_Doppelturnier_nimmt_ein_Paar(Discipline disziplin)
    {
        Turnier(disziplin).RequireMatchesDiscipline(hasPartner: true);
    }

    [Fact]
    public void Ein_Einzelturnier_weist_ein_Paar_ab()
    {
        Assert.Throws<DomainException>(
            () => Turnier(Discipline.Singles).RequireMatchesDiscipline(hasPartner: true));
    }

    [Fact]
    public void Ein_Einzelturnier_nimmt_einen_Einzelnen()
    {
        Turnier(Discipline.Singles).RequireMatchesDiscipline(hasPartner: false);
    }

    [Fact]
    public void Mixed_verhaelt_sich_beim_Melden_wie_Doppel()
    {
        // Die Paarungsregel selbst ist Sache der Ausschreibung und wird hier
        // nicht geprüft — das Geschlecht der Spieler wird gar nicht erhoben.
        Assert.True(Discipline.Mixed.NeedsPartner());
        Assert.True(Discipline.Doubles.NeedsPartner());
        Assert.False(Discipline.Singles.NeedsPartner());
    }

    [Fact]
    public void Die_Disziplin_laesst_sich_aendern_solange_niemand_gemeldet_ist()
    {
        var tournament = Turnier(Discipline.Singles);

        tournament.ChangeDiscipline(Discipline.Doubles);

        Assert.Equal(Discipline.Doubles, tournament.Discipline);
    }

    [Fact]
    public void Mit_Meldungen_im_Feld_laesst_sie_sich_nicht_mehr_aendern()
    {
        // Ein Einzelturnier, das nachträglich zum Doppel erklärt wird, hätte ein
        // Feld aus Einzelspielern — und umgekehrt Paare, von denen die Hälfte
        // nicht mehr antreten dürfte.
        var tournament = Turnier(Discipline.Singles);
        tournament.OpenRegistration();
        tournament.Enter(Guid.NewGuid(), Guid.NewGuid());

        var ex = Assert.Throws<DomainException>(() => tournament.ChangeDiscipline(Discipline.Doubles));

        Assert.Contains("Meldungen", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dieselbe_Disziplin_noch_einmal_zu_setzen_bleibt_erlaubt()
    {
        // Sonst scheiterte jedes Speichern der Eckdaten, sobald eine Meldung
        // steht — die Oberfläche schickt immer alle Felder.
        var tournament = Turnier(Discipline.Singles);
        tournament.OpenRegistration();
        tournament.Enter(Guid.NewGuid(), Guid.NewGuid());

        tournament.ChangeDiscipline(Discipline.Singles);

        Assert.Equal(Discipline.Singles, tournament.Discipline);
    }

    [Fact]
    public void Ein_ausgeloster_Draw_bleibt_von_der_Disziplin_unberuehrt()
    {
        // Die Prüfung greift beim Melden. Ist das Feld einmal ausgelost, ist es
        // eingefroren — dort entscheidet der Zustandsautomat, nicht die
        // Disziplin.
        var tournament = Turnier(Discipline.Singles);
        tournament.OpenRegistration();

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var entry = tournament.Enter(Guid.NewGuid(), Guid.NewGuid());
            tournament.Accept(entry.Id);
        }

        tournament.CloseRegistration();
        tournament.GenerateDraw(BuiltInFormats.Knockout, templateVersion: 1);

        Assert.Throws<DomainException>(() => tournament.ChangeDiscipline(Discipline.Doubles));
    }
}
