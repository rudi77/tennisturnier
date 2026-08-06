using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Tournaments;

/// <summary>
/// Der Termin eines Turniers ist optional.
///
/// Ein Turnier entsteht meist, bevor der Termin steht: der Veranstalter legt es
/// an, sammelt Meldungen und ruft erst dann beim Verein an. Ein Pflichtfeld
/// zwänge ihn, an dieser Stelle etwas zu erfinden — und eine erfundene Angabe
/// ist schlechter als gar keine, weil ihr niemand ansieht, dass sie erfunden
/// ist.
/// </summary>
public sealed class TerminTests
{
    private static readonly Guid TemplateId = Guid.NewGuid();

    private static readonly Venue Ort = new("TC Test", null, "Maria Alm", "Europe/Vienna");

    private static Tournament Turnier(DateOnly? beginn = null, DateOnly? ende = null) => new(
        Guid.NewGuid(),
        "Clubmeisterschaft",
        Ort,
        Discipline.Singles,
        beginn,
        ende,
        TemplateId);

    [Fact]
    public void Ein_Turnier_entsteht_ohne_Termin()
    {
        var turnier = Turnier();

        Assert.Null(turnier.StartsOn);
        Assert.Null(turnier.EndsOn);
        Assert.False(turnier.HasDates);
    }

    /// <summary>
    /// Der eintägige Fall ist die häufigste Vereinsausschreibung. Dasselbe Datum
    /// zweimal einzutragen wäre eine Zumutung ohne Gegenwert.
    /// </summary>
    [Fact]
    public void Ein_Beginn_ohne_Ende_ist_ein_eintaegiges_Turnier()
    {
        var turnier = Turnier(new DateOnly(2026, 5, 16));

        Assert.Equal(new DateOnly(2026, 5, 16), turnier.StartsOn);
        Assert.Equal(new DateOnly(2026, 5, 16), turnier.EndsOn);
        Assert.True(turnier.HasDates);
    }

    [Fact]
    public void Ein_Ende_ohne_Beginn_ergibt_keinen_Zeitraum()
    {
        var fehler = Assert.Throws<DomainException>(
            () => Turnier(beginn: null, ende: new DateOnly(2026, 5, 17)));

        Assert.Contains("ohne Beginn", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_Ende_vor_dem_Beginn_bleibt_abgewiesen()
    {
        Assert.Throws<DomainException>(
            () => Turnier(new DateOnly(2026, 5, 17), new DateOnly(2026, 5, 16)));
    }

    /// <summary>
    /// Ein abgesagter Termin ist eine gewöhnliche Nachricht und kein Sonderfall.
    /// Ohne diesen Weg bliebe ein einmal eingetragenes Datum für immer stehen.
    /// </summary>
    [Fact]
    public void Ein_Termin_laesst_sich_wieder_offenlassen()
    {
        var turnier = Turnier(new DateOnly(2026, 5, 16), new DateOnly(2026, 5, 17));

        turnier.Reschedule(null, null);

        Assert.False(turnier.HasDates);
        Assert.Null(turnier.StartsOn);
    }

    /// <summary>
    /// Ohne Termin gibt es kein Fenster, in dem eine Ansetzung liegen könnte —
    /// und deshalb auch nichts, worauf die Platzzeiten zuzuschneiden wären.
    /// </summary>
    [Fact]
    public void Ohne_Termin_gibt_es_keinen_Zeitraum()
    {
        Assert.Null(Turnier().Period());
        Assert.NotNull(Turnier(new DateOnly(2026, 5, 16)).Period());
    }

    /// <summary>
    /// Der Spielplan sagt, was fehlt, statt stumm nichts anzulegen. Vorher lief
    /// die Platzzeitanlage über einen leeren Zeitraum und meldete Erfolg.
    /// </summary>
    [Fact]
    public void Der_Spielplan_verlangt_einen_Termin_und_sagt_das()
    {
        var fehler = Assert.Throws<DomainException>(() => Turnier().RequireDatesRecorded());

        Assert.Contains("kein Termin", fehler.Message, StringComparison.Ordinal);

        // Mit Termin ist es still.
        Turnier(new DateOnly(2026, 5, 16)).RequireDatesRecorded();
    }

    /// <summary>
    /// Ohne Termin gibt es hier keine Schranke, gegen die zu prüfen wäre — es
    /// ist schlicht nichts zu sagen.
    ///
    /// Dass daraus kein Loch wird, ist Sache der Aufrufer und nicht dieser
    /// Methode: der Spielplan verlangt den Termin über RequireDatesRecorded,
    /// bevor er überhaupt so weit kommt. Dieser Kommentar behauptete einmal das
    /// Gegenteil, und die Prüfung, auf die er verwies, stand damals an einer
    /// einzigen, ganz anderen Stelle — die Lücke war echt.
    /// </summary>
    [Fact]
    public void Ohne_Termin_schraenkt_kein_Zeitpunkt_ein()
    {
        Turnier().RequireScheduledWithin(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Throws<DomainException>(() => Turnier(new DateOnly(2026, 5, 16))
            .RequireScheduledWithin(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    /// <summary>
    /// Ein Platz ohne Turnierzeitraum liefert seine Fenster ungeschnitten. Vorher
    /// gab es diesen Fall nicht — jetzt ist er der Normalfall eines frisch
    /// angelegten Turniers, und ein leeres Ergebnis hieße „keine Platzzeit", was
    /// nicht stimmt.
    /// </summary>
    [Fact]
    public void Ohne_Zeitraum_bleiben_die_Platzzeiten_ungeschnitten()
    {
        var turnier = Turnier();
        var platz = turnier.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);

        var fenster = new TimeSlot(
            new DateTimeOffset(2026, 5, 16, 8, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 5, 16, 20, 0, 0, TimeSpan.FromHours(2)));

        platz.AddWindow(Guid.NewGuid(), fenster);

        var frei = platz.FreeWindows(turnier.Period());

        Assert.Single(frei);
        Assert.Equal(fenster, frei[0]);
    }
}
