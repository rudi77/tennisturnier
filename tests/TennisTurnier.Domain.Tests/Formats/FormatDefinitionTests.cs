using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;

namespace TennisTurnier.Domain.Tests.Formats;

public sealed class FormatDefinitionTests
{
    private static PhaseDefinition Knockout(int ordinal = 1, Qualification? qualification = null) => new()
    {
        Ordinal = ordinal,
        Format = PhaseFormatKind.Knockout,
        Qualification = qualification,
    };

    private static FormatDefinition WithPhases(params PhaseDefinition[] phases) => new()
    {
        Id = "test",
        Name = "Test",
        Phases = phases,
    };

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void Die_mitgelieferten_Vorlagen_sind_gueltig(FormatDefinition definition)
    {
        definition.Validate();
    }

    public static TheoryData<FormatDefinition> BuiltIns()
    {
        var data = new TheoryData<FormatDefinition>();
        foreach (var definition in BuiltInFormats.All)
        {
            data.Add(definition);
        }

        return data;
    }

    [Fact]
    public void Gruppenphase_und_Endrunde_sind_eine_Komposition_und_kein_eigenes_Format()
    {
        // Der Kern von ADR-0001: „Gruppenphase + K.o." ist keine eigene
        // Formatart, sondern eine Folge aus zwei bekannten Phasen.
        var definition = BuiltInFormats.GroupThenKnockout;

        Assert.Equal(2, definition.Phases.Count);
        Assert.Equal(PhaseFormatKind.RoundRobin, definition.Phases[0].Format);
        Assert.Equal(PhaseFormatKind.Knockout, definition.Phases[1].Format);
        Assert.Equal(1, definition.Phases[1].Qualification!.FromPhase);
    }

    [Fact]
    public void Ohne_Phasen_ist_eine_Definition_ungueltig()
    {
        Assert.Throws<DomainException>(() => WithPhases().Validate());
    }

