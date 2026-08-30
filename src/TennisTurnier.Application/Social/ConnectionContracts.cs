namespace TennisTurnier.Application.Social;

/// <summary>
/// Jemand, mit dem der Aufrufer gespielt hat (ADR-0013).
///
/// Es gibt keine Freundschaftsanfrage und keine Bestätigung. Der Graph entsteht
/// aus gespielten Matches und ist damit am ersten Tag gefüllt — in dem
/// Augenblick, in dem das erste Ergebnis eingetragen wird. Eine Anfrage wäre ein
/// zweiter Beziehungsbegriff neben dem, der ohnehin entsteht, mit eigenem
/// Zustand, eigener Ablehnung und der Eigenschaft, am Anfang leer zu sein.
/// </summary>
/// <param name="Together">
/// Matches, in denen beide auf derselben Seite standen — im Doppel. Im Einzel
/// gibt es das nicht, und dann steht hier null.
/// </param>
/// <param name="Against">Matches gegeneinander.</param>
/// <param name="Won">Davon gewonnen, aus Sicht des Aufrufers.</param>
/// <param name="LastPlayedOn">
/// Wann zuletzt. Ohne Platzzuweisung gibt es keine Uhrzeit — dann steht hier der
/// Beginn des Turniers, und wo auch der fehlt, gar nichts.
/// </param>
/// <param name="LastTournamentName">
/// Wo zuletzt. Es ist der Anknüpfungspunkt: „gegen Lena beim Clubturnier" sagt
/// mehr als eine Zahl.
/// </param>
public sealed record ConnectionView(
    Guid PlayerId,
    string DisplayName,
    int Together,
    int Against,
    int Won,
    int Lost,
    DateOnly? LastPlayedOn,
    string LastTournamentName,
    int SharedTournaments);
