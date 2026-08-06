namespace TennisTurnier.Application.Tournaments;

/// <summary>
/// Eine hochgeladene Teilnehmerliste, als Text.
///
/// Der Inhalt und nicht die Datei: das Formular liest sie im Browser und
/// schickt den Text. Ein Multipart-Upload brächte hier nichts, was ihn
/// aufwöge — es gibt keine Größenordnung, in der eine Teilnehmerliste nicht in
/// eine Anfrage passt, und ein Textfeld lässt sich einfügen, wenn gar keine
/// Datei zur Hand ist.
/// </summary>
/// <param name="Csv">
/// Spalten, Trennzeichen wahlweise Semikolon, Komma oder Tabulator; eine
/// Kopfzeile wird erkannt und übersprungen.
///
/// <b>Einzel:</b> Vorname, Nachname, E-Mail, Telefon — die letzten beiden
/// freiwillig.
///
/// <b>Doppel und Mixed:</b> Vorname, Nachname, Partner-Vorname,
/// Partner-Nachname, E-Mail, Partner-E-Mail, Teamname — ab der fünften Spalte
/// freiwillig.
///
/// Die Namen stehen vorn und die freiwilligen Angaben hinten, damit sich das
/// Weglassen nicht rächt: „Anna;Müller;Bea;Berger" ist ein vollständiges
/// Doppel, ohne dass jemand leere Felder abzählen müsste.
/// </param>
public sealed record ImportEntriesRequest(string Csv);

/// <summary>
/// Eine Zeile, die nicht durchging.
///
/// Mit Nummer <em>und</em> Wortlaut: die Nummer allein hilft nicht, wenn die
/// Datei inzwischen in einer Tabelle offen ist, in der eine Kopfzeile mitzählt
/// oder Leerzeilen fehlen.
/// </summary>
public sealed record ImportProblem(int Line, string Text, string Reason);

/// <param name="Imported">Neu ins Feld gekommen.</param>
/// <param name="Skipped">
/// Übersprungen, weil dieselbe Aufstellung schon gemeldet war. Ausdrücklich
/// kein Fehler: dieselbe Liste ein zweites Mal hochzuladen ist der Normalfall
/// nach einer Korrektur und soll nichts verdoppeln.
/// </param>
public sealed record ImportEntriesResult(
    int Imported,
    int Skipped,
    IReadOnlyList<ImportProblem> Problems);
