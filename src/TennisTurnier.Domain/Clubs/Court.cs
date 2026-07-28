using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Clubs;

public enum CourtSurface
{
    Clay,
    Hard,
    Carpet,
    Grass,
    Artificial,
}

public enum CourtLocation
{
    Outdoor,
    Indoor,
}

/// <summary>
/// Ein Platz des Vereins samt seiner Verfügbarkeit.
///
/// Die Verfügbarkeit gehört bewusst hierher und nicht ans Turnier: der Platz
/// gehört dem Verein, ein Turnier belegt nur Fenster darin.
/// </summary>
public sealed class Court : Entity
{
    private readonly List<AvailabilityWindow> _availability = [];
    private readonly List<CourtBlock> _blocks = [];

    internal Court(Guid id, Guid clubId, string name, CourtSurface surface, CourtLocation location)
        : base(id)
    {
        if (clubId == Guid.Empty)
        {
            throw new DomainException("Ein Platz braucht einen Verein.");
        }

        ClubId = clubId;
        Name = Validate(name);
        Surface = surface;
        Location = location;
        IsActive = true;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private Court(Guid id) : base(id) => Name = string.Empty;

    public Guid ClubId { get; private set; }

    public string Name { get; private set; }

    public CourtSurface Surface { get; private set; }

    public CourtLocation Location { get; private set; }

    /// <summary>Bevorzugtes Ziel für Finalspiele — eine weiche Scheduling-Regel.</summary>
    public bool IsCenterCourt { get; private set; }

    /// <summary>
    /// Ein stillgelegter Platz bleibt erhalten, damit vergangene Turniere lesbar
    /// bleiben, wird aber nicht mehr eingeplant.
    /// </summary>
    public bool IsActive { get; private set; }

    public IReadOnlyList<AvailabilityWindow> Availability => _availability;

    public IReadOnlyList<CourtBlock> Blocks => _blocks;

    public void Rename(string name) => Name = Validate(name);

    public void MarkAsCenterCourt(bool isCenterCourt) => IsCenterCourt = isCenterCourt;

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    public AvailabilityWindow AddAvailability(
        Guid windowId,
        DayOfWeek dayOfWeek,
        TimeOnly opensAt,
        TimeOnly closesAt,
        DateOnly validFrom,
        DateOnly? validUntil = null)
    {
        var window = new AvailabilityWindow(windowId, Id, dayOfWeek, opensAt, closesAt, validFrom, validUntil);

        var clash = _availability.FirstOrDefault(existing => existing.ConflictsWith(window));
        if (clash is not null)
        {
            throw new DomainException(
                $"Die Öffnungszeit überschneidet sich mit einer bestehenden Regel ({clash}).");
        }

        _availability.Add(window);
        return window;
    }

    public void RemoveAvailability(Guid windowId)
    {
        var window = _availability.FirstOrDefault(w => w.Id == windowId)
            ?? throw new DomainException("Die zu entfernende Öffnungszeit existiert nicht.");

        _availability.Remove(window);
    }

    public CourtBlock AddBlock(Guid blockId, TimeSlot period, BlockReason reason, string? note = null)
    {
        var block = new CourtBlock(blockId, Id, period, reason, note);
        _blocks.Add(block);
        return block;
    }

    public void RemoveBlock(Guid blockId)
    {
        var block = _blocks.FirstOrDefault(b => b.Id == blockId)
            ?? throw new DomainException("Die zu entfernende Sperre existiert nicht.");

        _blocks.Remove(block);
    }

    private static string Validate(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new DomainException("Ein Platz braucht einen Namen.")
            : name.Trim();
}
