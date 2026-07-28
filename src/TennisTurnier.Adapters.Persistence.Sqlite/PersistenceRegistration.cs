using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Adapters.Persistence.Sqlite.Repositories;
using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;

namespace TennisTurnier.Adapters.Persistence.Sqlite;

public static class PersistenceRegistration
{
    private const int BusyTimeoutSeconds = 5;

    /// <summary>
    /// Registriert den SQLite-Adapter. Der Aufrufer — die Composition Root —
    /// entscheidet über die Verbindungszeichenfolge; der Adapter kennt keine
    /// Konfigurationsquelle.
    /// </summary>
    public static IServiceCollection AddSqlitePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        // Die Wartezeit auf eine belegte Datenbank ist bewusst kurz. SQLite lässt
        // immer nur einen Schreiber zu; wartete ein zweiter die voreingestellte
        // halbe Minute, hinge der Aufrufer, statt ein „bitte noch einmal" zu
        // bekommen — und am Turniertag ist eine schnelle Absage mehr wert als
        // ein Erfolg nach dreißig Sekunden.
        services.AddDbContext<TennisTurnierDbContext>(
            options => options.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(BusyTimeoutSeconds)));

        services.AddScoped<IClubRepository, ClubRepository>();
        services.AddScoped<ITournamentRepository, TournamentRepository>();
        services.AddScoped<IFormatTemplateRepository, FormatTemplateRepository>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<IPhaseRepository, PhaseRepository>();
        services.AddScoped<ICourtAssignmentRepository, CourtAssignmentRepository>();
        services.AddScoped<ITournamentProjectionStore, TournamentProjectionStore>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly TennisTurnierDbContext _db;
    private readonly IPostCommitQueue _postCommit;

    private IDbContextTransaction? _transaction;

    public UnitOfWork(TennisTurnierDbContext db, IPostCommitQueue postCommit)
    {
        _db = db;
        _postCommit = postCommit;
    }

    /// <summary>
    /// Schreibt zwischendurch und liest danach frisch.
    ///
    /// Beides gehört zusammen. Die Transaktion bleibt bis zum Abschluss der
    /// Einheit offen — ohne sie wäre ein Zwischenstand bereits festgeschrieben,
    /// und ein Konflikt beim Abschluss ließe die Datenbank in einem Zustand
    /// zurück, den niemand wollte: das Turnier ausgelost, die öffentliche
    /// Ansicht nicht.
    ///
    /// Und die Änderungsverfolgung wird geleert, damit alles Weitere die
    /// Datenbank liest statt der Kopien vom Anfang des Requests. Ohne das
    /// bekäme, wer den Turnierbaum zu Beginn geladen hat, beim erneuten Abfragen
    /// wieder seinen eigenen alten Stand — die Ergebnisse, die inzwischen andere
    /// eingetragen haben, blieben unsichtbar, und die daraus gebaute öffentliche
    /// Ansicht schriebe sie still weg.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        _transaction ??= await _db.Database.BeginTransactionAsync(cancellationToken);

        await SaveAsync(cancellationToken);

        _db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Speichert, übersetzt den Nebenläufigkeitskonflikt in die Sprache der
    /// Anwendung und löst danach aus, was auf das Speichern gewartet hat.
    ///
    /// Die Übersetzung, weil der Normalfall „jemand war schneller" beim Aufrufer
    /// sonst als Serverfehler ankäme und die eigens gepflegten Zähler nach außen
    /// wirkungslos blieben. Das Nachgelagerte, weil ein Push an die Zuschauer
    /// erst hinausgehen darf, wenn die Änderung wirklich steht — bei einem
    /// Konflikt bleibt die Warteschlange unangetastet.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await SaveAsync(cancellationToken);

        if (_transaction is { } transaction)
        {
            _transaction = null;

            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
        }

        await _postCommit.DrainAsync(cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(exception);
        }
        catch (DbUpdateException exception) when (IsDatabaseBusy(exception))
        {
            throw new ConcurrencyConflictException(exception);
        }
    }

    /// <summary>
    /// War die Datenbank belegt?
    ///
    /// SQLite lässt immer nur einen Schreiber zu. Für den Aufrufer ist das
    /// dasselbe wie ein Nebenläufigkeitskonflikt — jemand anderes war schneller,
    /// bitte neu laden und wiederholen —, und es ist ausdrücklich kein
    /// Serverfehler. Ohne diese Unterscheidung käme der häufigste Fall am
    /// Turniertag als 500 beim Schiedsrichter an.
    /// </summary>
    private static bool IsDatabaseBusy(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: SqliteBusy or SqliteLocked };

    private const int SqliteBusy = 5;

    private const int SqliteLocked = 6;
}
