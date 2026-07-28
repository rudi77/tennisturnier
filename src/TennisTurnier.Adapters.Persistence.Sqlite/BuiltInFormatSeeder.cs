using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Domain.Formats;

namespace TennisTurnier.Adapters.Persistence.Sqlite;

public static class BuiltInFormatSeeder
{
    /// <summary>
    /// Legt die mitgelieferten Formatvorlagen an, sofern sie fehlen.
    ///
    /// Idempotent und ohne Aktualisierung bestehender Einträge: eine bereits
    /// verwendete Vorlage nachträglich zu ändern, würde ihre Version hochzählen
    /// und in laufenden Turnieren einen Stand ausweisen, den es dort nie gab.
    /// Eine geänderte Standardvorlage bekommt deshalb eine neue Id.
    /// </summary>
    public static async Task SeedBuiltInFormatsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TennisTurnierDbContext>();

        var existing = await db.FormatTemplates
            .IgnoreQueryFilters()
            .Where(t => t.ClubId == null)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var missing = BuiltInFormats.All
            .Select(definition => new { Id = DeterministicId(definition.Id), Definition = definition })
            .Where(candidate => !existing.Contains(candidate.Id))
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        foreach (var candidate in missing)
        {
            db.FormatTemplates.Add(new FormatTemplate(candidate.Id, clubId: null, candidate.Definition));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Leitet aus der fachlichen Id eine feste Guid ab.
    ///
    /// Damit trägt „ko-single" in jeder Umgebung dieselbe Id — sonst zeigte ein
    /// Turnier, das aus einer Sicherung in eine andere Datenbank wandert, auf
    /// eine Vorlage, die dort eine andere ist.
    /// </summary>
    private static Guid DeterministicId(string formatId)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"tennisturnier.builtin-format.{formatId}"));

        return new Guid(bytes);
    }
}
