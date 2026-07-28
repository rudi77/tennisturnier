using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.PublicView;

namespace TennisTurnier.Domain.Tests.PublicView;

public sealed class TournamentProjectionTests
{
    private static readonly DateTimeOffset Monday =
        new(2026, 5, 16, 9, 0, 0, TimeSpan.Zero);

    private static TournamentProjection New(string json = "{\"a\":1}") =>
        new(Guid.NewGuid(), json, Monday);

    [Fact]
    public void Ein_neuer_Stand_bekommt_einen_ETag()
    {
        var projection = New();

        Assert.StartsWith("\"", projection.ETag, StringComparison.Ordinal);
        Assert.EndsWith("\"", projection.ETag, StringComparison.Ordinal);
        Assert.Equal(1, projection.Version);
    }

    [Fact]
    public void Derselbe_Inhalt_ergibt_denselben_ETag()
    {
        // Der Neuaufbau läuft nach jeder Handlung, oft ohne Änderung. Käme dabei
        // ein neuer ETag heraus, wäre jeder Cache wertlos.
        Assert.Equal(New().ETag, New().ETag);
    }

    [Fact]
    public void Ein_unveraenderter_Stand_wird_nicht_uebernommen()
    {
        var projection = New();

        Assert.False(projection.Replace("{\"a\":1}", Monday.AddHours(3)));
        Assert.Equal(1, projection.Version);
        Assert.Equal(Monday, projection.UpdatedAt);
    }

    [Fact]
    public void Ein_geaenderter_Stand_zaehlt_hoch_und_bekommt_einen_neuen_ETag()
    {
        var projection = New();
        var before = projection.ETag;

        Assert.True(projection.Replace("{\"a\":2}", Monday.AddHours(3)));

        Assert.NotEqual(before, projection.ETag);
        Assert.Equal(2, projection.Version);
        Assert.Equal(Monday.AddHours(3), projection.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Eine_Projektion_ohne_Inhalt_gibt_es_nicht(string json)
    {
        Assert.Throws<DomainException>(() => new TournamentProjection(Guid.NewGuid(), json, Monday));
        Assert.Throws<DomainException>(() => New().Replace(json, Monday));
    }
}
