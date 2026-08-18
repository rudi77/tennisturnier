using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Tournaments;

/// <summary>
/// Das Satzformat am Turnier.
///
/// Es liegt dort und nicht in der Vorlage, weil es meist nichts über den Modus
/// aussagt, sondern über den Nachmittag: Sätze bis vier und ein Champions-
/// Tiebreak statt des dritten, weil um sechs zugesperrt wird. Diese Tests
/// halten fest, was daraus folgt — es überschreibt die Vorlage beim Einfrieren,
/// auch die Phasenangaben darin, und ab der Auslosung steht es fest.
/// </summary>
public sealed class SatzformatTests
{
    private static readonly Venue Ort = new("TC Test", null, "Maria Alm", "Europe/Vienna");

    private static Tournament NewTournament() => new(
        Guid.NewGuid(),
        "Clubmeisterschaft 2026",
        Ort,
        Discipline.Singles,
        new DateOnly(2026, 5, 16),
        new DateOnly(2026, 5, 17),
        Guid.NewGuid());

    private static Tournament ReadyForDraw()
    {
        var tournament = NewTournament();
        tournament.OpenRegistration();

        for (var i = 0; i < 4; i++)
        {
            var entry = tournament.Enter(Guid.NewGuid(), Guid.NewGuid());
            tournament.Accept(entry.Id);
        }

        tournament.CloseRegistration();
        return tournament;
    }

    /// <summary>Ein kurzes Turnier: Sätze bis vier, Champions-Tiebreak im dritten.</summary>
    private static readonly MatchFormat Kurz =
        new(BestOf: 3, FinalSetMode.MatchTiebreak10, TiebreakAt: 4);

    [Fact]
    public void Ein_neues_Turnier_hat_kein_eigenes_Satzformat()
    {
        Assert.Null(NewTournament().MatchFormat);
    }

    [Fact]
    public void Das_eingestellte_Satzformat_steht_im_eingefrorenen_Format()
    {
        var tournament = ReadyForDraw();
        tournament.ChangeMatchFormat(Kurz);

        tournament.GenerateDraw(BuiltInFormats.RoundRobin, templateVersion: 1);

        Assert.Equal(Kurz, tournament.Format!.Definition.MatchFormat);
    }

    [Fact]
    public void Ohne_eigenes_Satzformat_bleibt_das_der_Vorlage_stehen()
    {
        var tournament = ReadyForDraw();

        tournament.GenerateDraw(BuiltInFormats.RoundRobin, templateVersion: 1);

        Assert.Equal(BuiltInFormats.RoundRobin.MatchFormat, tournament.Format!.Definition.MatchFormat);
    }

    /// <summary>
    /// Eine Vorlage darf je Phase ein eigenes Satzformat mitbringen. Wer am
    /// Turnier Sätze bis vier einstellt, meint sein Turnier und nicht seine
    /// Gruppenphase — ein Halbfinale über volle Sätze wäre die Überraschung,
    /// die niemand nachvollziehen kann.
    /// </summary>
    [Fact]
    public void Es_ueberschreibt_auch_das_Satzformat_einzelner_Phasen()
    {
        var vorlage = BuiltInFormats.GroupThenKnockout with
        {
            Phases =
            [
                BuiltInFormats.GroupThenKnockout.Phases[0] with
                {
                    MatchFormat = new MatchFormat(BestOf: 1, FinalSetMode.Regular, TiebreakAt: 6),
                },
                BuiltInFormats.GroupThenKnockout.Phases[1] with
                {
                    MatchFormat = new MatchFormat(BestOf: 5, FinalSetMode.Advantage, TiebreakAt: 6),
                },
            ],
        };

        var tournament = ReadyForDraw();
        tournament.ChangeMatchFormat(Kurz);

        tournament.GenerateDraw(vorlage, templateVersion: 3);

        var definition = tournament.Format!.Definition;
        Assert.Equal(Kurz, definition.MatchFormat);
        Assert.All(definition.Phases, phase => Assert.Null(phase.MatchFormat));
        Assert.All(definition.Phases, phase => Assert.Equal(Kurz, definition.MatchFormatOf(phase)));
    }

    [Fact]
    public void Die_Vorlage_selbst_bleibt_unberuehrt()
    {
        var tournament = ReadyForDraw();
        tournament.ChangeMatchFormat(Kurz);

        tournament.GenerateDraw(BuiltInFormats.RoundRobin, templateVersion: 1);

        Assert.Equal(6, BuiltInFormats.RoundRobin.MatchFormat.TiebreakAt);
    }

    [Fact]
    public void Es_laesst_sich_wieder_zuruecknehmen()
    {
        var tournament = NewTournament();
        tournament.ChangeMatchFormat(Kurz);

        tournament.ChangeMatchFormat(null);

        Assert.Null(tournament.MatchFormat);
    }

    [Fact]
    public void Ein_ungueltiges_Satzformat_wird_abgewiesen()
    {
        var tournament = NewTournament();

        Assert.Throws<DomainException>(() =>
            tournament.ChangeMatchFormat(new MatchFormat(BestOf: 2)));

        Assert.Throws<DomainException>(() =>
            tournament.ChangeMatchFormat(new MatchFormat(TiebreakAt: 13)));

        Assert.Null(tournament.MatchFormat);
    }

    /// <summary>
    /// Nach der Auslosung hinge an einer Änderung ein bereits eingetragenes
    /// Ergebnis, das gegen die alten Regeln geprüft wurde.
    /// </summary>
    [Fact]
    public void Ab_der_Auslosung_steht_das_Satzformat_fest()
    {
        var tournament = ReadyForDraw();
        tournament.GenerateDraw(BuiltInFormats.RoundRobin, templateVersion: 1);

        var fehler = Assert.Throws<DomainException>(() => tournament.ChangeMatchFormat(Kurz));

        Assert.Contains("Satzformat", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Jede_Aenderung_zaehlt_den_Nebenlaeufigkeitszaehler_hoch()
    {
        var tournament = NewTournament();
        var vorher = tournament.Version;

        tournament.ChangeMatchFormat(Kurz);

        Assert.True(tournament.Version > vorher);
    }
}
