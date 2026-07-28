using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Adapters.Persistence.Sqlite.Repositories;
using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;

namespace TennisTurnier.Adapters.Persistence.Sqlite;

public static class PersistenceRegistration
{
    /// <summary>
    /// Registriert den SQLite-Adapter. Der Aufrufer — die Composition Root —
    /// entscheidet über die Verbindungszeichenfolge; der Adapter kennt keine
    /// Konfigurationsquelle.
    /// </summary>
    public static IServiceCollection AddSqlitePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<TennisTurnierDbContext>(options => options.UseSqlite(connectionString));

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
    /// Schreibt zwischendurch, innerhalb einer Transaktion, die bis zum Abschluss
    /// der Einheit offen bleibt. Ohne sie wäre ein Zwischenstand bereits
    /// festgeschrieben, und ein Konflikt beim Abschluss ließe die Datenbank in
    /// einem Zustand zurück, den niemand wollte: das Turnier ausgelost, die
    /// öffentliche Ansicht nicht.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        _transaction ??= await _db.Database.BeginTransactionAsync(cancellationToken);

        await SaveAsync(cancellationToken);
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
    }
}
