using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Tests.Formats;

public sealed class FormatTemplateTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static FormatTemplate EigeneVorlage(FormatDefinition? definition = null) =>
        new(Guid.NewGuid(), OwnerId, definition ?? BuiltInFormats.Knockout);

    private static FormatTemplate BuiltIn() =>
        new(Guid.NewGuid(), ownerUserId: null, BuiltInFormats.Knockout);

    [Fact]
    public void Eine_neue_Vorlage_hat_Version_eins()
    {
        Assert.Equal(1, EigeneVorlage().Version);
    }

    [Fact]
    public void Eine_ungueltige_Definition_wird_beim_Anlegen_abgewiesen()
    {
        var invalid = BuiltInFormats.Knockout with { Phases = [] };

        Assert.Throws<DomainException>(() => new FormatTemplate(Guid.NewGuid(), OwnerId, invalid));
    }

    [Fact]
    public void Eine_Aenderung_zaehlt_die_Version_hoch()
    {
        var template = EigeneVorlage();

        template.Update(BuiltInFormats.League);

        Assert.Equal(2, template.Version);
        Assert.Equal(BuiltInFormats.LeagueId, template.Definition.Id);
    }

    [Fact]
    public void Eine_ungueltige_Definition_wird_auch_beim_Aendern_abgewiesen()
    {
        var template = EigeneVorlage();

        Assert.Throws<DomainException>(() => template.Update(BuiltInFormats.Knockout with { Phases = [] }));
        Assert.Equal(1, template.Version);
    }

    [Fact]
    public void Eine_mitgelieferte_Vorlage_laesst_sich_nicht_aendern()
    {
        var builtIn = BuiltIn();

        var ex = Assert.Throws<DomainException>(() => builtIn.Update(BuiltInFormats.League));
        Assert.Contains("Kopie", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Kopie_gehoert_ihrem_Anleger_und_ist_aenderbar()
    {
        var copy = BuiltIn().CopyFor(Guid.NewGuid(), OwnerId, "  Hausformat  ");

        Assert.False(copy.IsBuiltIn);
        Assert.Equal("Hausformat", copy.Name);

        copy.Update(BuiltInFormats.League);
        Assert.Equal(2, copy.Version);
    }

    [Fact]
    public void Eine_Aenderung_an_der_Vorlage_beruehrt_ein_laufendes_Turnier_nicht()
    {
        // Der eigentliche Zweck des eingefrorenen Snapshots (ADR-0001): wer die
        // Vorlage nachschärft, während ein Turnier läuft, darf dessen Regeln
        // nicht rückwirkend verändern.
        var template = EigeneVorlage(BuiltInFormats.Knockout);
        var tournament = TournamentReadyForDraw(template.Id);

        tournament.GenerateDraw(template.Definition, template.Version);
        var frozen = tournament.Format!;

        template.Update(BuiltInFormats.League with { Name = "Ganz anderes Format" });

        Assert.Equal(BuiltInFormats.KnockoutId, tournament.Format!.Definition.Id);
        Assert.Equal(1, frozen.TemplateVersion);
        Assert.Equal(2, template.Version);
    }

    [Fact]
    public void Der_Snapshot_haelt_fest_aus_welchem_Stand_er_stammt()
    {
        var template = EigeneVorlage();
        template.Update(BuiltInFormats.League);
        var tournament = TournamentReadyForDraw(template.Id);

        tournament.GenerateDraw(template.Definition, template.Version);

        Assert.Equal(template.Id, tournament.Format!.TemplateId);
        Assert.Equal(2, tournament.Format.TemplateVersion);
    }

    private static Tournament TournamentReadyForDraw(Guid templateId)
    {
        var tournament = new Tournament(
            Guid.NewGuid(),
            "Clubmeisterschaft",
            new Venue("TC Test", null, "Maria Alm", "Europe/Vienna"),
            Discipline.Singles,
            new DateOnly(2026, 5, 16),
            new DateOnly(2026, 5, 17),
            templateId);

        tournament.OpenRegistration();
        for (var i = 0; i < 2; i++)
        {
            var entry = tournament.Enter(Guid.NewGuid(), Guid.NewGuid());
            tournament.Accept(entry.Id);
        }

        tournament.CloseRegistration();
        return tournament;
    }

    [Fact]
    public void Eine_Kopie_braucht_Eigentuemer_und_Namen()
    {
        // Eine Kopie ohne Eigentümer wäre eine zweite mitgelieferte Vorlage —
        // niemand dürfte sie ändern, und niemand könnte sie loswerden.
        var vorlage = BuiltIn();

        var ohneEigentuemer = Assert.Throws<DomainException>(() =>
            vorlage.CopyFor(Guid.NewGuid(), Guid.Empty, "Meine Fassung"));

        Assert.Contains("braucht einen Eigentümer", ohneEigentuemer.Message, StringComparison.Ordinal);

        var ohneNamen = Assert.Throws<DomainException>(() =>
            vorlage.CopyFor(Guid.NewGuid(), OwnerId, "   "));

        Assert.Contains("braucht einen Namen", ohneNamen.Message, StringComparison.Ordinal);
    }
}
