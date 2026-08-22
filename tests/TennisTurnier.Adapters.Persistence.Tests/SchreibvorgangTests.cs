using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TennisTurnier.Adapters.Persistence.Sqlite;
using TennisTurnier.Application.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Tests;

/// <summary>
/// Was beim Schreiben schiefgehen kann — und wie es beim Aufrufer ankommt.
///
/// SQLite lässt immer nur einen Schreiber zu. Für den Aufrufer ist eine belegte
/// Datenbank dasselbe wie ein Nebenläufigkeitskonflikt: jemand war schneller,
/// bitte neu laden. Ein Verstoß gegen einen Fremdschlüssel ist dagegen ein
/// Programmfehler und darf sich nicht als „bitte noch einmal" tarnen — sonst
/// probiert die Oberfläche es endlos erneut.
/// </summary>
public sealed class SchreibvorgangTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    public Task InitializeAsync()
    {
        _database.ActingAs = UserPrincipal.System;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private static UnitOfWork EinheitFuer(TennisTurnierDbContext db) => new(db, new PostCommitQueue());

    [Fact]
    public async Task Eine_belegte_Datenbank_ist_ein_Nebenlaeufigkeitskonflikt()
    {
        // Ein zweiter Schreiber hält die Datei exklusiv. Der erste bekommt keine
        // fünfhundert, sondern die Aufforderung, es noch einmal zu versuchen.
        await using var db = _database.NewContext();
        db.FormatTemplates.Add(new FormatTemplate(Guid.NewGuid(), Guid.NewGuid(), BuiltInFormats.Knockout));

        await using var sperre = _database.Sperren();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            EinheitFuer(db).SaveChangesAsync());
    }

    [Fact]
    public async Task Ein_Fremdschluesselfehler_bleibt_ein_Fehler()
    {
        // Eine Rolle für ein Konto, das es nicht gibt. Das ist kein Wettlauf,
        // und ein „bitte noch einmal" wäre eine Endlosschleife.
        await using var db = _database.NewContext();
        db.RoleAssignments.Add(new RoleAssignment(
            Guid.NewGuid(), Guid.NewGuid(), Role.TournamentDirector, ResourceScope.Tournament(Guid.NewGuid())));

        await Assert.ThrowsAsync<DbUpdateException>(() => EinheitFuer(db).SaveChangesAsync());
    }

    [Fact]
    public async Task Ein_Zwischenstand_wird_erst_am_Ende_festgeschrieben()
    {
        // Flush schreibt in einer offenen Transaktion; erst SaveChanges macht
        // daraus einen Stand, den andere sehen.
        await using var db = _database.NewContext();
        var einheit = EinheitFuer(db);
        var vorlage = new FormatTemplate(Guid.NewGuid(), Guid.NewGuid(), BuiltInFormats.Knockout);

        db.FormatTemplates.Add(vorlage);
        await einheit.FlushAsync();

        // Die Änderungsverfolgung ist danach leer: alles Weitere liest die
        // Datenbank und nicht die Kopien vom Anfang.
        Assert.Empty(db.ChangeTracker.Entries());

        await einheit.SaveChangesAsync();

        await using var anderer = _database.NewContext();
        Assert.NotNull(await anderer.FormatTemplates.FindAsync(vorlage.Id));
    }

    [Fact]
    public void Die_Entwurfszeit_baut_ihren_eigenen_Kontext()
    {
        // Nur für `dotnet ef`. Ohne diesen Weg ließe sich keine Migration
        // erzeugen — und geprüft wird er sonst von niemandem.
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.NotNull(db.Model.FindEntityType(typeof(FormatTemplate)));

        // Und der Query-Filter kommt aus dem Systemkontext: die Abfrage entsteht
        // ohne Einschränkung auf ein Turnier. Ein Blick auf das erzeugte SQL
        // genügt dafür — die Datei dahinter wird nie angelegt.
        Assert.DoesNotContain("@__", db.Tournaments.ToQueryString(), StringComparison.Ordinal);
    }
}
