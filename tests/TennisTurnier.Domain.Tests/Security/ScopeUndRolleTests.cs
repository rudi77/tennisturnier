using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Domain.Tests.Security;

/// <summary>
/// Die Ränder der Rollenzuweisung (ADR-0004).
///
/// Der Scope ist hier der wunde Punkt: eine Rolle im falschen Scope ist kein
/// harmloser Tippfehler, sondern entweder ein Turnierleiter ohne Turnier — der
/// dann in jedem eines wäre — oder ein Systemadministrator, der nur in einem
/// einzigen Turnier gilt. Beides wird beim Anlegen abgewiesen, nicht erst beim
/// Prüfen.
/// </summary>
public sealed class ScopeUndRolleTests
{
    [Fact]
    public void Ein_globaler_Scope_benennt_keine_Ressource()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            ResourceScope.Create(ScopeType.Global, Guid.NewGuid()));

        Assert.Contains("darf keine Ressource benennen", fehler.Message, StringComparison.Ordinal);

        // Die leere Guid zählt als „keine" und ergibt den globalen Scope.
        Assert.Equal(ResourceScope.Global, ResourceScope.Create(ScopeType.Global, Guid.Empty));
        Assert.Equal(ResourceScope.Global, ResourceScope.Create(ScopeType.Global, null));
    }

    [Fact]
    public void Ein_Turnierscope_ohne_Turnier_ist_keiner()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            ResourceScope.Create(ScopeType.Tournament, null));

        Assert.Contains("braucht eine Ressource", fehler.Message, StringComparison.Ordinal);

        Assert.Throws<DomainException>(() => ResourceScope.Create(ScopeType.Tournament, Guid.Empty));
    }

    [Fact]
    public void Ein_Scope_nennt_sich_lesbar()
    {
        var turnier = Guid.NewGuid();

        Assert.Equal("Global", ResourceScope.Global.ToString());
        Assert.Equal($"Tournament:{turnier}", ResourceScope.Tournament(turnier).ToString());
    }

    [Fact]
    public void Eine_Rollenzuweisung_braucht_einen_Benutzer()
    {
        var fehler = Assert.Throws<DomainException>(() =>
            new RoleAssignment(Guid.NewGuid(), Guid.Empty, Role.Organizer, ResourceScope.Global));

        Assert.Contains("braucht einen Benutzer", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_unbekannte_Rolle_hat_keinen_Scope_und_keine_Rechte()
    {
        // Aus der Ablage kann eine Rolle kommen, die diese Fassung nicht kennt.
        // Sie darf dann nichts — und sie lässt sich auch nicht zuweisen.
        Assert.Empty(Permissions.Of((Role)99));

        var fehler = Assert.Throws<DomainException>(() =>
            new RoleAssignment(Guid.NewGuid(), Guid.NewGuid(), (Role)99, ResourceScope.Global));

        Assert.Contains("Unbekannte Rolle", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Zuweisung_nennt_Rolle_und_Scope()
    {
        var turnier = Guid.NewGuid();
        var zuweisung = new RoleAssignment(
            Guid.NewGuid(), Guid.NewGuid(), Role.Referee, ResourceScope.Tournament(turnier));

        Assert.Equal($"Referee@Tournament:{turnier}", zuweisung.ToString());
    }

    [Fact]
    public void Ein_Mitglied_sieht_wer_dazugehoert_und_sonst_nichts()
    {
        // Die Rolle, die ein Turnier zur Gruppe macht. Ein einziges Recht, und
        // es ist ein Leserecht: eine Gruppe, in der niemand sieht, wer sonst
        // dabei ist, waere keine. Alles andere — Spielplan, Draw, Ergebnisse —
        // kommt nicht aus der Rechtematrix, sondern aus dem Query-Filter, der
        // an der Zuweisung haengt.
        Assert.Equal([Permission.ViewMembers], Permissions.Of(Role.Member));

        var turnier = Guid.NewGuid();
        var zuweisung = new RoleAssignment(
            Guid.NewGuid(), Guid.NewGuid(), Role.Member, ResourceScope.Tournament(turnier));

        Assert.Equal($"Member@Tournament:{turnier}", zuweisung.ToString());

        // Global gibt es sie nicht: ein Mitglied ist Mitglied eines Turniers.
        Assert.Throws<DomainException>(() =>
            new RoleAssignment(Guid.NewGuid(), Guid.NewGuid(), Role.Member, ResourceScope.Global));
    }

    [Fact]
    public void Ein_Mitglied_sieht_sein_Turnier_und_kein_anderes()
    {
        var meins = Guid.NewGuid();
        var fremdes = Guid.NewGuid();
        var benutzer = Guid.NewGuid();

        var mitglied = new UserPrincipal(benutzer, [
            new RoleAssignment(Guid.NewGuid(), benutzer, Role.Member, ResourceScope.Tournament(meins)),
        ]);

        Assert.Contains(meins, mitglied.TournamentIds);
        Assert.DoesNotContain(fremdes, mitglied.TournamentIds);
        Assert.False(mitglied.Can(Permission.ManageTournament, ResourceScope.Tournament(meins)));
        Assert.False(mitglied.Can(Permission.EnterResults, ResourceScope.Tournament(meins)));
    }

    [Fact]
    public void Ein_Benutzerkonto_braucht_Aussteller_und_Subject()
    {
        var aussteller = Assert.Throws<DomainException>(() =>
            new UserAccount(Guid.NewGuid(), "  ", "sub-1", null, null));

        Assert.Contains("Aussteller", aussteller.Message, StringComparison.Ordinal);

        Assert.Throws<DomainException>(() =>
            new UserAccount(Guid.NewGuid(), "https://idp.example.invalid", "  ", null, null));
    }
}