    [Fact]
    public void Luecken_in_der_Phasennummerierung_werden_abgewiesen()
    {
        var definition = WithPhases(
            Knockout(1),
            Knockout(3, new Qualification(1, QualificationRule.All)));

        var ex = Assert.Throws<DomainException>(definition.Validate);
        Assert.Contains("lückenlos", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Die_erste_Phase_darf_keine_Qualifikation_haben()
    {
        // Sie bezieht ihre Teilnehmer aus der Meldeliste. Eine Qualifikation
        // dort verwiese ins Leere.
        var definition = WithPhases(Knockout(1, new Qualification(1, QualificationRule.All)));

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Fact]
    public void Jede_spaetere_Phase_braucht_eine_Qualifikation()
    {
        var definition = WithPhases(Knockout(1), Knockout(2));

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Fact]
    public void Eine_Qualifikation_muss_auf_eine_fruehere_Phase_zeigen()
    {
        var definition = WithPhases(
            Knockout(1),
            Knockout(2, new Qualification(2, QualificationRule.All)));

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void BestOf_muss_ungerade_und_klein_sein(int bestOf)
    {
        var definition = WithPhases(Knockout()) with { MatchFormat = new MatchFormat(BestOf: bestOf) };

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 3)]
    public void SetsToWin_folgt_aus_BestOf(int bestOf, int expected)
    {
        Assert.Equal(expected, new MatchFormat(BestOf: bestOf).SetsToWin);
    }

    [Fact]
    public void Eine_Phase_kann_das_Matchformat_des_Turniers_ueberschreiben()
    {
        // Gruppenspiele auf Match-Tiebreak, das Finale über drei volle Sätze —
        // eine verbreitete Kombination, die ohne Überschreiben zwei Turniere
        // bräuchte.
        var phase = new PhaseDefinition
        {
            Ordinal = 1,
            Format = PhaseFormatKind.Knockout,
            MatchFormat = new MatchFormat(BestOf: 3, FinalSetMode.Regular),
        };
        var definition = WithPhases(phase) with { MatchFormat = new MatchFormat(BestOf: 1) };

        Assert.Equal(FinalSetMode.Regular, definition.MatchFormatOf(phase).FinalSetMode);
        Assert.Equal(3, definition.MatchFormatOf(phase).BestOf);
    }

    [Fact]
    public void Ohne_eigenes_Matchformat_gilt_das_des_Turniers()
    {
        var phase = Knockout();
        var definition = WithPhases(phase) with { MatchFormat = new MatchFormat(BestOf: 5) };

        Assert.Equal(5, definition.MatchFormatOf(phase).BestOf);
    }

    [Fact]
    public void Eine_Phase_ohne_Tiebreaker_ist_ungueltig()
    {
        var definition = WithPhases(new PhaseDefinition
        {
            Ordinal = 1,
            Format = PhaseFormatKind.RoundRobin,
            Tiebreakers = [],
        });

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Fact]
    public void Der_Losentscheid_muss_das_letzte_Kriterium_sein()
    {
        // Steht er davor, sind alle folgenden Kriterien unerreichbar — das Los
        // entscheidet immer.
        var definition = WithPhases(new PhaseDefinition
        {
            Ordinal = 1,
            Format = PhaseFormatKind.RoundRobin,
            Tiebreakers = [Tiebreaker.Lot, Tiebreaker.SetRatio],
        });

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Fact]
    public void Ein_doppelter_Tiebreaker_wird_abgewiesen()
    {
        var definition = WithPhases(new PhaseDefinition
        {
            Ordinal = 1,
            Format = PhaseFormatKind.RoundRobin,
            Tiebreakers = [Tiebreaker.SetRatio, Tiebreaker.SetRatio],
        });

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Fact]
    public void Eine_KO_Phase_kennt_keine_Gruppen()
    {
        var definition = WithPhases(new PhaseDefinition
        {
            Ordinal = 1,
            Format = PhaseFormatKind.Knockout,
            GroupCount = 4,
        });

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void RoundRobin_kennt_nur_Hin_und_Rueckrunde(int encounters)
    {
        var definition = WithPhases(new PhaseDefinition
        {
            Ordinal = 1,
            Format = PhaseFormatKind.RoundRobin,
            Encounters = encounters,
        });

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Theory]
    [InlineData(QualificationRule.TopNPerGroup)]
    [InlineData(QualificationRule.BestThirds)]
    public void Eine_Qualifikantenzahl_unter_eins_wird_abgewiesen(QualificationRule rule)
    {
        // Regression: geprüft wurde nur TopNPerGroup. Eine Definition mit
        // BestThirds und negativem N bestand die Prüfung, ließ sich speichern
        // und wurde beim Auslosen unverändert eingefroren.
        var definition = WithPhases(
            Knockout(1),
            Knockout(2, new Qualification(1, rule, N: -5)));

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Fact]
    public void Eine_Definition_ohne_Phasenliste_wird_abgewiesen_statt_abzustuerzen()
    {
        // Kann aus einer von Hand geschriebenen Definition mit "phases": null
        // entstehen. Ein Absturz wäre eine 500 statt einer verständlichen Absage.
        var definition = BuiltInFormats.Knockout with { Phases = null! };

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Fact]
    public void Eine_Definition_ohne_Matchformat_wird_abgewiesen_statt_abzustuerzen()
    {
        var definition = BuiltInFormats.Knockout with { MatchFormat = null! };

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Fact]
    public void Eine_Phase_ohne_Tiebreakerliste_wird_abgewiesen_statt_abzustuerzen()
    {
        var definition = WithPhases(new PhaseDefinition
        {
            Ordinal = 1,
            Format = PhaseFormatKind.RoundRobin,
            Tiebreakers = null!,
        });

        Assert.Throws<DomainException>(definition.Validate);
    }

    [Fact]
    public void Ein_eigener_Modus_entsteht_ohne_neuen_Code()
    {
        // Die Probe aufs Exempel für ADR-0001: drei Phasen, die es als Vorlage
        // nicht gibt — Gruppen, Zwischenrunde, Endrunde — und die dennoch
        // gültig sind.
        var definition = new FormatDefinition
        {
            Id = "custom-drei-phasen",
            Name = "Gruppen, Zwischenrunde, Endrunde",
            Phases =
            [
                new PhaseDefinition
                {
                    Ordinal = 1,
                    Format = PhaseFormatKind.RoundRobin,
                    GroupCount = 8,
                },
                new PhaseDefinition
                {
                    Ordinal = 2,
                    Format = PhaseFormatKind.RoundRobin,
                    GroupCount = 4,
                    Qualification = new Qualification(1, QualificationRule.TopNPerGroup, N: 2),
                },
                new PhaseDefinition
                {
                    Ordinal = 3,
                    Format = PhaseFormatKind.Knockout,
                    Qualification = new Qualification(2, QualificationRule.TopNPerGroup, N: 2),
                },
            ],
        };

        definition.Validate();
    }
}
