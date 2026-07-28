using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

/// <summary>
/// Speichert eine kleine, feste Liste von Ids als kommaseparierten Text.
///
/// Gedacht für die ein bis zwei Spieler eines Teilnehmers. Für eine eigene
/// Tabelle ist das zu wenig Struktur, und abgefragt wird die Liste nie einzeln:
/// sie wird immer ganz geladen, um Überschneidungen im Spielplan zu prüfen.
/// Für längere oder durchsuchbare Listen wäre das die falsche Wahl.
/// </summary>
public sealed class GuidListConverter : ValueConverter<IReadOnlyList<Guid>, string>
{
    public GuidListConverter()
        : base(
            ids => string.Join(',', ids),
            text => Parse(text))
    {
    }

    private static IReadOnlyList<Guid> Parse(string text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
}

/// <summary>
/// Ohne eigenen Vergleicher hielte EF Core zwei inhaltsgleiche Listen für
/// verschieden und schriebe bei jedem Speichern.
/// </summary>
public sealed class GuidListComparer : ValueComparer<IReadOnlyList<Guid>>
{
    public GuidListComparer()
        : base(
            (left, right) => (left ?? new List<Guid>()).SequenceEqual(right ?? new List<Guid>()),
            ids => ids.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
            ids => ids.ToList())
    {
    }
}
