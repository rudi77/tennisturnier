using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Clubs;

/// <summary>
/// Ein Tennisverein. Wurzel der Mandantengrenze: alles, was einem Verein gehört,
/// trägt seine <see cref="Entity.Id"/> und wird über den Query-Filter aus ADR-0004
/// gegen fremde Zugriffe abgeschirmt.
/// </summary>
public sealed class Club : Entity
{
    private readonly List<Court> _courts = [];

    public Club(Guid id, string name, string timeZoneId, string? city = null)
        : base(id)
    {
        Name = Validate(name);
        TimeZoneId = ValidateTimeZone(timeZoneId);
        SetCity(city);
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private Club(Guid id) : base(id)
    {
        Name = string.Empty;
        TimeZoneId = "UTC";
    }

    public string Name { get; private set; }

    public string? City { get; private set; }

    /// <summary>
    /// IANA-Zeitzone des Vereins, etwa <c>Europe/Vienna</c>. Alle Zeiten werden
    /// intern in UTC geführt; die Öffnungszeiten eines Platzes sind aber lokale
    /// Angaben und ohne diese Zone nicht auf die Zeitachse abbildbar.
    /// </summary>
    public string TimeZoneId { get; private set; }

    public IReadOnlyList<Court> Courts => _courts;

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public void Rename(string name) => Name = Validate(name);

    public void MoveTo(string timeZoneId) => TimeZoneId = ValidateTimeZone(timeZoneId);

    public void SetCity(string? city) => City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();

    public Court AddCourt(Guid courtId, string name, CourtSurface surface, CourtLocation location)
    {
        RequireFreeCourtName(name, exceptCourtId: null);

        var court = new Court(courtId, Id, name, surface, location);
        _courts.Add(court);
        return court;
    }

    /// <summary>
    /// Das Umbenennen läuft über den Verein, weil nur er die Geschwisterplätze
    /// kennt. Ginge es direkt am Platz, bliebe die Eindeutigkeit allein dem
    /// eindeutigen Index überlassen — und ein belegter Name schlüge als
    /// Datenbankfehler durch statt als fachlicher.
    /// </summary>
    public void RenameCourt(Guid courtId, string name)
    {
        var court = _courts.FirstOrDefault(c => c.Id == courtId)
            ?? throw new DomainException($"Der Verein hat keinen Platz mit der Id {courtId}.");

        RequireFreeCourtName(name, exceptCourtId: courtId);
        court.Rename(name);
    }

    private void RequireFreeCourtName(string name, Guid? exceptCourtId)
    {
        var candidate = (name ?? string.Empty).Trim();

        var taken = _courts.Any(c =>
            c.Id != exceptCourtId
            && string.Equals(c.Name, candidate, StringComparison.OrdinalIgnoreCase));

        if (taken)
        {
            throw new DomainException($"Der Verein hat bereits einen Platz mit dem Namen „{candidate}“.");
        }
    }

    private static string Validate(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new DomainException("Ein Verein braucht einen Namen.")
            : name.Trim();

    private static string ValidateTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new DomainException("Ein Verein braucht eine Zeitzone.");
        }

        try
        {
            // Wirft, wenn die Zone auf diesem System unbekannt ist. Lieber hier
            // scheitern als später beim Aufbau des Spielplans.
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DomainException($"Unbekannte Zeitzone „{timeZoneId.Trim()}“.", ex);
        }

        return timeZoneId.Trim();
    }
}
