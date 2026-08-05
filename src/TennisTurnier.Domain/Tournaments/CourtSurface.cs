namespace TennisTurnier.Domain.Tournaments;

public enum CourtSurface
{
    Clay,
    Hard,
    Carpet,
    Grass,
    Artificial,
}

/// <summary>Drinnen oder draußen — nicht zu verwechseln mit dem <see cref="Venue"/>.</summary>
public enum CourtLocation
{
    Outdoor,
    Indoor,
}
