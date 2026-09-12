namespace Matchday.Domain;

/// <summary>Ein Teilnehmer ist ein Name. Mehr braucht ein Turnier unter Freunden nicht.</summary>
public sealed record Participant(Guid Id, string Name);
