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

    /// <summary>
    /// Darf Turniere anlegen — und führt danach seine eigenen. Scope: Global.
    ///
    /// Global und trotzdem harmlos: das einzige Recht dieser Rolle ist
    /// <see cref="Permission.CreateTournament"/>, und was daraus entsteht,
    /// gehört ihrem Anleger allein. Sie ist die Antwort auf die Frage, wer
    /// überhaupt hereindarf, nicht auf die Frage, wer was sieht.
    /// </summary>
    Organizer,

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
    /// <summary>
    /// Ein Turnier anlegen. Bewusst getrennt von
    /// <see cref="ManageTournament"/>: das eine sagt, wer hereindarf, das
    /// andere, wer ein bestimmtes Turnier führt. Nur das erste kann global
    /// sein, denn ein Turnier, das es noch nicht gibt, hat keinen Scope.
    /// </summary>
    CreateTournament,

    /// <summary>
    /// Ein Turnier führen: Stammdaten, Plätze, Meldungen, Draw, Spielplan.
    /// </summary>
    ManageTournament,

    /// <summary>Ergebnisse eintragen und korrigieren.</summary>
    EnterResults,

    /// <summary>Nicht-öffentliche Daten sehen: Kontaktdaten, interne Notizen.</summary>
    ViewInternals,
}
