using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TennisTurnier.Adapters.Persistence.Sqlite.Repositories;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Tests;

public sealed class UserDirectoryTests : IAsyncLifetime
{
    private const string Issuer = "https://test.local/realms/tennisturnier";

    private readonly SqliteTestDatabase _database = new();

    public Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Beim_ersten_Login_entsteht_ein_Konto()
    {
        await using var db = _database.NewContext();
        var directory = new UserDirectory(db);

        var account = await directory.EnsureAccountAsync(Issuer, "sub-1", "a@example.invalid", "Anna");

        Assert.Equal("sub-1", account.SubjectId);
        Assert.Equal("Anna", account.DisplayName);
        Assert.Equal(1, await db.UserAccounts.CountAsync());
    }

    [Fact]
    public async Task Ein_zweiter_Login_liefert_dasselbe_Konto()
    {
        await using var db = _database.NewContext();
        var directory = new UserDirectory(db);

        var first = await directory.EnsureAccountAsync(Issuer, "sub-1", "a@example.invalid", "Anna");
        var second = await directory.EnsureAccountAsync(Issuer, "sub-1", "a@example.invalid", "Anna");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.UserAccounts.CountAsync());
    }

    [Fact]
    public async Task Ein_geaenderter_Anzeigename_wird_uebernommen()
    {
        await using var db = _database.NewContext();
        var directory = new UserDirectory(db);

        await directory.EnsureAccountAsync(Issuer, "sub-1", "a@example.invalid", "Anna");
        var updated = await directory.EnsureAccountAsync(Issuer, "sub-1", "anna@example.invalid", "Anna Neu");

        Assert.Equal("anna@example.invalid", updated.Email);
        Assert.Equal("Anna Neu", updated.DisplayName);
    }

    [Fact]
    public async Task Dasselbe_Subject_bei_anderem_Aussteller_ist_ein_anderer_Benutzer()
    {
        // Der Grund, warum der Aussteller im Schlüssel steht: ein „sub" ist nur
        // innerhalb seines Identity Providers eindeutig. Sonst übernähme ein
        // Benutzer aus Provider B das Konto eines gleichnamigen aus Provider A.
        await using var db = _database.NewContext();
        var directory = new UserDirectory(db);

        var a = await directory.EnsureAccountAsync(Issuer, "sub-1", null, null);
        var b = await directory.EnsureAccountAsync("https://other.local", "sub-1", null, null);

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public async Task Dieselbe_Rolle_zweimal_zu_vergeben_bleibt_folgenlos()
    {
        await using var db = _database.NewContext();
        var directory = new UserDirectory(db);
        var account = await directory.EnsureAccountAsync(Issuer, "sub-1", null, null);
        var scope = ResourceScope.Tournament(Guid.NewGuid());

        await directory.AssignAsync(new RoleAssignment(Guid.NewGuid(), account.Id, Role.TournamentDirector, scope));
        await directory.AssignAsync(new RoleAssignment(Guid.NewGuid(), account.Id, Role.TournamentDirector, scope));

        Assert.Single(await directory.GetAssignmentsAsync(account.Id));
    }

    [Fact]
    public async Task Ein_Duplikat_aus_einem_parallelen_Zugriff_wird_abgefangen()
    {
        // Zwischen Prüfung und Schreiben passt eine zweite Vergabe. Genau das
        // stellt der Interceptor her: er schiebt dieselbe Zuweisung aus einem
        // zweiten Kontext ein, nachdem die Prüfung nichts gefunden hat. Der
        // eindeutige Index lässt nur einen durch — und der Verlierer darf das
        // nicht als Fehler nach oben geben.
        await using var setup = _database.NewContext();
        var account = await new UserDirectory(setup).EnsureAccountAsync(Issuer, "sub-1", null, null);
        var scope = ResourceScope.Tournament(Guid.NewGuid());

        var dazwischen = new SchreibtDazwischen(() =>
        {
            using var anderer = _database.NewContext();
            anderer.RoleAssignments.Add(new RoleAssignment(
                Guid.NewGuid(), account.Id, Role.TournamentDirector, scope));
            anderer.SaveChanges();
        });

        await using var db = _database.NewContext(dazwischen);

        await new UserDirectory(db).AssignAsync(
            new RoleAssignment(Guid.NewGuid(), account.Id, Role.TournamentDirector, scope));

        Assert.True(dazwischen.HatGeschrieben);
        Assert.Single(await new UserDirectory(setup).GetAssignmentsAsync(account.Id));
    }

    [Fact]
    public async Task Ein_anderer_Schreibfehler_wird_nicht_verschluckt()
    {
        // Dieselbe Kennung, anderer Scope: der Primärschlüssel schlägt zu, und
        // die Rolle steht danach trotzdem nicht da. Das ist kein Wettlauf,
        // sondern ein Fehler — er muss nach oben.
        await using var erster = _database.NewContext();
        var account = await new UserDirectory(erster).EnsureAccountAsync(Issuer, "sub-1", null, null);
        var id = Guid.NewGuid();

        await new UserDirectory(erster).AssignAsync(new RoleAssignment(
            id, account.Id, Role.TournamentDirector, ResourceScope.Tournament(Guid.NewGuid())));

        await using var zweiter = _database.NewContext();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new UserDirectory(zweiter).AssignAsync(new RoleAssignment(
                id, account.Id, Role.TournamentDirector, ResourceScope.Tournament(Guid.NewGuid()))));
    }

    [Fact]
    public async Task Eine_Rolle_die_es_nicht_gibt_zu_entziehen_bleibt_folgenlos()
    {
        // Zwei Turnierleitungen, die gleichzeitig dieselbe Rolle entziehen: der
        // zweite Aufruf findet nichts mehr vor und ist trotzdem in Ordnung.
        await using var db = _database.NewContext();

        await new UserDirectory(db).RevokeAsync(Guid.NewGuid());
    }

    /// <summary>
    /// Schreibt einmalig etwas in die Datenbank, kurz bevor der geprüfte Kontext
    /// selbst speichert.
    /// </summary>
    private sealed class SchreibtDazwischen : ISaveChangesInterceptor
    {
        private readonly Action _schreiben;

        internal SchreibtDazwischen(Action schreiben) => _schreiben = schreiben;

        internal bool HatGeschrieben { get; private set; }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!HatGeschrieben)
            {
                HatGeschrieben = true;
                _schreiben();
            }

            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Verschiedene_Scopes_derselben_Rolle_bleiben_nebeneinander_bestehen()
    {
        await using var db = _database.NewContext();
        var directory = new UserDirectory(db);
        var account = await directory.EnsureAccountAsync(Issuer, "sub-1", null, null);

        await directory.AssignAsync(new RoleAssignment(
            Guid.NewGuid(), account.Id, Role.TournamentDirector, ResourceScope.Tournament(Guid.NewGuid())));
        await directory.AssignAsync(new RoleAssignment(
            Guid.NewGuid(), account.Id, Role.TournamentDirector, ResourceScope.Tournament(Guid.NewGuid())));

        Assert.Equal(2, (await directory.GetAssignmentsAsync(account.Id)).Count);
    }

    [Fact]
    public async Task Eine_Rolle_laesst_sich_wieder_entziehen()
    {
        await using var db = _database.NewContext();
        var directory = new UserDirectory(db);
        var account = await directory.EnsureAccountAsync(Issuer, "sub-1", null, null);
        var assignment = new RoleAssignment(
            Guid.NewGuid(), account.Id, Role.TournamentDirector, ResourceScope.Tournament(Guid.NewGuid()));

        await directory.AssignAsync(assignment);
        await directory.RevokeAsync(assignment.Id);

        Assert.Empty(await directory.GetAssignmentsAsync(account.Id));
    }
}
