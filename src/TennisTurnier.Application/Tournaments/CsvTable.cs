using System.Text;

namespace TennisTurnier.Application.Tournaments;

/// <summary>Eine gelesene Zeile samt ihrer Nummer in der Datei — für die Rückmeldung.</summary>
public sealed record CsvRow(int Line, string Text, IReadOnlyList<string> Cells)
{
    /// <summary>Die Zelle an dieser Stelle, getrimmt. Leer, wenn es sie nicht gibt.</summary>
    public string At(int index) => index < Cells.Count ? Cells[index].Trim() : string.Empty;
}

/// <summary>
/// Ein genügsamer CSV-Leser für hochgeladene Teilnehmerlisten.
///
/// Genügsam heißt: kein RFC-4180-Anspruch, aber alles, was aus Excel, Numbers
/// und einem Texteditor tatsächlich herauskommt. Der Trennzeichenstreit
/// zwischen deutschem Excel (Semikolon) und dem Rest der Welt (Komma) ist der
/// häufigste Grund, warum eine Liste beim ersten Versuch nicht durchgeht —
/// deshalb wird er nicht dem Hochladenden überlassen, sondern erraten.
///
/// Eine eigene Abhängigkeit wäre für diese Aufgabe ein schlechter Tausch: was
/// hier fehlt (mehrzeilige Felder in Anführungszeichen), kommt in einer
/// Teilnehmerliste nicht vor, und was zählt — Trennzeichen erraten, BOM,
/// Kopfzeile, Zeilennummern für die Fehlermeldung — müsste man ohnehin
/// darumherum bauen.
/// </summary>
public static class CsvTable
{
    private static readonly char[] Candidates = [';', ',', '\t'];

    /// <summary>
    /// Wörter, an denen eine Kopfzeile zu erkennen ist. Sie wird übersprungen —
    /// wer seine Liste aus einer Tabelle exportiert, hat fast immer eine, und
    /// „Vorname Nachname" als Teilnehmer anzulegen ist keine hilfreiche Antwort.
    /// </summary>
    private static readonly string[] HeaderWords =
        ["vorname", "firstname", "first name", "name", "nachname", "lastname", "last name"];

    public static IReadOnlyList<CsvRow> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        // Ein BOM steht sonst im ersten Vornamen und lässt ihn an keiner
        // Namensgleichheit mehr teilnehmen — unsichtbar und deshalb besonders
        // ärgerlich.
        var text = content.TrimStart('﻿');
        var lines = text.Split('\n');
        var separator = DetectSeparator(lines);

        var rows = new List<CsvRow>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = Split(line, separator);

            // Die Kopfzeile nur ganz vorn: „Name" mitten in der Datei ist ein
            // Teilnehmer, der so heißt, und keine zweite Überschrift.
            if (rows.Count == 0 && IsHeader(cells))
            {
                continue;
            }

            rows.Add(new CsvRow(index + 1, line.Trim(), cells));
        }

        return rows;
    }

    /// <summary>
    /// Das Trennzeichen, das am gleichmäßigsten trennt.
    ///
    /// Gezählt wird über alle Zeilen: ein Komma im Ortsnamen einer einzelnen
    /// Zeile soll die Datei nicht umdeuten. Bei Gleichstand gewinnt das
    /// Semikolon — deutsches Excel ist hier die häufigste Quelle.
    /// </summary>
    private static char DetectSeparator(IReadOnlyList<string> lines) =>
        Candidates.MaxBy(candidate => lines.Sum(line => line.Count(c => c == candidate)));

    /// <summary>
    /// Zerlegt eine Zeile. Anführungszeichen schützen das Trennzeichen, ein
    /// verdoppeltes Anführungszeichen steht für sich selbst.
    /// </summary>
    private static List<string> Split(string line, char separator)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == separator && !quoted)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString());
        return cells;
    }

    // Split hängt immer eine letzte Zelle an, und leere Zeilen sind vorher
    // aussortiert — cells[0] gibt es also.
    private static bool IsHeader(IReadOnlyList<string> cells) =>
        HeaderWords.Contains(cells[0].Trim().Trim('"'), StringComparer.OrdinalIgnoreCase);
}
