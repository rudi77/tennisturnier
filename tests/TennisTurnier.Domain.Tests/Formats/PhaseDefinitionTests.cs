using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;

namespace TennisTurnier.Domain.Tests.Formats;

/// <summary>
/// Was eine Phasendefinition abweist.
///
/// Sie wird beim Speichern einer Vorlage geprüft und erneut beim Einfrieren in
/// ein Turnier — eine ungültige Definition darf nie in ein laufendes Turnier
/// gelangen. Jede Absage hier ist eine, die sonst erst beim Auslosen auffiele,
/// dann aber mit eingefrorenem Format.
/// </summary>
public sealed class PhaseDefinitionTests
{
    private static PhaseDefinition Phase(
        PhaseFormatKind format = PhaseFormatKind.RoundRobin,
        int ordinal = 1,
        int groupCount = 1,
        int encounters = 1,
        int? rounds = null,
        Qualification? qualification = null,
        ScoringRules? scoring = null,
        IReadOnlyList<Tiebreaker>? tiebreakers = null,
        MatchFormat? matchFormat = null) => new()
    {
        Ordinal = ordinal,
        Format = format,
        GroupCount = groupCount,
        Encounters = encounters,
        Rounds = rounds,
        Qualification = qualification,
        Scoring = scoring!,
        Tiebreakers = tiebreakers ?? [Tiebreaker.SetRatio],
        MatchFormat = matchFormat,
    };

    private static PhaseDefinition Gueltig() => Phase(scoring: new ScoringRules());

    [Fact]
    public void Eine_gueltige_Phase_geht_durch()
    {
        Gueltig().Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ordinal_beginnt_bei_eins(int ordinal)
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(ordinal: ordinal, scoring: new ScoringRules()).Validate());

        Assert.Contains("ordinal muss mindestens 1 sein", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Die_erste_Phase_bezieht_ihre_Teilnehmer_aus_der_Meldeliste()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(
                qualification: new Qualification(1, QualificationRule.All),
                scoring: new ScoringRules()).Validate());

        Assert.Contains("darf keine Qualifikation angeben", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Jede_weitere_Phase_braucht_eine_Qualifikation()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(ordinal: 2, scoring: new ScoringRules()).Validate());

        Assert.Contains("braucht eine Qualifikation", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Qualifikation_zeigt_auf_eine_fruehere_Phase()
    {
        foreach (var fromPhase in new[] { 0, 2, 3 })
        {
            var fehler = Assert.Throws<DomainException>(() =>
                Phase(
                    ordinal: 2,
                    qualification: new Qualification(fromPhase, QualificationRule.TopNPerGroup),
                    scoring: new ScoringRules()).Validate());

            Assert.Contains("auf eine frühere Phase zeigen", fehler.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(QualificationRule.TopNPerGroup)]
    [InlineData(QualificationRule.BestThirds)]
    public void Die_Zahl_der_Qualifikanten_beginnt_bei_eins(QualificationRule regel)
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(
                ordinal: 2,
                qualification: new Qualification(1, regel, N: 0),
                scoring: new ScoringRules()).Validate());

        Assert.Contains("qualification.n", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Alle_Teilnehmer_zu_uebernehmen_braucht_kein_N()
    {
        Phase(
            ordinal: 2,
            qualification: new Qualification(1, QualificationRule.All, N: 0),
            scoring: new ScoringRules()).Validate();
    }

    [Fact]
    public void Ohne_Punktesystem_geht_es_nicht()
    {
        var fehler = Assert.Throws<DomainException>(() => Phase().Validate());

        Assert.Contains("Punktesystem fehlt", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_ungueltiges_Satzformat_wird_durchgereicht()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(matchFormat: new MatchFormat(BestOf: 4), scoring: new ScoringRules()).Validate());

        Assert.Contains("bestOf muss 1, 3 oder 5 sein", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Gruppenphase_braucht_mindestens_eine_Gruppe()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(groupCount: 0, scoring: new ScoringRules()).Validate());

        Assert.Contains("groupCount muss mindestens 1 sein", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mehr_als_sechsundzwanzig_Gruppen_sind_nicht_vorgesehen()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(groupCount: 27, scoring: new ScoringRules()).Validate());

        Assert.Contains("mehr als 26 Gruppen", fehler.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Eine_Gruppe_spielt_Hin_oder_Hin_und_Rueckrunde(int begegnungen)
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(encounters: begegnungen, scoring: new ScoringRules()).Validate());

        Assert.Contains("encounters muss 1 oder 2 sein", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Das_Schweizer_System_braucht_mindestens_eine_Runde()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(PhaseFormatKind.Swiss, rounds: 0, scoring: new ScoringRules()).Validate());

        Assert.Contains("rounds muss mindestens 1 sein", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Das_Schweizer_System_ist_nur_als_erste_Phase_vorgesehen()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(
                PhaseFormatKind.Swiss,
                ordinal: 2,
                rounds: 3,
                qualification: new Qualification(1, QualificationRule.All),
                scoring: new ScoringRules()).Validate());

        Assert.Contains("nur als erste Phase", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Das_Schweizer_System_kennt_keine_Gruppen()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(PhaseFormatKind.Swiss, groupCount: 2, rounds: 3, scoring: new ScoringRules()).Validate());

        Assert.Contains("kennt keine Gruppen", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_KO_Phase_kennt_keine_Gruppen()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(PhaseFormatKind.Knockout, groupCount: 2, scoring: new ScoringRules()).Validate());

        Assert.Contains("eine K.-o.-Phase kennt keine Gruppen", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Tiebreaker_bliebe_Punktgleichheit_unaufloesbar()
    {
        // Beide Formen, in denen eine von Hand geschriebene Definition „keine
        // Kriterien" ausdrücken kann: die leere Liste und gar keine.
        foreach (var kriterien in new IReadOnlyList<Tiebreaker>?[] { null, [] })
        {
            var definition = new PhaseDefinition
            {
                Ordinal = 1,
                Format = PhaseFormatKind.RoundRobin,
                Scoring = new ScoringRules(),
                Tiebreakers = kriterien!,
            };

            var fehler = Assert.Throws<DomainException>(definition.Validate);

            Assert.Contains("unauflösbar", fehler.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ein_Tiebreaker_kommt_nur_einmal_vor()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(
                tiebreakers: [Tiebreaker.SetRatio, Tiebreaker.SetRatio],
                scoring: new ScoringRules()).Validate());

        Assert.Contains("nur einmal vorkommen", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Der_Losentscheid_steht_zuletzt()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            Phase(
                tiebreakers: [Tiebreaker.Lot, Tiebreaker.SetRatio],
                scoring: new ScoringRules()).Validate());

        Assert.Contains("das letzte Kriterium", fehler.Message, StringComparison.Ordinal);

        Phase(
            tiebreakers: [Tiebreaker.SetRatio, Tiebreaker.Lot],
            scoring: new ScoringRules()).Validate();
    }
}
