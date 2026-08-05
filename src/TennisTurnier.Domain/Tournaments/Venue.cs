using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tournaments;

/// <summary>
/// Wo ein Turnier stattfindet.
///
/// Bewusst ein Wertobjekt am Turnier und keine eigene Entität: die
/// Platzreservierung passiert außerhalb dieser Anwendung — der Veranstalter
/// ruft beim Verein an. Was hier steht, ist deshalb keine verwaltete Anlage mit
/// Stammdaten und Belegungsplan, sondern die Auskunft „hier wird gespielt",
/// zusammen mit der Zeitzone, ohne die keine Platzzeit auf die Zeitachse
/// abzubilden ist.
///
/// Ein Ort ohne Turnier hat damit keine Bedeutung, und zwei Turniere an
/// derselben Anlage teilen sich nichts. Das ist der Preis dafür, dass niemand
/// eine Anlage pflegen muss, bevor er ein Turnier ausschreiben kann.
/// </summary>
public sealed record Venue
{
    public Venue(string name, string? address, string? city, string timeZoneId)
    {
        Name = ValidateName(name);
        TimeZoneId = ValidateTimeZone(timeZoneId);
        Address = Trimmed(address);
        City = Trimmed(city);
    }

    /// <summary>„TC Maria Alm" — der Name, unter dem die Anlage bekannt ist.</summary>
    public string Name { get; }

    public string? Address { get; }

    public string? City { get; }

    /// <summary>
    /// IANA-Zeitzone der Anlage, etwa <c>Europe/Vienna</c>. Alle Zeiten werden
    /// intern in UTC geführt; die Platzzeiten sind aber lokale Angaben und ohne
    /// diese Zone nicht auf die Zeitachse abbildbar.
    /// </summary>
    public string TimeZoneId { get; }

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public override string ToString() => City is null ? Name : $"{Name}, {City}";

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new DomainException("Ein Turnier braucht einen Ort, an dem es stattfindet.")
            : name.Trim();

    /// <summary>
    /// Weist eine Zone zurück, die dieses System nicht kennt.
    ///
    /// Lieber hier scheitern als später beim Aufbau des Spielplans: dort wäre
    /// die Ursache eine Zeichenkette, die vor Wochen eingegeben wurde.
    /// </summary>
    private static string ValidateTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new DomainException("Ein Ort braucht eine Zeitzone.");
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DomainException($"Unbekannte Zeitzone „{timeZoneId.Trim()}“.", ex);
        }

        return timeZoneId.Trim();
    }
}
