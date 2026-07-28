namespace TennisTurnier.Domain.Formats;

/// <summary>
/// Die mitgelieferten Standardvorlagen.
///
/// Sie sind zugleich die lesbarste Beschreibung dessen, was ADR-0001 meint: alle
/// vier geforderten Modi entstehen aus denselben Bausteinen, und
/// „Gruppenphase mit anschließendem K.o." ist kein eigenes Format, sondern eine
/// Folge aus zwei Phasen.
/// </summary>
public static class BuiltInFormats
{
    public const string KnockoutId = "ko-single";
    public const string GroupThenKnockoutId = "group-then-ko";
    public const string LeagueId = "liga-round-robin";
    public const string SwissId = "swiss";

    public static FormatDefinition Knockout { get; } = new()
    {
        Id = KnockoutId,
        Name = "K.-o.-System",
        MatchFormat = new MatchFormat(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6),
        Phases =
        [
            new PhaseDefinition
            {
                Ordinal = 1,
                Format = PhaseFormatKind.Knockout,
                Name = "Hauptfeld",
                ThirdPlaceMatch = true,
                Tiebreakers = [Tiebreaker.DirectEncounter],
            },
        ],
    };

    public static FormatDefinition GroupThenKnockout { get; } = new()
    {
        Id = GroupThenKnockoutId,
        Name = "Gruppenphase mit anschließendem K.o.",
        MatchFormat = new MatchFormat(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6),
        Phases =
        [
            new PhaseDefinition
            {
                Ordinal = 1,
                Format = PhaseFormatKind.RoundRobin,
                Name = "Gruppenphase",
                GroupCount = 4,
                Encounters = 1,
                Scoring = new ScoringRules(Win: 2, Loss: 0, Walkover: 0),
                Tiebreakers =
                    [Tiebreaker.DirectEncounter, Tiebreaker.SetRatio, Tiebreaker.GameRatio, Tiebreaker.Lot],
            },
            new PhaseDefinition
            {
                Ordinal = 2,
                Format = PhaseFormatKind.Knockout,
                Name = "Endrunde",
                Qualification = new Qualification(
                    FromPhase: 1,
                    Rule: QualificationRule.TopNPerGroup,
                    N: 2,
                    Seeding: SeedingRule.CrossGroup),
                ThirdPlaceMatch = true,
                Tiebreakers = [Tiebreaker.DirectEncounter],
            },
        ],
    };

    public static FormatDefinition League { get; } = new()
    {
        Id = LeagueId,
        Name = "Liga (jeder gegen jeden, Hin- und Rückrunde)",
        MatchFormat = new MatchFormat(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6),
        Phases =
        [
            new PhaseDefinition
            {
                Ordinal = 1,
                Format = PhaseFormatKind.RoundRobin,
                Name = "Liga",
                GroupCount = 1,
                Encounters = 2,
                Scoring = new ScoringRules(Win: 2, Loss: 0, Walkover: 0),
                Tiebreakers =
                    [Tiebreaker.DirectEncounter, Tiebreaker.SetRatio, Tiebreaker.GameRatio, Tiebreaker.Lot],
            },
        ],
    };

    public static FormatDefinition Swiss { get; } = new()
    {
        Id = SwissId,
        Name = "Schweizer System",
        MatchFormat = new MatchFormat(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 6),
        Phases =
        [
            new PhaseDefinition
            {
                Ordinal = 1,
                Format = PhaseFormatKind.Swiss,
                Name = "Schweizer System",
                Rounds = null,
                Scoring = new ScoringRules(Win: 1, Loss: 0, Walkover: 0),
                Tiebreakers = [Tiebreaker.Buchholz, Tiebreaker.SetRatio, Tiebreaker.GameRatio, Tiebreaker.Lot],
            },
        ],
    };

    public static IReadOnlyList<FormatDefinition> All { get; } =
        [Knockout, GroupThenKnockout, League, Swiss];
}
