using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Tests.Matches;

/// <summary>
/// Der Summentyp, der den ganzen Turnierbaum trägt (ADR-0001).
///
/// Geprüft wird beides: was er über sich sagt — „Sieger aus …", „Zweiter der
/// Gruppe B" — und dass Kodierung und Lesen ein Paar bleiben. Laufen die beiden
/// Hälften auseinander, sind gespeicherte Bäume unlesbar, und zwar erst beim
/// nächsten Start.
/// </summary>
public sealed class ParticipantRefTests
{
    [Fact]
    public void Eine_Meldungsreferenz_braucht_eine_Meldung()
    {
        Assert.Throws<DomainException>(() => ParticipantRef.Of(Guid.Empty));
    }

    [Fact]
    public void Eine_Siegerreferenz_braucht_ein_Match()
    {
        Assert.Throws<DomainException>(() => ParticipantRef.FromWinnerOf(Guid.Empty));
    }

    [Fact]
    public void Eine_Verliererreferenz_braucht_ein_Match()
    {
        Assert.Throws<DomainException>(() => ParticipantRef.FromLoserOf(Guid.Empty));
    }

    [Fact]
    public void Eine_Gruppenplatzreferenz_braucht_eine_Phase()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            ParticipantRef.FromGroupPosition(Guid.Empty, "A", 1));

        Assert.Contains("braucht eine Phase", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_Gruppenplatz_beginnt_bei_eins()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            ParticipantRef.FromGroupPosition(Guid.NewGuid(), "A", 0));

        Assert.Contains("war 0", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_leerer_Gruppenname_ist_zulaessig_und_heisst_Tabelle()
    {
        var ohneGruppe = ParticipantRef.FromGroupPosition(Guid.NewGuid(), "  ", 2);

        Assert.Equal("Zweiter der Tabelle", ohneGruppe.ToString());
    }

    [Fact]
    public void Ein_fehlender_Gruppenname_wird_zur_leeren_Gruppe()
    {
        var ohneGruppe = ParticipantRef.FromGroupPosition(Guid.NewGuid(), null!, 1);

        Assert.Equal("Erster der Tabelle", ohneGruppe.ToString());
    }

    [Theory]
    [InlineData(1, "Erster der Gruppe A")]
    [InlineData(2, "Zweiter der Gruppe A")]
    [InlineData(3, "Dritter der Gruppe A")]
    [InlineData(4, "4. der Gruppe A")]
    public void Nennt_den_Rang_in_Worten(int rang, string erwartet)
    {
        var referenz = ParticipantRef.FromGroupPosition(Guid.NewGuid(), "Gruppe A", rang);

        Assert.Equal(erwartet, referenz.ToString());
    }

    [Fact]
    public void Ein_Auffuellplatz_nennt_seine_Reihenfolge_voran()
    {
        var referenz = ParticipantRef.FromGroupPosition(Guid.NewGuid(), "#bester", 3);

        Assert.Equal("bester Dritter", referenz.ToString());
    }

    [Fact]
    public void Jede_Form_nennt_sich_verstaendlich()
    {
        var match = Guid.NewGuid();
        var meldung = Guid.NewGuid();

        Assert.Equal($"Meldung {meldung}", ParticipantRef.Of(meldung).ToString());
        Assert.Equal($"Sieger aus {match}", ParticipantRef.FromWinnerOf(match).ToString());
        Assert.Equal($"Verlierer aus {match}", ParticipantRef.FromLoserOf(match).ToString());
        Assert.Equal("Freilos", ParticipantRef.ByeSlot.ToString());
        Assert.Equal("offen", ParticipantRef.Open.ToString());
    }

    [Fact]
    public void Nur_Meldung_und_Freilos_stehen_fest()
    {
        Assert.True(ParticipantRef.Of(Guid.NewGuid()).IsResolved);
        Assert.True(ParticipantRef.ByeSlot.IsResolved);
        Assert.False(ParticipantRef.Open.IsResolved);
        Assert.False(ParticipantRef.FromWinnerOf(Guid.NewGuid()).IsResolved);
    }

    [Fact]
    public void Ein_Freilos_hat_keine_Meldung()
    {
        // Es ist kein Teilnehmer, sondern seine Abwesenheit.
        Assert.Null(ParticipantRef.ByeSlot.ResolvedEntryId);

        var meldung = Guid.NewGuid();
        Assert.Equal(meldung, ParticipantRef.Of(meldung).ResolvedEntryId);
    }

    [Fact]
    public void Nur_Sieger_und_Verlierer_haengen_an_einem_Match()
    {
        var match = Guid.NewGuid();

        Assert.Equal(match, ParticipantRef.FromWinnerOf(match).DependsOnMatch);
        Assert.Equal(match, ParticipantRef.FromLoserOf(match).DependsOnMatch);
        Assert.Null(ParticipantRef.Of(Guid.NewGuid()).DependsOnMatch);
        Assert.Null(ParticipantRef.ByeSlot.DependsOnMatch);
    }

    public static TheoryData<ParticipantRef> AlleFormen() =>
    [
        ParticipantRef.Of(Guid.Parse("11111111-1111-1111-1111-111111111111")),
        ParticipantRef.FromWinnerOf(Guid.Parse("22222222-2222-2222-2222-222222222222")),
        ParticipantRef.FromLoserOf(Guid.Parse("33333333-3333-3333-3333-333333333333")),
        ParticipantRef.FromGroupPosition(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Gruppe A", 2),
        ParticipantRef.FromGroupPosition(Guid.Parse("55555555-5555-5555-5555-555555555555"), "", 1),
        ParticipantRef.ByeSlot,
        ParticipantRef.Open,
    ];

    [Theory]
    [MemberData(nameof(AlleFormen))]
    public void Kodierung_und_Lesen_bleiben_ein_Paar(ParticipantRef referenz)
    {
        Assert.Equal(referenz, ParticipantRef.Parse(referenz.Encode()));
    }

    [Fact]
    public void Eine_Gruppe_darf_Doppelpunkte_enthalten()
    {
        var referenz = ParticipantRef.FromGroupPosition(Guid.NewGuid(), "Gruppe: A:1", 1);

        Assert.Equal(referenz, ParticipantRef.Parse(referenz.Encode()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Eine_leere_Referenz_laesst_sich_nicht_lesen(string kodiert)
    {
        var fehler = Assert.Throws<DomainException>(() => ParticipantRef.Parse(kodiert));

        Assert.Contains("lässt sich nicht lesen", fehler.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("X:123")]
    [InlineData("nur Text")]
    [InlineData("G:nur:zwei")]
    public void Eine_unlesbare_Referenz_wird_benannt(string kodiert)
    {
        var fehler = Assert.Throws<DomainException>(() => ParticipantRef.Parse(kodiert));

        Assert.Contains(kodiert, fehler.Message, StringComparison.Ordinal);
    }
}
