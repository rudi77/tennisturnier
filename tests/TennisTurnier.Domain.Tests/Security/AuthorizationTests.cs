using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Domain.Tests.Security;

public sealed class AuthorizationTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid TournamentA = Guid.NewGuid();
    private static readonly Guid TournamentB = Guid.NewGuid();

    private static RoleAssignment Assign(Role role, ResourceScope scope, Guid? userId = null) =>
        new(Guid.NewGuid(), userId ?? User, role, scope);

    private static UserPrincipal PrincipalWith(params RoleAssignment[] assignments) =>
        new(User, assignments);

    [Fact]
    public void Ein_Turnierleiter_darf_sein_Turnier_fuehren()
    {
        var principal = PrincipalWith(Assign(Role.TournamentDirector, ResourceScope.Tournament(TournamentA)));

        Assert.True(principal.Can(Permission.ManageTournament, ResourceScope.Tournament(TournamentA)));
    }

    [Fact]
    public void Ein_Turnierleiter_darf_ein_fremdes_Turnier_nicht_fuehren()
    {
        // Der Kern von ADR-0004: die Rolle allein genügt nicht, der Scope entscheidet.
        var principal = PrincipalWith(Assign(Role.TournamentDirector, ResourceScope.Tournament(TournamentA)));

        Assert.False(principal.Can(Permission.ManageTournament, ResourceScope.Tournament(TournamentB)));
    }

    [Fact]
    public void Ein_Turnierleiter_darf_keine_weiteren_Turniere_anlegen()
    {
        // Turnierleiter ist man für ein Turnier. Wer ausschreiben will, braucht
        // dafür die globale Rolle Organizer — sonst würde die Rolle, die man beim
        // Anlegen bekommt, sich selbst vermehren.
        var principal = PrincipalWith(Assign(Role.TournamentDirector, ResourceScope.Tournament(TournamentA)));

        Assert.False(principal.Can(Permission.CreateTournament, ResourceScope.Global));
    }

    [Fact]
    public void Ein_Veranstalter_darf_anlegen_und_sonst_nichts()
    {
        // Die Rolle, die jeder angemeldete Benutzer bekommt. Sie ist global und
        // trotzdem harmlos, weil sie genau ein Recht trägt.
        var principal = PrincipalWith(Assign(Role.Organizer, ResourceScope.Global));

        Assert.True(principal.Can(Permission.CreateTournament, ResourceScope.Global));
        Assert.False(principal.Can(Permission.ManageTournament, ResourceScope.Tournament(TournamentA)));
        Assert.False(principal.Can(Permission.EnterResults, ResourceScope.Tournament(TournamentA)));
        Assert.False(principal.Can(Permission.ViewInternals, ResourceScope.Tournament(TournamentA)));
    }

    [Fact]
    public void Ein_SystemAdmin_darf_alles_in_jedem_Scope()
    {
        var principal = PrincipalWith(Assign(Role.SystemAdmin, ResourceScope.Global));

        Assert.True(principal.Can(Permission.CreateTournament, ResourceScope.Global));
        Assert.True(principal.Can(Permission.ManageTournament, ResourceScope.Tournament(TournamentB)));
        Assert.True(principal.Can(Permission.EnterResults, ResourceScope.Tournament(TournamentA)));
    }

    [Fact]
    public void Ein_Schiedsrichter_darf_nur_Ergebnisse_eintragen()
    {
        var principal = PrincipalWith(Assign(Role.Referee, ResourceScope.Tournament(TournamentA)));

        Assert.True(principal.Can(Permission.EnterResults, ResourceScope.Tournament(TournamentA)));
        Assert.False(principal.Can(Permission.ManageTournament, ResourceScope.Tournament(TournamentA)));
    }

    [Fact]
    public void Ein_Turnierleiter_darf_die_Ergebnisse_seines_Turniers_eintragen()
    {
        // Ergebnisse trägt der Schiedsrichter ein — und der Turnierleiter, der
        // sie korrigieren können muss, ohne dafür eine zweite Rolle zu brauchen.
        var principal = PrincipalWith(Assign(Role.TournamentDirector, ResourceScope.Tournament(TournamentA)));

        Assert.True(principal.Can(Permission.EnterResults, ResourceScope.Tournament(TournamentA)));
    }

    [Fact]
    public void Ein_Aussenstehender_darf_nichts()
    {
        Assert.False(UserPrincipal.Anonymous.Can(
            Permission.ManageTournament, ResourceScope.Tournament(TournamentA)));
        Assert.False(UserPrincipal.Anonymous.IsAuthenticated);
        Assert.False(UserPrincipal.Anonymous.IsSystemAdmin);
    }

    [Fact]
    public void Ein_angemeldeter_Benutzer_ohne_Rolle_darf_nichts()
    {
        // Seit der Verein entfallen ist, ist das der Normalfall eines frisch
        // angemeldeten Benutzers: er hat ein Konto und sonst nichts.
        var principal = PrincipalWith();

        Assert.True(principal.IsAuthenticated);
        Assert.False(principal.IsSystemAdmin);
        Assert.Empty(principal.TournamentIds);
        Assert.False(principal.Can(Permission.ManageTournament, ResourceScope.Tournament(TournamentA)));
    }

    [Fact]
    public void Der_Systemkontext_umgeht_jede_Pruefung()
    {
        Assert.True(UserPrincipal.System.IsSystemAdmin);
        Assert.True(UserPrincipal.System.Can(Permission.CreateTournament, ResourceScope.Global));
        Assert.True(UserPrincipal.System.Can(
            Permission.ManageTournament, ResourceScope.Tournament(TournamentA)));
    }

    [Fact]
    public void Der_Systemkontext_gilt_nicht_als_angemeldet()
    {
        // Sonst würde er in Audit-Einträgen als Benutzer auftauchen.
        Assert.False(UserPrincipal.System.IsAuthenticated);
    }

    [Fact]
    public void TournamentIds_sammelt_alle_Turniere_mit_Rolle_ohne_Duplikate()
    {
        var principal = PrincipalWith(
            Assign(Role.TournamentDirector, ResourceScope.Tournament(TournamentA)),
            Assign(Role.Referee, ResourceScope.Tournament(TournamentA)),
            Assign(Role.Referee, ResourceScope.Tournament(TournamentB)));

        Assert.Equal(
            new[] { TournamentA, TournamentB }.Order(),
            principal.TournamentIds.Order());
    }

    [Fact]
    public void Require_wirft_bei_fehlender_Berechtigung()
    {
        var principal = PrincipalWith(Assign(Role.Referee, ResourceScope.Tournament(TournamentA)));

        var ex = Assert.Throws<AccessDeniedException>(
            () => principal.Require(Permission.ManageTournament, ResourceScope.Tournament(TournamentA)));

        Assert.Equal(Permission.ManageTournament, ex.Permission);
    }

    [Fact]
    public void Require_laesst_eine_erlaubte_Handlung_durch()
    {
        var principal = PrincipalWith(Assign(Role.TournamentDirector, ResourceScope.Tournament(TournamentA)));

        principal.Require(Permission.ManageTournament, ResourceScope.Tournament(TournamentA));
    }

    [Theory]
    [InlineData(Role.SystemAdmin, ScopeType.Tournament)]
    [InlineData(Role.Organizer, ScopeType.Tournament)]
    [InlineData(Role.Referee, ScopeType.Global)]
    [InlineData(Role.TournamentDirector, ScopeType.Global)]
    public void Eine_Rolle_im_falschen_Scope_wird_abgewiesen(Role role, ScopeType scopeType)
    {
        // Ohne diese Prüfung wäre „Turnierleiter global" speicherbar und damit
        // ein stiller Vollzugriff auf alle Turniere.
        var scope = ResourceScope.Create(scopeType, scopeType == ScopeType.Global ? null : Guid.NewGuid());

        Assert.Throws<DomainException>(() => Assign(role, scope));
    }

    [Fact]
    public void Ein_Scope_ohne_Ressource_ist_ungueltig()
    {
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
        var foreign = Assign(
            Role.TournamentDirector, ResourceScope.Tournament(TournamentA), userId: Guid.NewGuid());

        Assert.Throws<DomainException>(() => new UserPrincipal(User, [foreign]));
    }
}
