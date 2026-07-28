namespace TennisTurnier.Domain.Security;

/// <summary>
/// Rollen nach ADR-0004. Jede Rolle gilt in einem <see cref="ResourceScope"/>,
/// nicht systemweit — mit Ausnahme von <see cref="SystemAdmin"/>.
/// </summary>
public enum Role
{
    /// <summary>Darf alles, inklusive Vereine anlegen. Scope: Global.</summary>
    SystemAdmin,

    /// <summary>Plätze, Mitglieder und Turniere eines Vereins. Scope: Club.</summary>
    ClubAdmin,

    /// <summary>Draw, Spielplan und Ergebnisse eines Turniers. Scope: Tournament.</summary>
    TournamentDirector,

    /// <summary>Ausschließlich Ergebniseingabe. Scope: Tournament.</summary>
    Referee,

    /// <summary>Anmeldung und eigene Daten. Scope: Club.</summary>
    Player,
}

/// <summary>
/// Was jemand tun darf. Bewusst fachlich benannt und nicht als CRUD-Matrix:
/// „darf Ergebnisse eintragen" überlebt eine Umgestaltung der Endpunkte,
/// „darf POST auf /matches" nicht.
/// </summary>
public enum Permission
{
    /// <summary>Vereine anlegen und löschen.</summary>
    ManageClubs,

    /// <summary>Stammdaten eines Vereins ändern.</summary>
    ManageClub,

    /// <summary>Plätze, Öffnungszeiten und Sperren eines Vereins pflegen.</summary>
    ManageCourts,

    /// <summary>Turniere anlegen, Draw erzeugen, Spielplan ändern.</summary>
    ManageTournament,

    /// <summary>Ergebnisse eintragen und korrigieren.</summary>
    EnterResults,

    /// <summary>Nicht-öffentliche Daten sehen: Kontaktdaten, interne Notizen.</summary>
    ViewInternals,
}
