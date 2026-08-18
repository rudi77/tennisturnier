using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

/// <summary>
/// Serialisierung der Formatdefinition.
///
/// Enums werden als Namen geschrieben, nicht als Zahlen: eine gespeicherte
/// Definition soll lesbar und vergleichbar sein (ADR-0001 nennt „diff-bar" als
/// Zweck), und das Einfügen eines Enum-Werts darf gespeicherte Daten nicht
/// umdeuten.
/// </summary>
public static class FormatJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json, string what) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new DomainException($"Gespeichertes {what} ließ sich nicht lesen: {json}");
}

/// <summary>
/// Legt die eingefrorene Formatdefinition als eine JSON-Spalte ab.
///
/// Bewusst als Ganzes und nicht in Einzelspalten: nach ADR-0006 wird eine
/// JSON-Spalte nie serverseitig durchsucht, damit der Wechsel von SQLite auf
/// PostgreSQL ein Adapterwechsel bleibt und kein Umbau.
/// </summary>
public sealed class FormatSnapshotConverter : ValueConverter<FormatSnapshot, string>
{
    public FormatSnapshotConverter()
        : base(
            snapshot => FormatJson.Serialize(snapshot),
            json => FormatJson.Deserialize<FormatSnapshot>(json, "Turnierformat"))
    {
    }
}

/// <summary>
/// Das Satzformat des Turniers, ebenfalls als eine JSON-Spalte.
///
/// Drei kleine Spalten täten es auch — bis zur ersten Erweiterung des
/// Satzformats, die dann eine Migration je Feld bedeutete. Die Spalte wird nie
/// durchsucht (ADR-0006), und das Format ist dasselbe unveränderliche
/// Wertobjekt, das im Snapshot ohnehin als JSON liegt.
/// </summary>
public sealed class MatchFormatConverter : ValueConverter<MatchFormat, string>
{
    public MatchFormatConverter()
        : base(
            format => FormatJson.Serialize(format),
            json => FormatJson.Deserialize<MatchFormat>(json, "Satzformat"))
    {
    }
}

public sealed class MatchFormatComparer : ValueComparer<MatchFormat>
{
    public MatchFormatComparer()
        : base(
            (left, right) => FormatJson.Serialize(left) == FormatJson.Serialize(right),
            format => FormatJson.Serialize(format).GetHashCode(StringComparison.Ordinal),
            format => FormatJson.Deserialize<MatchFormat>(FormatJson.Serialize(format), "Satzformat"))
    {
    }
}

public sealed class FormatDefinitionConverter : ValueConverter<FormatDefinition, string>
{
    public FormatDefinitionConverter()
        : base(
            definition => FormatJson.Serialize(definition),
            json => FormatJson.Deserialize<FormatDefinition>(json, "Format"))
    {
    }
}

/// <summary>
/// Vergleich und Kopie für die Änderungsverfolgung.
///
/// Ohne das vergleicht EF Core Referenzen. Eine geladene Definition, die
/// unverändert zurückgeschrieben wird, gälte dann als geändert — und eine
/// tatsächliche Änderung an einer Kopie bliebe unbemerkt.
/// </summary>
public sealed class FormatDefinitionComparer : ValueComparer<FormatDefinition>
{
    public FormatDefinitionComparer()
        : base(
            (left, right) => FormatJson.Serialize(left) == FormatJson.Serialize(right),
            definition => FormatJson.Serialize(definition).GetHashCode(StringComparison.Ordinal),
            definition => FormatJson.Deserialize<FormatDefinition>(FormatJson.Serialize(definition), "Format"))
    {
    }
}

public sealed class FormatSnapshotComparer : ValueComparer<FormatSnapshot>
{
    public FormatSnapshotComparer()
        : base(
            (left, right) => FormatJson.Serialize(left) == FormatJson.Serialize(right),
            snapshot => FormatJson.Serialize(snapshot).GetHashCode(StringComparison.Ordinal),
            snapshot => FormatJson.Deserialize<FormatSnapshot>(FormatJson.Serialize(snapshot), "Turnierformat"))
    {
    }
}
