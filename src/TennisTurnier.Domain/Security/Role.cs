namespace TennisTurnier.Domain.Security;

/// <summary>
/// Rollen nach ADR-0004. Jede Rolle gilt in einem <see cref="ResourceScope"/>,
/// nicht systemweit — mit Ausnahme von <see cref="SystemAdmin"/>.
///
/// Die Ressource, an der eine Rolle hängt, ist das Turnier. Es gab hier einmal
/// <c>ClubAdmin</c> und <c>Player</c> im Scope eines Vereins; beide sind
/// entfallen, weil der Verein als Mandantengrenze entfällt. Für den
/// Turnierleiter ändert sich dabei nichts — er war von Anfang an ans Turnier
/// gebunden, und dass er jetzt der einzige Weg dorthin ist, macht die Grenze
/// enger, nicht weiter.
/// </summary>
public enum Role
{
    /// <summary>Darf alles. Scope: Global.</summary>
    SystemAdmin,

    /// <summary>Draw, Spielplan und Ergebnisse eines Turniers. Scope: Tournament.</summary>
    TournamentDirector,

    /// <summary>Ausschließlich Ergebniseingabe. Scope: Tournament.</summary>
    Referee,
}

/// <summary>
/// Was jemand tun darf. Bewusst fachlich benannt und nicht als CRUD-Matrix:
/// „darf Ergebnisse eintragen" überlebt eine Umgestaltung der Endpunkte,
/// „darf POST auf /matches" nicht.
/// </summary>
public enum Permission
{
    // Die drei vereinsbezogenen Rechte hat seit dem Wegfall von ClubAdmin nur
    // noch der Systemadministrator. Sie stehen hier, bis der Verein selbst
    // entfällt — sie vorher zu streichen hieße, die Vereinsverwaltung für einen
    // Commit ungeschützt zu lassen.

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
