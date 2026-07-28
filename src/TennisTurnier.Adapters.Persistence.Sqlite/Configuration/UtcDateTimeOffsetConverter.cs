using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

/// <summary>
/// Speichert jeden <see cref="DateTimeOffset"/> als normalisierten ISO-8601-Text
/// in UTC.
///
/// Der Grund steht in ADR-0006: SQLite hat keinen eigenen Datumstyp und sortiert
/// TEXT lexikografisch. Die Vorgabe des Providers enthält den Zonenversatz
/// (<c>+02:00</c>), womit <c>10:00+02:00</c> hinter <c>09:00+00:00</c> einsortiert
/// würde, obwohl es derselbe Zeitpunkt ist. Bei einer Anwendung, deren gesamter
/// Zweck die zeitliche Ordnung von Matches ist, wäre das kein Randfall.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, string>
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public UtcDateTimeOffsetConverter()
        : base(
            value => value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture),
            text => Parse(text))
    {
    }

    private static DateTimeOffset Parse(string text) =>
        DateTimeOffset.ParseExact(
            text,
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
