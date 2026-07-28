using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Web;

/// <summary>
/// Beschriftungen für die Oberfläche.
///
/// Die Aufzählungen der Domäne heißen englisch, weil sie im Code gelesen werden;
/// auf dem Bildschirm steht Deutsch, weil dort eine Turnierleitung liest. Die
/// Übersetzung an einer Stelle zu halten ist der ganze Zweck dieser Klasse —
/// sonst steht „DrawGenerated" an drei Stellen unterschiedlich übersetzt.
/// </summary>
internal static class Display
{
    public static string Of(TournamentState state) => state switch
    {
        TournamentState.Draft => "Entwurf",
        TournamentState.RegistrationOpen => "Meldung offen",
        TournamentState.RegistrationClosed => "Meldeschluss",
        TournamentState.DrawGenerated => "Ausgelost",
        TournamentState.InProgress => "Läuft",
        TournamentState.Completed => "Beendet",
        TournamentState.Abandoned => "Abgebrochen",
        _ => state.ToString(),
    };

    /// <summary>Die Farbfamilie zum Zustand — dieselbe Zuordnung in allen Ansichten.</summary>
    public static string TagOf(TournamentState state) => state switch
    {
        TournamentState.InProgress => "live",
        TournamentState.Completed => "ok",
        TournamentState.Abandoned => "bad",
        TournamentState.DrawGenerated => "ok",
        _ => string.Empty,
    };

    public static string Of(SchedulingMode mode) => mode switch
    {
        SchedulingMode.Planning => "Planung",
        SchedulingMode.MatchDay => "Turniertag",
        _ => mode.ToString(),
    };

    public static string Of(EntryStatus status) => status switch
    {
        EntryStatus.Applied => "Gemeldet",
        EntryStatus.Accepted => "Im Feld",
        EntryStatus.WaitingList => "Warteliste",
        EntryStatus.Withdrawn => "Zurückgezogen",
        _ => status.ToString(),
    };

    public static string TagOf(EntryStatus status) => status switch
    {
        EntryStatus.Accepted => "ok",
        EntryStatus.WaitingList => "warn",
        EntryStatus.Withdrawn => "bad",
        _ => string.Empty,
    };

    public static string Of(MatchStatus status) => status switch
    {
        MatchStatus.Pending => "Wartet auf Vorspiel",
        MatchStatus.Ready => "Spielbereit",
        MatchStatus.Finished => "Beendet",
        _ => status.ToString(),
    };

    public static string Of(MatchOutcome outcome) => outcome switch
    {
        MatchOutcome.Normal => "Ausgespielt",
        MatchOutcome.Retirement => "Aufgabe",
        MatchOutcome.Walkover => "Nicht angetreten",
        MatchOutcome.Disqualification => "Disqualifikation",
        MatchOutcome.Bye => "Freilos",
        _ => outcome.ToString(),
    };

    public static string Of(AssignmentStatus status) => status switch
    {
        AssignmentStatus.Planned => "Geplant",
        AssignmentStatus.Called => "Aufgerufen",
        AssignmentStatus.Running => "Läuft",
        AssignmentStatus.Finished => "Beendet",
        AssignmentStatus.Suspended => "Unterbrochen",
        _ => status.ToString(),
    };

    public static string TagOf(AssignmentStatus status) => status switch
    {
        AssignmentStatus.Running => "live",
        AssignmentStatus.Called => "warn",
        AssignmentStatus.Finished => "ok",
        AssignmentStatus.Suspended => "bad",
        _ => string.Empty,
    };

    public static string Of(PhaseStatus status) => status switch
    {
        PhaseStatus.Pending => "Ausstehend",
        PhaseStatus.Running => "Läuft",
        PhaseStatus.Completed => "Abgeschlossen",
        _ => status.ToString(),
    };

    public static string Of(CourtSurface surface) => surface switch
    {
        CourtSurface.Clay => "Sand",
        CourtSurface.Hard => "Hartplatz",
        CourtSurface.Carpet => "Teppich",
        CourtSurface.Grass => "Rasen",
        CourtSurface.Artificial => "Kunstrasen",
        _ => surface.ToString(),
    };

    public static string Of(CourtLocation location) => location switch
    {
        CourtLocation.Outdoor => "Freiluft",
        CourtLocation.Indoor => "Halle",
        _ => location.ToString(),
    };

    public static string Of(BlockReason reason) => reason switch
    {
        BlockReason.Training => "Training",
        BlockReason.LeagueMatch => "Punktspiel",
        BlockReason.Maintenance => "Pflege",
        BlockReason.Weather => "Wetter",
        BlockReason.Other => "Sonstiges",
        _ => reason.ToString(),
    };

    public static string Of(PhaseFormatKind kind) => kind switch
    {
        PhaseFormatKind.Knockout => "K.-o.",
        PhaseFormatKind.RoundRobin => "Gruppe",
        PhaseFormatKind.Swiss => "Schweizer System",
        _ => kind.ToString(),
    };

    public static string Of(ProposalChange change) => change switch
    {
        ProposalChange.Unchanged => "unverändert",
        ProposalChange.Added => "neu",
        ProposalChange.Moved => "verschoben",
        _ => change.ToString(),
    };

    public static string Of(ScheduleConstraint constraint) => constraint switch
    {
        ScheduleConstraint.PlayerDoubleBooked => "Spieler doppelt angesetzt",
        ScheduleConstraint.PlayerRestPeriod => "Pause zu kurz",
        ScheduleConstraint.CourtDoubleBooked => "Platz doppelt belegt",
        ScheduleConstraint.CourtUnavailable => "Platz nicht verfügbar",
        ScheduleConstraint.DependencyOrder => "Reihenfolge verletzt",
        _ => constraint.ToString(),
    };

    public static string Of(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Montag",
        DayOfWeek.Tuesday => "Dienstag",
        DayOfWeek.Wednesday => "Mittwoch",
        DayOfWeek.Thursday => "Donnerstag",
        DayOfWeek.Friday => "Freitag",
        DayOfWeek.Saturday => "Samstag",
        DayOfWeek.Sunday => "Sonntag",
        _ => day.ToString(),
    };

    /// <summary>Eine Uhrzeit ohne Datum — am Turniertag zählt selten mehr.</summary>
    public static string Time(DateTimeOffset? value) => value?.ToString("HH:mm") ?? "–";

    public static string DateTime(DateTimeOffset? value) => value?.ToString("dd.MM. HH:mm") ?? "–";

    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy");

    /// <summary>„1:15 h" statt „01:15:00" — die Dauer ist eine Auskunft, kein Zeitstempel.</summary>
    public static string Duration(TimeSpan value) =>
        value.Hours > 0 ? $"{value.Hours}:{value.Minutes:00} h" : $"{value.Minutes} min";
}
