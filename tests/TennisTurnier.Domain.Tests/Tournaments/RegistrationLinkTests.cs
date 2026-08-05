using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Tournaments;

public sealed class RegistrationLinkTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 5, 1, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Jeder_Link_bekommt_ein_eigenes_Token()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => RegistrationLink.New().Token).ToList();

        Assert.Equal(50, tokens.Distinct().Count());
    }

    [Fact]
    public void Das_Token_ist_URL_sicher_und_kurz_genug_fuer_einen_Aushang()
    {
        var token = RegistrationLink.New().Token;

        Assert.All(token, c => Assert.True(
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_',
            $"„{c}“ gehört nicht in eine Adresszeile."));

        // 128 Bit in Base64Url: 22 Zeichen ohne Auffüllung.
        Assert.Equal(22, token.Length);
    }

    [Fact]
    public void Eine_Aenderung_der_Bedingungen_laesst_das_Token_stehen()
    {
        // Der Link hängt am Aushang. Wer nachträglich eine Kapazität einträgt,
        // will ihn nicht ungültig machen.
        var link = RegistrationLink.New();

        var changed = link.With(capacity: 32, deadline: Jetzt);

        Assert.Equal(link.Token, changed.Token);
        Assert.Equal(32, changed.Capacity);
        Assert.Equal(Jetzt, changed.Deadline);
    }

    [Fact]
    public void Ein_erneuertes_Token_entwertet_das_alte()
    {
        var link = RegistrationLink.New(capacity: 16);

        var rotated = link.Rotated();

        Assert.NotEqual(link.Token, rotated.Token);
        Assert.Equal(16, rotated.Capacity);
    }

    [Fact]
    public void Ohne_Kapazitaet_wird_das_Feld_nie_voll()
    {
        var link = RegistrationLink.New();

        Assert.False(link.IsFull(0));
        Assert.False(link.IsFull(1000));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(15, false)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    public void Die_Kapazitaet_ist_erreicht_wenn_so_viele_gemeldet_sind(int applied, bool full)
    {
        Assert.Equal(full, RegistrationLink.New(capacity: 16).IsFull(applied));
    }

    [Fact]
    public void Eine_Kapazitaet_unter_eins_wird_abgewiesen()
    {
        Assert.Throws<DomainException>(() => RegistrationLink.New(capacity: 0));
    }

    [Fact]
    public void Ohne_Meldeschluss_verstreicht_nichts()
    {
        Assert.False(RegistrationLink.New().IsPastDeadline(Jetzt.AddYears(10)));
    }

    [Fact]
    public void Der_Meldeschluss_selbst_liegt_noch_innerhalb()
    {
        // Wer in der letzten Sekunde absendet, hat gemeldet.
        var link = RegistrationLink.New(deadline: Jetzt);

        Assert.False(link.IsPastDeadline(Jetzt));
        Assert.True(link.IsPastDeadline(Jetzt.AddSeconds(1)));
    }
}
