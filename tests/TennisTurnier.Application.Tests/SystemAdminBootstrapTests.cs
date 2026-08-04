using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tests.Fakes;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Tests;

/// <summary>
/// Die Randfälle der Einlösung. Die Wirkung auf einen echten Aufruf steht in
/// <c>BootstrapAdminTests</c> der API-Tests; hier geht es um die Entscheidung
/// selbst, die bei jedem Request für jeden Benutzer fällt.
/// </summary>
public sealed class SystemAdminBootstrapTests
{
    private const string Issuer = "https://test.local/realms/tennisturnier";
    private const string Email = "erster.admin@example.invalid";

    private static UserAccount Account(string? email = Email, string subjectId = "sub-1") =>
        new(Guid.NewGuid(), Issuer, subjectId, email, "Erster Admin");

    private static (SystemAdminBootstrap Bootstrap, RecordingUserDirectory Directory) Build(
        params string[] configured)
    {
        var directory = new RecordingUserDirectory();
        var options = new BootstrapAdminOptions { BootstrapSystemAdmins = configured };

        return (new SystemAdminBootstrap(options, directory), directory);
    }

    [Fact]
    public async Task Vergibt_die_globale_Rolle_an_den_konfigurierten_Benutzer()
    {
        var (bootstrap, directory) = Build(Email);
        var account = Account();

        var outcome = await bootstrap.ApplyAsync(account, []);

        Assert.Equal(BootstrapOutcome.Granted, outcome);
        var assignment = Assert.Single(directory.Assigned);
        Assert.Equal(Role.SystemAdmin, assignment.Role);
        Assert.Equal(ResourceScope.Global, assignment.Scope);
        Assert.Equal(account.Id, assignment.UserId);
    }

    [Fact]
    public async Task Ohne_Konfiguration_geschieht_nichts()
    {
        var (bootstrap, directory) = Build();

        Assert.Equal(BootstrapOutcome.NotConfigured, await bootstrap.ApplyAsync(Account(), []));
        Assert.Empty(directory.Assigned);
    }

    [Fact]
    public async Task Wer_die_Rolle_schon_hat_bekommt_keine_zweite()
    {
        var (bootstrap, directory) = Build(Email);
        var account = Account();
        var vorhanden = new RoleAssignment(Guid.NewGuid(), account.Id, Role.SystemAdmin, ResourceScope.Global);

        Assert.Equal(BootstrapOutcome.AlreadyAdmin, await bootstrap.ApplyAsync(account, [vorhanden]));
        Assert.Empty(directory.Assigned);
    }

    [Fact]
    public async Task Eine_andere_Rolle_ersetzt_die_Einlösung_nicht()
    {
        // Sonst bliebe der konfigurierte Administrator auf einer Vereinsrolle
        // sitzen, die er sich nebenbei geholt hat.
        var (bootstrap, directory) = Build(Email);
        var account = Account();
        var vereinsrolle = new RoleAssignment(
            Guid.NewGuid(), account.Id, Role.ClubAdmin, ResourceScope.Club(Guid.NewGuid()));

        Assert.Equal(BootstrapOutcome.Granted, await bootstrap.ApplyAsync(account, [vereinsrolle]));
        Assert.Single(directory.Assigned);
    }

    [Fact]
    public async Task Ein_Konto_ohne_E_Mail_faellt_nicht_auf_einen_leeren_Eintrag_herein()
    {
        // Ein leerer Konfigurationseintrag und eine fehlende E-Mail sind beide
        // „nichts" — sie dürfen einander trotzdem nicht treffen, sonst bekäme
        // der erste beliebige Anmelder die höchste Rolle.
        var (bootstrap, directory) = Build("", "   ");

        Assert.Equal(BootstrapOutcome.NotListed, await bootstrap.ApplyAsync(Account(email: null), []));
        Assert.Empty(directory.Assigned);
    }

    [Fact]
    public async Task Die_Subject_ID_wird_genau_verglichen()
    {
        // Anders als die E-Mail ist sie eine undurchsichtige Kennung; zwei, die
        // sich nur in der Schreibweise unterscheiden, sind zwei.
        var (bootstrap, directory) = Build("SUB-1");

        Assert.Equal(
            BootstrapOutcome.NotListed,
            await bootstrap.ApplyAsync(Account(email: null, subjectId: "sub-1"), []));
        Assert.Empty(directory.Assigned);
    }
}
