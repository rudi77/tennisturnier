namespace TennisTurnier.Domain.Tournaments;

/// <summary>
/// Der Lebenszyklus eines Turniers.
///
/// <code>
/// Draft → RegistrationOpen → RegistrationClosed → DrawGenerated
///       → InProgress → Completed
///                    ↘ Abandoned
/// </code>
///
/// Der entscheidende Übergang ist <see cref="DrawGenerated"/>: ab dort sind
/// Teilnehmerliste und Format eingefroren. Eine Nachmeldung erfordert den
/// ausdrücklichen Rückschritt, der den Draw verwirft — niemals stillschweigend.
/// </summary>
public enum TournamentState
{
    /// <summary>Angelegt, noch nicht ausgeschrieben.</summary>
    Draft,

    /// <summary>Meldungen werden angenommen.</summary>
    RegistrationOpen,

    /// <summary>Meldeschluss vorbei, Setzliste kann gesetzt werden.</summary>
    RegistrationClosed,

    /// <summary>Auslosung steht. Teilnehmer und Format sind eingefroren.</summary>
    DrawGenerated,

    /// <summary>Das erste Match hat begonnen.</summary>
    InProgress,

    /// <summary>Alle Phasen abgeschlossen.</summary>
    Completed,

    /// <summary>Abgebrochen — Wetter, zu wenige Meldungen, Absage.</summary>
    Abandoned,
}

/// <summary>
/// Betriebsmodus des Spielplans (ADR-0002).
///
/// Der Unterschied ist keine Kosmetik: im Planungsmodus ist eine Uhrzeit eine
/// Schätzung, am Turniertag eine Zusage. Wer beides gleich darstellt, erzeugt
/// genau die Verwirrung, die ADR-0002 als Konsequenz benennt.
/// </summary>
public enum SchedulingMode
{
    /// <summary>Zeitraster mit geschätzten Dauern. Zweck ist Machbarkeit und Aushang.</summary>
    Planning,

    /// <summary>Court-Queues. Ein Match hat eine früheste Startzeit, keine feste.</summary>
    MatchDay,
}

/// <summary>Stand einer einzelnen Meldung.</summary>
public enum EntryStatus
{
    /// <summary>Gemeldet, noch nicht bestätigt.</summary>
    Applied,

    /// <summary>Im Feld.</summary>
    Accepted,

    /// <summary>Auf der Warteliste, rückt bei einer Absage nach.</summary>
    WaitingList,

    /// <summary>Zurückgezogen.</summary>
    Withdrawn,

    /// <summary>
    /// Gemeldet und einem Team zugeschlagen. Im Draw steht das Team, nicht
    /// diese Meldung — sie bleibt trotzdem bestehen: sie ist die Meldung eines
    /// Menschen, samt seinem Bestätigungscode und seinem Meldezeitpunkt. Wer
    /// sie zurückzöge, nähme ihm den Weg zu seiner eigenen Anmeldung.
    ///
    /// Steht hinten, weil der Wert über die Schnittstelle als Zahl geht: eine
    /// Einfügung in der Mitte verschöbe die bestehenden.
    /// </summary>
    Paired,
}
