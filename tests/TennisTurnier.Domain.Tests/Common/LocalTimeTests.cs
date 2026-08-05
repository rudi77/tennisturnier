using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tests.Common;

/// <summary>
/// Die Abbildung lokaler Wanduhrzeit auf die Zeitachse.
///
/// Die beiden Umstellungsnächte sind der ganze Grund, aus dem es diese Klasse
/// gibt. Sie liegen bei europäischen Zeitzonen nachts um drei und damit
/// praktisch nie in den Platzzeiten eines Turniers — genau deshalb fiele ein
/// Fehler hier erst auf, wenn er einmal im Jahr einen unerklärlichen Spielplan
/// erzeugt.
/// </summary>
public sealed class LocalTimeTests
{
    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    private readonly LocalTime _local = new(Vienna);

    private TimeSlot Window(DateOnly day, int fromHour, int toHour) =>
        new(
            _local.Resolve(day, new TimeOnly(fromHour, 0), LocalTime.Ambiguity.Earliest),
            _local.Resolve(day, new TimeOnly(toHour, 0), LocalTime.Ambiguity.Latest));

    [Fact]
    public void Ein_normaler_Tag_hat_genau_die_angegebene_Dauer()
    {
        // Gegenprobe zu den beiden Umstellungstests: ohne Zeitumstellung darf
        // die Auflösung nichts verschieben.
        var window = Window(new DateOnly(2026, 5, 16), 8, 22);

        Assert.Equal(TimeSpan.FromHours(14), window.Duration);
    }

    [Fact]
    public void Der_Tag_der_Zeitumstellung_im_Fruehjahr_ist_eine_Stunde_kuerzer()
    {
        // 29.03.2026: In Europa springt die Uhr um 02:00 auf 03:00. Ein Fenster
        // von 00:00 bis 06:00 lokaler Zeit dauert daher nur fünf Stunden.
        var window = Window(new DateOnly(2026, 3, 29), 0, 6);

        Assert.Equal(TimeSpan.FromHours(5), window.Duration);
    }

    [Fact]
    public void Der_Tag_der_Zeitumstellung_im_Herbst_ist_eine_Stunde_laenger()
    {
        // 25.10.2026: Die Uhr fällt um 03:00 auf 02:00 zurück, die Stunde von
        // 02:00 bis 03:00 existiert doppelt. Beginn auf die frühere, Ende auf
        // die spätere Ausprägung — das Fenster deckt beide ab.
        var window = Window(new DateOnly(2026, 10, 25), 0, 6);

        Assert.Equal(TimeSpan.FromHours(7), window.Duration);
    }

    [Fact]
    public void Eine_uebersprungene_Stunde_wandert_auf_den_ersten_gueltigen_Zeitpunkt()
    {
        // 02:30 gibt es am 29.03.2026 nicht. Eine Platzzeit, die dort begänne,
        // muss irgendwohin — und zwar nach vorn, nicht zurück: sonst begänne
        // sie vor der Zeit, die der Veranstalter zugesagt bekommen hat.
        var resolved = _local.Resolve(
            new DateOnly(2026, 3, 29), new TimeOnly(2, 30), LocalTime.Ambiguity.Earliest);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)), resolved);
    }

    [Fact]
    public void Eine_doppelte_Stunde_hat_zwei_Auspraegungen()
    {
        var day = new DateOnly(2026, 10, 25);
        var time = new TimeOnly(2, 30);

        var earliest = _local.Resolve(day, time, LocalTime.Ambiguity.Earliest);
        var latest = _local.Resolve(day, time, LocalTime.Ambiguity.Latest);

        Assert.Equal(TimeSpan.FromHours(2), earliest.Offset);
        Assert.Equal(TimeSpan.FromHours(1), latest.Offset);
        Assert.Equal(TimeSpan.FromHours(1), latest - earliest);
    }

    [Fact]
    public void Mitternacht_ist_die_lokale_und_nicht_die_UTC_Mitternacht()
    {
        var midnight = _local.Midnight(new DateOnly(2026, 5, 16));

        Assert.Equal(new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.FromHours(2)), midnight);
        Assert.Equal(new DateTime(2026, 5, 15, 22, 0, 0, DateTimeKind.Utc), midnight.UtcDateTime);
    }
}
