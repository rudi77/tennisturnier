using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Domain.Tests.Security;

public sealed class AuthorizationTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid ClubA = Guid.NewGuid();
    private static readonly Guid ClubB = Guid.NewGuid();
    private static readonly Guid Tournament = Guid.NewGuid();

    private static RoleAssignment Assign(Role role, ResourceScope scope, Guid? userId = null) =>
        new(Guid.NewGuid(), userId ?? User, role, scope);

    private static UserPrincipal PrincipalWith(params RoleAssignment[] assignments) =>
        new(User, assignments);

    [Fact]
    public void Ein_ClubAdmin_darf_die_Plaetze_seines_Vereins_pflegen()
    {
        var principal = PrincipalWith(Assign(Role.ClubAdmin, ResourceScope.Club(ClubA)));

        Assert.True(principal.Can(Permission.ManageCourts, ResourceScope.Club(ClubA)));
    }

    [Fact]
    public void Ein_ClubAdmin_darf_die_Plaetze_eines_fremden_Vereins_nicht_pflegen()
    {
        // Der Kern von ADR-0004: die Rolle allein genügt nicht, der Scope entscheidet.
        var principal = PrincipalWith(Assign(Role.ClubAdmin, ResourceScope.Club(ClubA)));

        Assert.False(principal.Can(Permission.ManageCourts, ResourceScope.Club(ClubB)));
    }

    [Fact]
    public void Ein_ClubAdmin_darf_keine_Vereine_anlegen()
    {
        var principal = PrincipalWith(Assign(Role.ClubAdmin, ResourceScope.Club(ClubA)));

        Assert.False(principal.Can(Permission.ManageClubs, ResourceScope.Global));
    }

    [Fact]
    public void Ein_SystemAdmin_darf_alles_in_jedem_Scope()
    {
        var principal = PrincipalWith(Assign(Role.SystemAdmin, ResourceScope.Global));

        Assert.True(principal.Can(Permission.ManageClubs, ResourceScope.Global));
        Assert.True(principal.Can(Permission.ManageCourts, ResourceScope.Club(ClubB)));
        Assert.True(principal.Can(Permission.EnterResults, ResourceScope.Tournament(Tournament)));
    }

    [Fact]
    public void Ein_Schiedsrichter_darf_nur_Ergebnisse_eintragen()
    {
        var principal = PrincipalWith(Assign(Role.Referee, ResourceScope.Tournament(Tournament)));

        Assert.True(principal.Can(Permission.EnterResults, ResourceScope.Tournament(Tournament)));
        Assert.False(principal.Can(Permission.ManageTournament, ResourceScope.Tournament(Tournament)));
    }

    [Fact]
    public void Der_Vereinsadministrator_darf_Ergebnisse_seines_Turniers_eintragen()
    {
        // Die Aufrufstelle gibt beide Wege an, statt die Rangfolge selbst zu kennen:
        // Schiedsrichter des Turniers ODER Administrator des ausrichtenden Vereins.
        var principal = PrincipalWith(Assign(Role.ClubAdmin, ResourceScope.Club(ClubA)));

        Assert.True(principal.Can(
            Permission.EnterResults,
            ResourceScope.Tournament(Tournament),
            ResourceScope.Club(ClubA)));
    }

    [Fact]
    public void Ein_Aussenstehender_darf_nichts()
    {
        Assert.False(UserPrincipal.Anonymous.Can(Permission.ManageCourts, ResourceScope.Club(ClubA)));
        Assert.False(UserPrincipal.Anonymous.IsAuthenticated);
        Assert.False(UserPrincipal.Anonymous.IsSystemAdmin);
    }

    [Fact]
    public void Der_Systemkontext_umgeht_jede_Pruefung()
    {
        Assert.True(UserPrincipal.System.IsSystemAdmin);
        Assert.True(UserPrincipal.System.Can(Permission.ManageClubs, ResourceScope.Global));
        Assert.True(UserPrincipal.System.Can(Permission.ManageCourts, ResourceScope.Club(ClubA)));
    }

    [Fact]
    public void Der_Systemkontext_gilt_nicht_als_angemeldet()
    {
        // Sonst würde er in Audit-Einträgen als Benutzer auftauchen.
        Assert.False(UserPrincipal.System.IsAuthenticated);
    }

    [Fact]
    public void ClubIds_sammelt_alle_Vereine_mit_Rolle_ohne_Duplikate()
    {
        var principal = PrincipalWith(
            Assign(Role.ClubAdmin, ResourceScope.Club(ClubA)),
            Assign(Role.Player, ResourceScope.Club(ClubA)),
            Assign(Role.Player, ResourceScope.Club(ClubB)),
            Assign(Role.Referee, ResourceScope.Tournament(Tournament)));

        Assert.Equal(
            new[] { ClubA, ClubB }.Order(),
            principal.ClubIds.Order());
    }

    [Fact]
    public void Require_wirft_bei_fehlender_Berechtigung()
    {
        var principal = PrincipalWith(Assign(Role.Referee, ResourceScope.Tournament(Tournament)));

        var ex = Assert.Throws<AccessDeniedException>(
            () => principal.Require(Permission.ManageTournament, ResourceScope.Tournament(Tournament)));

        Assert.Equal(Permission.ManageTournament, ex.Permission);
    }

    [Fact]
    public void Require_laesst_eine_erlaubte_Handlung_durch()
    {
        var principal = PrincipalWith(Assign(Role.ClubAdmin, ResourceScope.Club(ClubA)));

        principal.Require(Permission.ManageCourts, ResourceScope.Club(ClubA));
    }

    [Theory]
    [InlineData(Role.SystemAdmin, ScopeType.Club)]
    [InlineData(Role.ClubAdmin, ScopeType.Global)]
    [InlineData(Role.ClubAdmin, ScopeType.Tournament)]
    [InlineData(Role.Referee, ScopeType.Club)]
    [InlineData(Role.TournamentDirector, ScopeType.Club)]
    public void Eine_Rolle_im_falschen_Scope_wird_abgewiesen(Role role, ScopeType scopeType)
    {
        // Ohne diese Prüfung wäre „ClubAdmin global" speicherbar und damit ein
        // stiller Vollzugriff auf alle Vereine.
        var scope = ResourceScope.Create(scopeType, scopeType == ScopeType.Global ? null : Guid.NewGuid());

        Assert.Throws<DomainException>(() => Assign(role, scope));
    }

    [Fact]
    public void Ein_Scope_ohne_Ressource_ist_ungueltig()
    {
        Assert.Throws<DomainException>(() => ResourceScope.Club(Guid.Empty));
        Assert.Throws<DomainException>(() => ResourceScope.Tournament(Guid.Empty));
    }

    [Fact]
    public void Ein_globaler_Scope_darf_keine_Ressource_benennen()
    {
        Assert.Throws<DomainException>(() => ResourceScope.Create(ScopeType.Global, Guid.NewGuid()));
    }

    [Fact]
    public void Rollenzuweisungen_fremder_Benutzer_werden_abgewiesen()
    {
        var foreign = Assign(Role.ClubAdmin, ResourceScope.Club(ClubA), userId: Guid.NewGuid());

        Assert.Throws<DomainException>(() => new UserPrincipal(User, [foreign]));
    }
}
