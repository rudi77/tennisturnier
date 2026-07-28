using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.PublicView;

/// <summary>
/// Die öffentliche Sicht auf ein Turnier, fertig serialisiert (ADR-0003).
///
/// Sie ist kein Cache des Schreibmodells, sondern ein eigener Lesestand: sie
/// enthält von vornherein nur, was jeder sehen darf, und wird deshalb — als
/// einzige Tabelle — nicht vom Query-Filter aus ADR-0004 abgeschirmt. Genau
/// darin liegt ihr Wert und ihr Risiko: was hier hineingerät, ist öffentlich.
///
/// Die Identität ist die des Turniers. Es gibt je Turnier genau einen Stand;
/// eine eigene Id wäre eine zweite Wahrheit über dieselbe Sache.
/// </summary>
public sealed class TournamentProjection : Entity
{
    public TournamentProjection(Guid tournamentId, string json, DateTimeOffset updatedAt)
        : base(tournamentId)
    {
        Json = Validate(json);
        ETag = ComputeETag(Json);
        UpdatedAt = updatedAt;
        Version = 1;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private TournamentProjection(Guid id) : base(id)
    {
        Json = string.Empty;
        ETag = string.Empty;
    }

    public string Json { get; private set; }

    /// <summary>
    /// Der Vergleichswert für <c>If-None-Match</c>, samt Anführungszeichen — so
    /// steht er in der Antwort und so kommt er zurück.
    /// </summary>
    public string ETag { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Zählt die inhaltlichen Änderungen, nicht die Neuberechnungen.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Übernimmt einen neuen Stand und meldet, ob sich etwas geändert hat.
    ///
    /// Der Neuaufbau läuft nach jedem Ergebnis, jeder Platzvergabe und jeder
    /// Korrektur — oft mit demselben Ergebnis. Bliebe das unbemerkt, bekäme
    /// jeder Zuschauer bei jedem Speichern einen Push und jeder Abruf einen
    /// neuen ETag: der Cache wäre nutzlos, obwohl sich nichts geändert hat.
    /// </summary>
    public bool Replace(string json, DateTimeOffset at)
    {
        var next = Validate(json);
        var etag = ComputeETag(next);

        if (etag == ETag)
        {
            return false;
        }

        Json = next;
        ETag = etag;
        UpdatedAt = at;
        Version++;

        return true;
    }

    private static string Validate(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? throw new DomainException("Eine Projektion ohne Inhalt gibt es nicht.")
            : json;

    /// <summary>
    /// Ein starker ETag über den Inhalt. Aus dem Inhalt und nicht aus einem
    /// Zähler, damit ein Neuaufbau ohne Änderung auch denselben Wert ergibt.
    /// </summary>
    private static string ComputeETag(string json)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"\"{Convert.ToHexStringLower(hash.AsSpan(0, 16))}\"");
    }
}
