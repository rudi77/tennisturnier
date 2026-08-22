using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tests;

/// <summary>
/// Was der Phasenfortschritt tut, wenn ihm die Grundlage fehlt.
///
/// Er läuft nach jedem Ergebnis und nach jeder Rücknahme, also an der Stelle,
/// an der am Turniertag am meisten los ist. Eine Ausnahme hier legt nicht einen
/// Aufruf lahm, sondern die Ergebniseingabe — deshalb gibt er auf, statt zu
/// werfen: ein Turnier ohne eingefrorenes Format hat keine Phasen, die
/// fortzuschreiben wären, und eine Phase ohne Definition ist nichts, worüber
/// sich rechnen ließe.
/// </summary>
public sealed class PhaseOrchestratorTests
{
    private static readonly Guid TurnierId = Guid.NewGuid();

    private static Tournament Turnier() =>
        new(
            TurnierId,
            "Clubmeisterschaft",
            new Venue("TC Test", null, "Maria Alm", "Europe/Vienna"),
            Discipline.Singles,
            new DateOnly(2026, 5, 16),
            new DateOnly(2026, 5, 17),
            Guid.NewGuid());

    /// <summary>Ein Turnier mit eingefrorenem Format und vier angenommenen Meldungen.</summary>
    private static Tournament Ausgelost(FormatDefinition? definition = null)
    {
        var tournament = Turnier();
        tournament.OpenRegistration();

        for (var i = 0; i < 4; i++)
        {
            var entry = tournament.Enter(Guid.NewGuid(), Guid.NewGuid());
            tournament.Accept(entry.Id);
        }

        tournament.CloseRegistration();
        tournament.GenerateDraw(definition ?? BuiltInFormats.Knockout, templateVersion: 1);

        return tournament;
    }

    private static Phase Phase(int ordinal, PhaseFormatKind format = PhaseFormatKind.Knockout) =>
        new(Guid.NewGuid(), TurnierId, ordinal, format);

    [Fact]
    public void Ohne_eingefrorenes_Format_gibt_es_nichts_fortzuschreiben()
    {
        var tournament = Turnier();

        var verworfen = PhaseOrchestrator.Advance(
            tournament,
            [Phase(1)],
            new Dictionary<Guid, string>(),
            new HashSet<Guid>());

        Assert.Empty(verworfen);

        // Und fertig ist es damit auch nicht — sonst meldete ein frisch
        // angelegtes Turnier sich als abgeschlossen.
        Assert.False(PhaseOrchestrator.IsFinished(tournament, [Phase(1)], new Dictionary<Guid, string>()));
    }

    [Fact]
    public void Ein_Turnier_ohne_Phasen_ist_nicht_fertig()
    {
        Assert.False(PhaseOrchestrator.IsFinished(Ausgelost(), [], new Dictionary<Guid, string>()));
    }

    [Fact]
    public void Eine_Phase_ohne_Definition_wird_uebergangen()
    {
        // Ordinal 9 steht in keiner Definition — etwa nach einem Rückbau des
        // Formats. Die Phase bleibt liegen, statt den Fortschritt anzuhalten.
        var tournament = Ausgelost();

        var verworfen = PhaseOrchestrator.Advance(
            tournament,
            [Phase(9)],
            new Dictionary<Guid, string>(),
            new HashSet<Guid>());

        Assert.Empty(verworfen);
        Assert.False(PhaseOrchestrator.IsFinished(tournament, [Phase(9)], new Dictionary<Guid, string>()));
        Assert.Null(PhaseOrchestrator.DefinitionOf(BuiltInFormats.Knockout, Phase(9)));
    }

    [Fact]
    public void Eine_Qualifikation_ohne_Vorphase_besetzt_nichts()
    {
        // Die zweite Phase beruft sich auf eine erste, die nicht angelegt wurde.
        // Sie bleibt unbesetzt — und das ist der vorsichtige Fehler: ein Bracket
        // aus einer Tabelle, die es nicht gibt, wäre die Alternative.
        var definition = new FormatDefinition
        {
            Id = "gruppen-dann-ko",
            Name = "Gruppen, dann K.o.",
            Phases =
            [
                new PhaseDefinition { Ordinal = 1, Format = PhaseFormatKind.RoundRobin, GroupCount = 1 },
                new PhaseDefinition
                {
                    Ordinal = 2,
                    Format = PhaseFormatKind.Knockout,
                    Qualification = new Qualification(1, QualificationRule.TopNPerGroup, N: 2),
                },
            ],
        };

        var tournament = Ausgelost(definition);

        // Die Endrunde steht schon — nur ihre Vorphase ist nicht dabei.
        var endrunde = Phase(2);
        endrunde.AddPairings([
            new Pairing(1, 1, ParticipantRef.Open, ParticipantRef.Open, "Finale"),
        ]);

        var verworfen = PhaseOrchestrator.Advance(
            tournament,
            [endrunde],
            new Dictionary<Guid, string>(),
            new HashSet<Guid>());

        Assert.Empty(verworfen);
        Assert.All(endrunde.Matches, match => Assert.False(match.Side1.Origin.IsResolved));
    }

    [Fact]
    public void Ein_Match_ohne_Etikett_heisst_nach_Runde_und_Position()
    {
        var phase = Phase(1, PhaseFormatKind.RoundRobin);
        var matches = phase.AddPairings([
            new Pairing(1, 1, ParticipantRef.Of(Guid.NewGuid()), ParticipantRef.Of(Guid.NewGuid())),
            new Pairing(2, 3, ParticipantRef.Of(Guid.NewGuid()), ParticipantRef.Of(Guid.NewGuid())),
        ]);

        var etiketten = MatchOrigins.LabelsOf(matches);

        Assert.Equal("Runde 1, Match 1", etiketten[matches[0].Id]);
        Assert.Equal("Runde 2, Match 3", etiketten[matches[1].Id]);
    }

    [Fact]
    public void Gleiche_Etiketten_werden_durchnummeriert()
    {
        var phase = Phase(1);
        var matches = phase.AddPairings([
            new Pairing(1, 1, ParticipantRef.Of(Guid.NewGuid()), ParticipantRef.Of(Guid.NewGuid()), "Halbfinale"),
            new Pairing(1, 2, ParticipantRef.Of(Guid.NewGuid()), ParticipantRef.Of(Guid.NewGuid()), "Halbfinale"),
            new Pairing(2, 1, ParticipantRef.Open, ParticipantRef.Open, "Finale"),
        ]);

        var etiketten = MatchOrigins.LabelsOf(matches);

        Assert.Equal("Halbfinale 1", etiketten[matches[0].Id]);
        Assert.Equal("Halbfinale 2", etiketten[matches[1].Id]);
        Assert.Equal("Finale", etiketten[matches[2].Id]);
    }

    [Fact]
    public void Eine_offene_Seite_heisst_offen()
    {
        var etiketten = new Dictionary<Guid, string>();

        Assert.Equal("offen", MatchOrigins.Describe(ParticipantRef.Open, etiketten));
        Assert.Equal("Freilos", MatchOrigins.Describe(ParticipantRef.ByeSlot, etiketten));
        Assert.Equal("gesetzt", MatchOrigins.Describe(ParticipantRef.Of(Guid.NewGuid()), etiketten));

        // Ohne bekanntes Vorspiel bleibt die Beschreibung trotzdem lesbar.
        Assert.Equal(
            "Sieger aus einem Vorspiel",
            MatchOrigins.Describe(ParticipantRef.FromWinnerOf(Guid.NewGuid()), etiketten));
    }
}
