using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Tests.Matches;

/// <summary>
/// Die Begegnung als Aggregat: was sie beim Aufbau verlangt und was sie
/// ablehnt, sobald sie entschieden ist.
///
/// Die Absagen sind der eigentliche Inhalt. Ein Match, das sich nach dem
/// Ergebnis noch umbesetzen ließe, verschöbe stillschweigend, wer eine Runde
/// weiter ist — und niemand sähe es dem Baum an.
/// </summary>
public sealed class MatchTests
{
    private static readonly Guid Turnier = Guid.NewGuid();
    private static readonly Guid PhaseId = Guid.NewGuid();

    private static Match Bauen(
        ParticipantRef? seite1 = null,
        ParticipantRef? seite2 = null,
        int runde = 1,
        int position = 1,
        string? label = null,
        string? gruppe = null) =>
        new(
            Guid.NewGuid(),
            Turnier,
            PhaseId,
            runde,
            position,
            seite1 ?? ParticipantRef.Of(Guid.NewGuid()),
            seite2 ?? ParticipantRef.Of(Guid.NewGuid()),
            label,
            gruppe);

    private static Score Ergebnis() =>
        Score.Played([new SetScore(6, 4), new SetScore(6, 3)], new MatchFormat());

    [Fact]
    public void Braucht_Turnier_und_Phase()
    {
        var ohneTurnier = Assert.Throws<DomainException>(() =>
            new Match(Guid.NewGuid(), Guid.Empty, PhaseId, 1, 1, ParticipantRef.Open, ParticipantRef.Open));
        Assert.Contains("Turnier und Phase", ohneTurnier.Message, StringComparison.Ordinal);

        Assert.Throws<DomainException>(() =>
            new Match(Guid.NewGuid(), Turnier, Guid.Empty, 1, 1, ParticipantRef.Open, ParticipantRef.Open));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Eine_Runde_beginnt_bei_eins(int runde)
    {
        var fehler = Assert.Throws<DomainException>(() => Bauen(runde: runde));

        Assert.Contains($"war {runde}", fehler.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Eine_Position_beginnt_bei_eins(int position)
    {
        var fehler = Assert.Throws<DomainException>(() => Bauen(position: position));

        Assert.Contains("Position", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verlangt_beide_Seiten()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new Match(Guid.NewGuid(), Turnier, PhaseId, 1, 1, null!, ParticipantRef.Open));
        Assert.Throws<ArgumentNullException>(() =>
            new Match(Guid.NewGuid(), Turnier, PhaseId, 1, 1, ParticipantRef.Open, null!));
    }

    [Fact]
    public void Beschriftung_und_Gruppe_werden_beschnitten_oder_entfallen()
    {
        var mit = Bauen(label: "  Finale  ", gruppe: "  A  ");
        Assert.Equal("Finale", mit.Label);
        Assert.Equal("A", mit.Group);

        var ohne = Bauen(label: "   ", gruppe: "");
        Assert.Null(ohne.Label);
        Assert.Null(ohne.Group);
    }

    [Fact]
    public void Steht_auf_Pending_solange_eine_Seite_offen_ist()
    {
        var match = Bauen(seite2: ParticipantRef.Open);

        Assert.Equal(MatchStatus.Pending, match.Status);
        Assert.Null(match.WinnerEntryId);
        Assert.Null(match.LoserEntryId);
    }

    [Fact]
    public void Nennt_Sieger_und_Verlierer_erst_mit_dem_Ergebnis()
    {
        var eins = Guid.NewGuid();
        var zwei = Guid.NewGuid();
        var match = Bauen(ParticipantRef.Of(eins), ParticipantRef.Of(zwei));

        match.RecordResult(Ergebnis());

        Assert.Equal(MatchStatus.Finished, match.Status);
        Assert.Equal(eins, match.WinnerEntryId);
        Assert.Equal(zwei, match.LoserEntryId);
    }

    [Fact]
    public void Ein_entschiedenes_Match_laesst_sich_nicht_mehr_umbauen()
    {
        var match = Bauen();
        match.RecordResult(Ergebnis());

        var fehler = Assert.Throws<DomainException>(() =>
            match.SetOrigin(1, ParticipantRef.FromWinnerOf(Guid.NewGuid())));

        Assert.Contains("nicht mehr umbauen", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Setzt_die_Herkunft_einer_Seite_solange_nichts_entschieden_ist()
    {
        var match = Bauen(seite1: ParticipantRef.Open);
        var vorgaenger = Guid.NewGuid();

        match.SetOrigin(1, ParticipantRef.FromWinnerOf(vorgaenger));

        Assert.Equal(vorgaenger, match.Side1.Origin.DependsOnMatch);
        Assert.Throws<ArgumentNullException>(() => match.SetOrigin(1, null!));
    }

    [Fact]
    public void Eine_aufgeloeste_Seite_braucht_eine_Meldung()
    {
        var match = Bauen(seite1: ParticipantRef.FromWinnerOf(Guid.NewGuid()));

        var fehler = Assert.Throws<DomainException>(() => match.Resolve(1, Guid.Empty));

        Assert.Contains("braucht eine Meldung", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_entschiedenes_Match_laesst_sich_nicht_umbesetzen()
    {
        var match = Bauen();
        match.RecordResult(Ergebnis());

        var fehler = Assert.Throws<DomainException>(() => match.Resolve(1, Guid.NewGuid()));

        Assert.Contains("zuerst das Ergebnis zurückzunehmen", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dieselbe_Meldung_zweimal_aufzuloesen_aendert_nichts()
    {
        var match = Bauen(seite1: ParticipantRef.FromWinnerOf(Guid.NewGuid()));
        var meldung = Guid.NewGuid();

        Assert.True(match.Resolve(1, meldung));
        var version = match.Version;

        Assert.False(match.Resolve(1, meldung));
        Assert.Equal(version, match.Version);
    }

    [Fact]
    public void Nimmt_eine_Aufloesung_zurueck_und_behaelt_die_Herkunft()
    {
        var vorgaenger = Guid.NewGuid();
        var match = Bauen(seite1: ParticipantRef.FromWinnerOf(vorgaenger));
        match.Resolve(1, Guid.NewGuid());

        Assert.True(match.Unresolve(1));

        Assert.Null(match.Side1.EntryId);
        Assert.Equal(vorgaenger, match.Side1.Origin.DependsOnMatch);
    }

    [Fact]
    public void Eine_offene_Seite_zurueckzunehmen_aendert_nichts()
    {
        var match = Bauen(seite1: ParticipantRef.FromWinnerOf(Guid.NewGuid()));

        Assert.False(match.Unresolve(1));
    }

    [Fact]
    public void Eine_von_Anfang_an_feststehende_Seite_bleibt_stehen()
    {
        // Ihre Herkunft *ist* die Meldung — sie zurückzunehmen hieße, sie zu
        // löschen, und dafür gibt es den Weg über den Draw.
        var match = Bauen();

        Assert.False(match.Unresolve(1));
        Assert.NotNull(match.Side1.EntryId);
    }

    [Fact]
    public void Eine_Seite_ist_eins_oder_zwei()
    {
        var match = Bauen(seite1: ParticipantRef.FromWinnerOf(Guid.NewGuid()));

        var fehler = Assert.Throws<DomainException>(() => match.Resolve(3, Guid.NewGuid()));

        Assert.Contains("war 3", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Setzt_auch_die_zweite_Seite()
    {
        var match = Bauen(seite2: ParticipantRef.FromWinnerOf(Guid.NewGuid()));
        var meldung = Guid.NewGuid();

        Assert.True(match.Resolve(2, meldung));
        Assert.Equal(meldung, match.Side2.EntryId);
    }

    [Fact]
    public void Ohne_feststehende_Teilnehmer_gibt_es_kein_Ergebnis()
    {
        var match = Bauen(seite2: ParticipantRef.Open);

        var fehler = Assert.Throws<DomainException>(() => match.RecordResult(Ergebnis()));

        Assert.Contains("noch nicht feststehen", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_Freilos_bekommt_kein_regulaeres_Ergebnis()
    {
        var match = Bauen(seite2: ParticipantRef.ByeSlot);

        var fehler = Assert.Throws<DomainException>(() => match.RecordResult(Ergebnis()));

        Assert.Contains("wird nicht gespielt", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_Freilos_setzt_voraus_dass_eine_Seite_frei_ist()
    {
        var match = Bauen();

        var fehler = Assert.Throws<DomainException>(() => match.RecordResult(Score.ByeFor(1)));

        Assert.Contains("dass eine Seite frei ist", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_Freilos_kann_nicht_gewinnen()
    {
        var match = Bauen(seite2: ParticipantRef.ByeSlot);

        var fehler = Assert.Throws<DomainException>(() => match.RecordResult(Score.ByeFor(2)));

        Assert.Contains("kann nicht gewinnen", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_Freilos_traegt_den_Gegner_weiter()
    {
        var meldung = Guid.NewGuid();
        var match = Bauen(ParticipantRef.Of(meldung), ParticipantRef.ByeSlot);

        match.RecordResult(Score.ByeFor(1));

        Assert.True(match.HasBye);
        Assert.Equal(meldung, match.WinnerEntryId);
    }

    [Fact]
    public void Verlangt_ein_Ergebnis()
    {
        Assert.Throws<ArgumentNullException>(() => Bauen().RecordResult(null!));
    }

    [Fact]
    public void Ein_Ergebnis_zurueckzunehmen_das_es_nicht_gibt_aendert_nichts()
    {
        var match = Bauen();
        var version = match.Version;

        match.ClearResult();

        Assert.Null(match.Score);
        Assert.Equal(version, match.Version);
    }

    [Fact]
    public void Nimmt_ein_Ergebnis_zurueck()
    {
        var match = Bauen();
        match.RecordResult(Ergebnis());

        match.ClearResult();

        Assert.Null(match.Score);
        Assert.Equal(MatchStatus.Ready, match.Status);
    }

    [Fact]
    public void Nennt_sich_mit_Beschriftung_Runde_und_Stand()
    {
        var ohneLabel = Bauen(runde: 2, position: 3);
        Assert.StartsWith("R2/3:", ohneLabel.ToString(), StringComparison.Ordinal);

        var match = Bauen(label: "Finale");
        Assert.StartsWith("Finale:", match.ToString(), StringComparison.Ordinal);

        match.RecordResult(Ergebnis());
        Assert.Contains("(6:4, 6:3)", match.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Seite_nennt_ihre_Herkunft_und_die_aufgeloeste_Meldung()
    {
        var vorgaenger = Guid.NewGuid();
        var match = Bauen(seite1: ParticipantRef.FromWinnerOf(vorgaenger));
        var meldung = Guid.NewGuid();
        match.Resolve(1, meldung);

        Assert.Equal($"Sieger aus {vorgaenger} → {meldung}", match.Side1.ToString());

        // Eine von Anfang an feststehende Seite nennt nur ihre Meldung — der
        // Pfeil auf sich selbst wäre eine Wiederholung.
        Assert.Equal($"Meldung {match.Side2.EntryId}", match.Side2.ToString());
    }
}
