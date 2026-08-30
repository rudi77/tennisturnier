using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Social;

namespace TennisTurnier.Domain.Tests.Social;

/// <summary>
/// Ein Eintrag im Feed eines Turniers (ADR-0014).
///
/// Die tragende Unterscheidung ist die zwischen Beitrag und Ereignis: der eine
/// hat einen Verfasser und lässt sich zurücknehmen, das andere gehört dem
/// Turnier.
/// </summary>
public sealed class TurnierfeedTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 5, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Turnier = Guid.NewGuid();

    private static TournamentPost Beitrag(string text = "Platz 3 ist nass.") =>
        TournamentPost.Message(Guid.CreateVersion7(), Turnier, Guid.NewGuid(), text, Jetzt);

    private static TournamentPost Ereignis(PostKind kind = PostKind.DrawGenerated) =>
        TournamentPost.Event(Guid.CreateVersion7(), Turnier, kind, "Der Draw steht.", Jetzt);

    [Fact]
    public void Ein_Beitrag_traegt_seinen_Verfasser()
    {
        var verfasser = Guid.NewGuid();
        var post = TournamentPost.Message(Guid.CreateVersion7(), Turnier, verfasser, "Hallo.", Jetzt);

        Assert.True(post.IsMessage);
        Assert.Equal(verfasser, post.AuthorUserId);
        Assert.Equal(Jetzt, post.CreatedAt);
        Assert.Null(post.MatchId);
    }

    /// <summary>
    /// Ein Ereignis dem Schiedsrichter zuzuschreiben, der das Ergebnis
    /// eintippt, wäre eine Behauptung über eine Handlung, die dem Turnier
    /// gehört und nicht ihm.
    /// </summary>
    [Fact]
    public void Ein_Ereignis_traegt_keinen_Verfasser()
    {
        var match = Guid.NewGuid();
        var post = TournamentPost.Event(
            Guid.CreateVersion7(), Turnier, PostKind.ResultRecorded, "A schlägt B 6:4 6:2", Jetzt, match);

        Assert.False(post.IsMessage);
        Assert.Null(post.AuthorUserId);
        Assert.Equal(match, post.MatchId);
    }

    [Fact]
    public void Ein_Ereignis_ohne_Verfasser_ist_kein_Beitrag()
    {
        var fehler = Assert.Throws<DomainException>(() => TournamentPost.Event(
            Guid.CreateVersion7(), Turnier, PostKind.Message, "Wer schreibt das?", Jetzt));

        Assert.Contains("ist kein Beitrag", fehler.Message);
    }

    [Fact]
    public void Ein_Beitrag_ohne_Verfasser_wird_abgewiesen()
    {
        Assert.Throws<DomainException>(() => TournamentPost.Message(
            Guid.CreateVersion7(), Turnier, Guid.Empty, "Anonym.", Jetzt));
    }

    [Fact]
    public void Ein_Eintrag_ohne_Turnier_wird_abgewiesen()
    {
        var fehler = Assert.Throws<DomainException>(() => TournamentPost.Message(
            Guid.CreateVersion7(), Guid.Empty, Guid.NewGuid(), "Wohin?", Jetzt));

        Assert.Contains("braucht ein Turnier", fehler.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ein_leerer_Beitrag_ist_keiner(string text)
    {
        var fehler = Assert.Throws<DomainException>(() => Beitrag(text));

        Assert.Contains("leerer Beitrag", fehler.Message);
    }

    [Fact]
    public void Ein_zu_langer_Beitrag_wird_abgewiesen()
    {
        var fehler = Assert.Throws<DomainException>(
            () => Beitrag(new string('x', TournamentPost.MaxTextLength + 1)));

        Assert.Contains("höchstens", fehler.Message);
    }

    [Fact]
    public void Leerraum_am_Rand_wird_abgeschnitten()
    {
        Assert.Equal("Platz 3 ist nass.", Beitrag("  Platz 3 ist nass.  ").Text);
    }

    [Fact]
    public void Ein_Eintrag_nimmt_Antworten_auf()
    {
        var post = Ereignis();
        var verfasser = Guid.NewGuid();

        var kommentar = post.Comment(Guid.CreateVersion7(), verfasser, "Endlich!", Jetzt);

        Assert.Same(kommentar, Assert.Single(post.Comments));
        Assert.Equal(verfasser, kommentar.AuthorUserId);
        Assert.Equal("Endlich!", kommentar.Text);
        Assert.Equal("Endlich!", kommentar.ToString());
    }

    [Fact]
    public void Ein_Kommentar_ohne_Verfasser_wird_abgewiesen()
    {
        var post = Ereignis();

        Assert.Throws<DomainException>(
            () => post.Comment(Guid.CreateVersion7(), Guid.Empty, "Anonym.", Jetzt));
    }

    [Fact]
    public void Ein_Kommentar_laesst_sich_zuruecknehmen()
    {
        var post = Beitrag();
        var kommentar = post.Comment(Guid.CreateVersion7(), Guid.NewGuid(), "Doch nicht.", Jetzt);

        post.RemoveComment(kommentar.Id);

        Assert.Empty(post.Comments);
    }

    /// <summary>
    /// Derselbe Klick zweimal ist derselbe Wille — ein unbekannter Kommentar
    /// ist deshalb kein Fehler.
    /// </summary>
    [Fact]
    public void Ein_unbekannter_Kommentar_zurueckzunehmen_ist_kein_Fehler()
    {
        var post = Beitrag();
        post.Comment(Guid.CreateVersion7(), Guid.NewGuid(), "Bleibt.", Jetzt);

        post.RemoveComment(Guid.NewGuid());

        Assert.Single(post.Comments);
    }

    [Fact]
    public void Ein_Eintrag_nennt_sich_lesbar()
    {
        Assert.Equal("DrawGenerated: Der Draw steht.", Ereignis().ToString());
    }
}
