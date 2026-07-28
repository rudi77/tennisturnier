using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Phases;

namespace TennisTurnier.Domain.Tests.Phases;

/// <summary>
/// Die Tiebreaker-Kette für sich, ohne Matches und ohne Phase. Der Dreier-Patt
/// ist der Fall, an dem sich die Reihenfolge der Kriterien tatsächlich zeigt.
/// </summary>
public sealed class TiebreakerChainTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static TableRecord Record(
        Guid id,
        string name,
        int setsWon = 0,
        int setsLost = 0,
        int gamesWon = 0,
        int gamesLost = 0,
        int? seed = null) =>
        new(id, name, Group: null, seed, Played: 2, Won: 1, Lost: 1, Points: 2,
            setsWon, setsLost, gamesWon, gamesLost);

    private static TiebreakContext Context(
        (Guid Winner, Guid Loser)[]? results = null,
        IReadOnlyDictionary<Guid, int>? buchholz = null) =>
        new(
            (results ?? []).ToDictionary(r => r, _ => 1),
            buchholz ?? new Dictionary<Guid, int>());

    [Fact]
    public void Der_direkte_Vergleich_geht_dem_Satzverhaeltnis_vor()
    {
        var a = Record(A, "A", setsWon: 4, setsLost: 0);
        var b = Record(B, "B", setsWon: 2, setsLost: 2);

        var order = TiebreakerChain.Order(
            [a, b],
            [Tiebreaker.DirectEncounter, Tiebreaker.SetRatio],
            Context([(B, A)]));

        Assert.Equal([B, A], order.Select(r => r.EntryId));
    }

    [Fact]
    public void Ohne_direkten_Vergleich_entscheidet_das_naechste_Kriterium()
    {
        var a = Record(A, "A", setsWon: 4, setsLost: 0);
        var b = Record(B, "B", setsWon: 2, setsLost: 2);

        var order = TiebreakerChain.Order(
            [b, a],
            [Tiebreaker.DirectEncounter, Tiebreaker.SetRatio],
            Context());

        Assert.Equal([A, B], order.Select(r => r.EntryId));
    }

    [Fact]
    public void Bei_gleichem_Satzverhaeltnis_entscheidet_das_Spielverhaeltnis()
    {
        var a = Record(A, "A", setsWon: 2, setsLost: 2, gamesWon: 24, gamesLost: 20);
        var b = Record(B, "B", setsWon: 2, setsLost: 2, gamesWon: 20, gamesLost: 24);

        var order = TiebreakerChain.Order(
            [b, a],
            [Tiebreaker.SetRatio, Tiebreaker.GameRatio],
            Context());

        Assert.Equal([A, B], order.Select(r => r.EntryId));
    }

    [Fact]
    public void Ein_Dreier_Patt_im_Ringschluss_wird_ueber_das_naechste_Kriterium_geloest()
    {
        // A schlägt B, B schlägt C, C schlägt A. Der direkte Vergleich ergibt für
        // alle drei dieselbe Bilanz — genau dafür gibt es die Kette.
        var a = Record(A, "A", setsWon: 4, setsLost: 2);
        var b = Record(B, "B", setsWon: 3, setsLost: 3);
        var c = Record(C, "C", setsWon: 2, setsLost: 4);

        var order = TiebreakerChain.Order(
            [c, a, b],
            [Tiebreaker.DirectEncounter, Tiebreaker.SetRatio],
            Context([(A, B), (B, C), (C, A)]));

        Assert.Equal([A, B, C], order.Select(r => r.EntryId));
    }

    [Fact]
    public void Der_direkte_Vergleich_zaehlt_nur_die_Begegnungen_der_Punktgleichen()
    {
        // A hat gegen den außenstehenden C gewonnen, B gegen A. Zählte der
        // Vergleich auch die Begegnung mit C, stünde A vorn — obwohl B ihn
        // geschlagen hat.
        var a = Record(A, "A");
        var b = Record(B, "B");

        var order = TiebreakerChain.Order(
            [a, b],
            [Tiebreaker.DirectEncounter],
            Context([(A, C), (B, A)]));

        Assert.Equal([B, A], order.Select(r => r.EntryId));
    }

    [Fact]
    public void Buchholz_bevorzugt_den_mit_den_staerkeren_Gegnern()
    {
        var a = Record(A, "A");
        var b = Record(B, "B");

        var order = TiebreakerChain.Order(
            [b, a],
            [Tiebreaker.Buchholz],
            Context(buchholz: new Dictionary<Guid, int> { [A] = 7, [B] = 3 }));

        Assert.Equal([A, B], order.Select(r => r.EntryId));
    }

    [Fact]
    public void Bei_voelliger_Gleichheit_bleibt_die_Reihenfolge_stabil()
    {
        // Ein echtes Los wäre bei jedem Abruf ein anderes — die Tabelle tanzte
        // bei jedem Neuladen. Entschieden wird stattdessen nach Setzung, dann
        // nach Name.
        var a = Record(A, "Zeller, Anna", seed: 3);
        var b = Record(B, "Aigner, Berta", seed: 1);

        var first = TiebreakerChain.Order([a, b], [Tiebreaker.SetRatio, Tiebreaker.Lot], Context());
        var second = TiebreakerChain.Order([b, a], [Tiebreaker.SetRatio, Tiebreaker.Lot], Context());

        Assert.Equal([B, A], first.Select(r => r.EntryId));
        Assert.Equal(first.Select(r => r.EntryId), second.Select(r => r.EntryId));
    }

    [Fact]
    public void Ein_einzelner_Teilnehmer_braucht_keine_Aufloesung()
    {
        var a = Record(A, "A");

        Assert.Equal([A], TiebreakerChain.Order([a], [Tiebreaker.SetRatio], Context()).Select(r => r.EntryId));
    }
}
