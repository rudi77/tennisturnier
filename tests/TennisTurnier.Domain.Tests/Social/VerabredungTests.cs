using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Social;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Social;

/// <summary>
/// Eine Spielverabredung außerhalb jedes Turniers (ADR-0015).
///
/// Sie hat kein Ergebnis und keinen gepflegten Zustand: gespeichert wird nur,
/// ob abgesagt wurde. Ob genug zugesagt haben, ergibt sich aus den Antworten
/// und der Disziplin — und genau das wird hier geprüft.
/// </summary>
public sealed class VerabredungTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 5, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Termin = new(2026, 6, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Gastgeber = Guid.NewGuid();

    private static PlayDate Verabredung(
        Discipline disziplin = Discipline.Singles,
        string? notiz = "Bringt Bälle mit.") =>
        new(
            Guid.CreateVersion7(),
            Gastgeber,
            "Samstag eine Runde?",
            disziplin,
            "TC Musterstadt, Platz 2",
            Termin,
            TimeSpan.FromMinutes(60),
            notiz,
            Jetzt);

    private static Guid Einladen(PlayDate verabredung)
    {
        var konto = Guid.NewGuid();
        verabredung.Invite(Guid.CreateVersion7(), konto, Guid.NewGuid());

        return konto;
    }

    // --- Aufbau -----------------------------------------------------------

    [Fact]
    public void Eine_neue_Verabredung_zaehlt_den_Gastgeber_mit()
    {
        var verabredung = Verabredung();

        Assert.Equal(2, verabredung.RequiredPlayers);
        Assert.Equal(1, verabredung.Committed);
        Assert.Equal(1, verabredung.Missing);
        Assert.False(verabredung.IsConfirmed);
        Assert.False(verabredung.IsCancelled);
        Assert.Equal(Termin.AddMinutes(60), verabredung.EndsAt);
    }

    [Theory]
    [InlineData(Discipline.Singles, 2)]
    [InlineData(Discipline.Doubles, 4)]
    [InlineData(Discipline.Mixed, 4)]
    public void Die_Disziplin_bestimmt_wie_viele_gebraucht_werden(Discipline disziplin, int erwartet)
    {
        Assert.Equal(erwartet, Verabredung(disziplin).RequiredPlayers);
    }

    [Fact]
    public void Eine_Verabredung_ohne_Gastgeber_wird_abgewiesen()
    {
        var fehler = Assert.Throws<DomainException>(() => new PlayDate(
            Guid.CreateVersion7(), Guid.Empty, "Wer lädt ein?", Discipline.Singles,
            "TC Test", Termin, TimeSpan.FromMinutes(60), null, Jetzt));

        Assert.Contains("braucht einen Gastgeber", fehler.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Eine_Verabredung_ohne_Dauer_wird_abgewiesen(int minuten)
    {
        Assert.Throws<DomainException>(() => new PlayDate(
            Guid.CreateVersion7(), Gastgeber, "Wie lange?", Discipline.Singles,
            "TC Test", Termin, TimeSpan.FromMinutes(minuten), null, Jetzt));
    }

    [Theory]
    [InlineData("", "TC Test")]
    [InlineData("   ", "TC Test")]
    [InlineData("Titel", "")]
    [InlineData("Titel", "   ")]
    public void Titel_und_Ort_sind_Pflicht(string titel, string ort)
    {
        Assert.Throws<DomainException>(() => new PlayDate(
            Guid.CreateVersion7(), Gastgeber, titel, Discipline.Singles,
            ort, Termin, TimeSpan.FromMinutes(60), null, Jetzt));
    }

    [Fact]
    public void Eine_Notiz_ist_freiwillig()
    {
        Assert.Null(Verabredung(notiz: null).Note);
        Assert.Null(Verabredung(notiz: "   ").Note);
    }

    [Fact]
    public void Ein_zu_langer_Titel_wird_abgewiesen()
    {
        var fehler = Assert.Throws<DomainException>(() => new PlayDate(
            Guid.CreateVersion7(), Gastgeber, new string('x', PlayDate.MaxTitleLength + 1),
            Discipline.Singles, "TC Test", Termin, TimeSpan.FromMinutes(60), null, Jetzt));

        Assert.Contains("höchstens", fehler.Message);
    }

    [Fact]
    public void Ein_zu_langer_Ort_wird_abgewiesen()
    {
        Assert.Throws<DomainException>(() => new PlayDate(
            Guid.CreateVersion7(), Gastgeber, "Titel", Discipline.Singles,
            new string('x', PlayDate.MaxVenueLength + 1), Termin, TimeSpan.FromMinutes(60), null, Jetzt));
    }

    [Fact]
    public void Eine_zu_lange_Notiz_wird_abgewiesen()
    {
        Assert.Throws<DomainException>(
            () => Verabredung(notiz: new string('x', PlayDate.MaxNoteLength + 1)));
    }

    // --- Einladen ---------------------------------------------------------

    [Fact]
    public void Eine_Einladung_beginnt_ohne_Antwort()
    {
        var verabredung = Verabredung();
        var konto = Einladen(verabredung);

        var einladung = Assert.Single(verabredung.Invitations);

        Assert.Equal(konto, einladung.UserId);
        Assert.Equal(InvitationResponse.Pending, einladung.Response);
        Assert.Equal(1, verabredung.Committed);
        Assert.Contains("Pending", einladung.ToString());
    }

    /// <summary>
    /// Dieselbe Person zweimal ist derselbe Klick. Die bestehende Einladung
    /// samt ihrer Antwort bleibt stehen — sie zu ersetzen hieße, eine Absage
    /// stillschweigend zurückzunehmen.
    /// </summary>
    [Fact]
    public void Dieselbe_Person_zweimal_einzuladen_laesst_ihre_Antwort_stehen()
    {
        var verabredung = Verabredung();
        var konto = Einladen(verabredung);
        verabredung.Respond(konto, accepted: false);

        verabredung.Invite(Guid.CreateVersion7(), konto, Guid.NewGuid());

        var einladung = Assert.Single(verabredung.Invitations);
        Assert.Equal(InvitationResponse.Declined, einladung.Response);
    }

    [Fact]
    public void Der_Gastgeber_laedt_sich_nicht_selbst_ein()
    {
        var verabredung = Verabredung();

        var fehler = Assert.Throws<DomainException>(
            () => verabredung.Invite(Guid.CreateVersion7(), Gastgeber, Guid.NewGuid()));

        Assert.Contains("bereits dabei", fehler.Message);
    }

    [Fact]
    public void Eine_Einladung_ohne_Empfaenger_wird_abgewiesen()
    {
        var verabredung = Verabredung();

        Assert.Throws<DomainException>(
            () => verabredung.Invite(Guid.CreateVersion7(), Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    public void Mehr_als_die_Obergrenze_wird_abgewiesen()
    {
        var verabredung = Verabredung(Discipline.Doubles);

        for (var i = 0; i < PlayDate.MaxInvitations; i++)
        {
            verabredung.Invite(Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid());
        }

        var fehler = Assert.Throws<DomainException>(
            () => verabredung.Invite(Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid()));

        Assert.Contains("höchstens", fehler.Message);
    }

    // --- Antworten --------------------------------------------------------

    [Fact]
    public void Mit_der_Zusage_steht_die_Runde()
    {
        var verabredung = Verabredung();
        var konto = Einladen(verabredung);

        verabredung.Respond(konto, accepted: true);

        Assert.Equal(2, verabredung.Committed);
        Assert.Equal(0, verabredung.Missing);
        Assert.True(verabredung.IsConfirmed);
        Assert.Contains("2/2", verabredung.ToString());
    }

    [Fact]
    public void Eine_Absage_zaehlt_nicht_mit()
    {
        var verabredung = Verabredung();
        var konto = Einladen(verabredung);

        verabredung.Respond(konto, accepted: false);

        Assert.Equal(1, verabredung.Committed);
        Assert.False(verabredung.IsConfirmed);
    }

    [Fact]
    public void Wer_nicht_eingeladen_ist_antwortet_nicht()
    {
        var verabredung = Verabredung();

        var fehler = Assert.Throws<DomainException>(
            () => verabredung.Respond(Guid.NewGuid(), accepted: true));

        Assert.Contains("keine Einladung", fehler.Message);
    }

    [Fact]
    public void In_eine_volle_Runde_sagt_niemand_mehr_zu()
    {
        var verabredung = Verabredung();
        var erster = Einladen(verabredung);
        var zweiter = Einladen(verabredung);

        verabredung.Respond(erster, accepted: true);

        var fehler = Assert.Throws<DomainException>(() => verabredung.Respond(zweiter, accepted: true));
        Assert.Contains("bereits voll", fehler.Message);

        // Absagen geht weiterhin: wer nicht kann, soll es sagen dürfen.
        verabredung.Respond(zweiter, accepted: false);
        Assert.Equal(InvitationResponse.Declined,
            verabredung.Invitations.Single(i => i.UserId == zweiter).Response);
    }

    /// <summary>
    /// Die eigene bestehende Zusage zählt nicht gegen einen — sonst ließe sie
    /// sich nicht bestätigen, sobald die Runde voll ist.
    /// </summary>
    [Fact]
    public void Eine_bestehende_Zusage_laesst_sich_wiederholen()
    {
        var verabredung = Verabredung();
        var konto = Einladen(verabredung);

        verabredung.Respond(konto, accepted: true);
        verabredung.Respond(konto, accepted: true);

        Assert.Equal(2, verabredung.Committed);
    }

    [Fact]
    public void Eine_Zusage_laesst_sich_zuruecknehmen()
    {
        var verabredung = Verabredung();
        var konto = Einladen(verabredung);

        verabredung.Respond(konto, accepted: true);
        verabredung.Respond(konto, accepted: false);

        Assert.Equal(1, verabredung.Committed);
        Assert.False(verabredung.IsConfirmed);
    }

    // --- Absagen ----------------------------------------------------------

    [Fact]
    public void Eine_abgesagte_Verabredung_steht_nicht_mehr()
    {
        var verabredung = Verabredung();
        var konto = Einladen(verabredung);
        verabredung.Respond(konto, accepted: true);

        verabredung.Cancel();

        Assert.True(verabredung.IsCancelled);
        Assert.False(verabredung.IsConfirmed);
    }

    [Fact]
    public void Auf_eine_abgesagte_Verabredung_antwortet_niemand_mehr()
    {
        var verabredung = Verabredung();
        var konto = Einladen(verabredung);
        verabredung.Cancel();

        Assert.Throws<DomainException>(() => verabredung.Respond(konto, accepted: true));
        Assert.Throws<DomainException>(
            () => verabredung.Invite(Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Zweimal_absagen_ist_kein_Fehler()
    {
        var verabredung = Verabredung();

        verabredung.Cancel();
        verabredung.Cancel();

        Assert.True(verabredung.IsCancelled);
    }
}
