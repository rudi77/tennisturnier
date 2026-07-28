using TennisTurnier.Domain.Clubs;

namespace TennisTurnier.Application.Clubs;

public sealed record CreateClubRequest(string Name, string TimeZoneId, string? City);

public sealed record UpdateClubRequest(string Name, string TimeZoneId, string? City);

public sealed record ClubSummary(Guid Id, string Name, string? City, string TimeZoneId, int CourtCount);

public sealed record ClubDetail(
    Guid Id,
    string Name,
    string? City,
    string TimeZoneId,
    IReadOnlyList<CourtDetail> Courts);

public sealed record CreateCourtRequest(
    string Name,
    CourtSurface Surface,
    CourtLocation Location,
    bool IsCenterCourt = false);

public sealed record UpdateCourtRequest(
    string Name,
    bool IsCenterCourt,
    bool IsActive);

public sealed record CourtDetail(
    Guid Id,
    string Name,
    CourtSurface Surface,
    CourtLocation Location,
    bool IsCenterCourt,
    bool IsActive,
    IReadOnlyList<AvailabilityDetail> Availability,
    IReadOnlyList<BlockDetail> Blocks);

public sealed record AvailabilityDetail(
    Guid Id,
    DayOfWeek DayOfWeek,
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    DateOnly ValidFrom,
    DateOnly? ValidUntil);

public sealed record CreateAvailabilityRequest(
    DayOfWeek DayOfWeek,
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    DateOnly ValidFrom,
    DateOnly? ValidUntil);

public sealed record BlockDetail(
    Guid Id,
    DateTimeOffset From,
    DateTimeOffset To,
    BlockReason Reason,
    string? Note);

public sealed record CreateBlockRequest(
    DateTimeOffset From,
    DateTimeOffset To,
    BlockReason Reason,
    string? Note);

/// <summary>Ein freies Fenster auf einem Platz — die Ausgabe des <see cref="CourtCalendar"/>.</summary>
public sealed record FreeWindow(DateTimeOffset From, DateTimeOffset To);
