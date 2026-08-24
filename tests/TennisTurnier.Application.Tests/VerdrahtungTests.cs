using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Application.Tests.Fakes;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Tests;

/// <summary>
/// Die Verdrahtung der Anwendungsschicht und der Selbstservice für
/// Veranstalter.
///
/// Beides sind Schalter, die genau einmal umgelegt werden und danach über jedes
/// Verhalten der Instanz entscheiden: ob eine Instanz offen läuft oder
/// geschlossen, und ob überhaupt jemand ein Turnier anlegen kann.
/// </summary>
public sealed class VerdrahtungTests
{
    private static UserAccount Konto() =>
        new(Guid.NewGuid(), "https://test.local", $"sub-{Guid.NewGuid():N}", "wer@example.invalid", "Wer");

    [Fact]
    public void Ohne_Angabe_gibt_es_keine_konfigurierten_Administratoren()
    {
        // Der Normalfall einer Instanz, die nicht gerade aufgesetzt wird: keine
        // Liste in der Konfiguration, und trotzdem müssen die Voreinstellungen
        // stehen — sonst startet die Anwendung gar nicht.
        var services = new ServiceCollection();
        services.AddApplication();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BootstrapAdminOptions>();

        Assert.Empty(options.BootstrapSystemAdmins);
        Assert.True(options.SelfServiceOrganizers);

        // Und das Los der Teams ist echter Zufall, solange niemand einen
        // Saatwert setzt.
        Assert.Null(provider.GetRequiredService<TournamentOptions>().TeamDrawSeed);
    }

    [Fact]
    public async Task Eine_geschlossene_Instanz_vergibt_die_Rolle_nicht()
    {
        // Wer den Selbstservice abschaltet, will genau das: Turniere legt nur
        // an, wen ein Systemadministrator dazu berufen hat.
        var directory = new RecordingUserDirectory();
        var bootstrap = new OrganizerBootstrap(
            new BootstrapAdminOptions { SelfServiceOrganizers = false },
            directory);

        Assert.False(await bootstrap.ApplyAsync(Konto(), []));
        Assert.Empty(directory.Assigned);
    }

    [Fact]
    public async Task Eine_offene_Instanz_vergibt_sie_genau_einmal()
    {
        var directory = new RecordingUserDirectory();
        var bootstrap = new OrganizerBootstrap(new BootstrapAdminOptions(), directory);
        var konto = Konto();

        Assert.True(await bootstrap.ApplyAsync(konto, []));
        var vergeben = Assert.Single(directory.Assigned);
        Assert.Equal(Role.Organizer, vergeben.Role);

        // Beim nächsten Request steht sie schon da — und wird nicht erneut
        // geschrieben, sonst läge bei jedem Aufruf ein Schreibversuch an.
        Assert.False(await bootstrap.ApplyAsync(konto, [vergeben]));
        Assert.Single(directory.Assigned);
    }
}
