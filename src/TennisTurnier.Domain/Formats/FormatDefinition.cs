using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Formats;

/// <summary>
/// Die vier Paarungsverfahren aus ADR-0001. Neue <em>Modi</em> entstehen durch
/// Komposition und Parameter; ein neuer Wert hier bedeutet einen genuin neuen
/// Paarungsalgorithmus und damit eine neue <c>IPhaseFormat</c>-Implementierung.
/// </summary>
public enum PhaseFormatKind
{
    Knockout,
    RoundRobin,
    Swiss,
}

/// <summary>Wie die Teilnehmer einer Phase aus der Vorphase hervorgehen.</summary>
public enum QualificationRule
{
    /// <summary>Die besten N jeder Gruppe.</summary>
    TopNPerGroup,

    /// <summary>Zusätzlich die besten Gruppendritten, um das Feld aufzufüllen.</summary>
    BestThirds,

    /// <summary>Alle Teilnehmer der Vorphase, in der Reihenfolge ihrer Platzierung.</summary>
    All,
}

/// <summary>Wie die Qualifikanten auf die Positionen der Folgephase verteilt werden.</summary>
public enum SeedingRule
{
    /// <summary>Gruppensieger treffen auf Zweite anderer Gruppen.</summary>
    CrossGroup,

    /// <summary>Nach Platzierung, ohne Rücksicht auf die Herkunftsgruppe.</summary>
    ByRank,
}

/// <summary>Kriterien zum Auflösen von Punktgleichheit, in der angegebenen Reihenfolge.</summary>
public enum Tiebreaker
{
    /// <summary>Direkter Vergleich der punktgleichen Teilnehmer untereinander.</summary>
    DirectEncounter,

    SetRatio,

    GameRatio,

    /// <summary>Buchholz — Summe der Punkte aller Gegner. Vor allem im Schweizer System.</summary>
    Buchholz,

    /// <summary>Losentscheid. Muss als letztes Kriterium stehen, sonst bleibt ein Patt möglich.</summary>
    Lot,
}

/// <summary>Wie ein Entscheidungssatz gespielt wird.</summary>
public enum FinalSetMode
{
    /// <summary>Wie jeder andere Satz.</summary>
    Regular,

    /// <summary>Match-Tiebreak bis 10 statt eines dritten Satzes.</summary>
    MatchTiebreak10,

    /// <summary>Durchspielen mit zwei Spielen Vorsprung, ohne Tiebreak.</summary>
    Advantage,
}

/// <summary>
/// Wie ein einzelnes Match gespielt wird. Gilt für das gesamte Turnier, kann
/// aber je Phase überschrieben werden — Gruppenspiele auf Match-Tiebreak, das
/// Finale über drei volle Sätze, ist eine verbreitete Kombination.
/// </summary>
public sealed record MatchFormat(
    int BestOf = 3,
    FinalSetMode FinalSetMode = FinalSetMode.MatchTiebreak10,
    int TiebreakAt = 6)
{
    public void Validate(string path)
    {
        if (BestOf is not (1 or 3 or 5))
        {
            throw new DomainException($"{path}: bestOf muss 1, 3 oder 5 sein, war {BestOf}.");
        }

        if (TiebreakAt is < 1 or > 12)
        {
            throw new DomainException($"{path}: tiebreakAt muss zwischen 1 und 12 liegen, war {TiebreakAt}.");
        }
    }

    /// <summary>Sätze, die zum Sieg genügen.</summary>
    public int SetsToWin => (BestOf / 2) + 1;
}

/// <summary>Punkte für die Tabelle einer Gruppen- oder Ligaphase.</summary>
public sealed record ScoringRules(int Win = 2, int Loss = 0, int Walkover = 0);

/// <summary>Woher die Teilnehmer einer Phase kommen.</summary>
public sealed record Qualification(
    int FromPhase,
    QualificationRule Rule,
    int N = 2,
    SeedingRule Seeding = SeedingRule.CrossGroup)
{
    public void Validate(string path, int ownOrdinal)
    {
        if (FromPhase < 1 || FromPhase >= ownOrdinal)
        {
            throw new DomainException(
                $"{path}: qualification.from muss auf eine frühere Phase zeigen, war {FromPhase} bei Phase {ownOrdinal}.");
        }

        // BestThirds wertet N ebenso aus. Nur TopNPerGroup zu prüfen hieße, eine
        // Definition mit negativem N durchzulassen — und sie würde beim Auslosen
        // unverändert eingefroren.
        if (Rule is QualificationRule.TopNPerGroup or QualificationRule.BestThirds && N < 1)
        {
            throw new DomainException($"{path}: qualification.n muss mindestens 1 sein, war {N}.");
        }
    }
}

/// <summary>Eine Phase des Turniers.</summary>
public sealed record PhaseDefinition
{
    public required int Ordinal { get; init; }

    public required PhaseFormatKind Format { get; init; }

    public string? Name { get; init; }

    /// <summary>Nur in der ersten Phase leer — dort kommen die Teilnehmer aus der Meldeliste.</summary>
    public Qualification? Qualification { get; init; }

    /// <summary>Anzahl Gruppen. Nur für <see cref="PhaseFormatKind.RoundRobin"/>.</summary>
    public int GroupCount { get; init; } = 1;

    /// <summary>Hin- oder Hin- und Rückrunde. Nur für <see cref="PhaseFormatKind.RoundRobin"/>.</summary>
    public int Encounters { get; init; } = 1;

    /// <summary>Rundenzahl im Schweizer System. Leer bedeutet <c>ceil(log2(n))</c>.</summary>
    public int? Rounds { get; init; }

    /// <summary>Spiel um Platz 3. Nur für <see cref="PhaseFormatKind.Knockout"/>.</summary>
    public bool ThirdPlaceMatch { get; init; }

    public ScoringRules Scoring { get; init; } = new();

    public IReadOnlyList<Tiebreaker> Tiebreakers { get; init; } =
        [Tiebreaker.DirectEncounter, Tiebreaker.SetRatio, Tiebreaker.GameRatio, Tiebreaker.Lot];

    /// <summary>Überschreibt das Matchformat des Turniers für diese Phase.</summary>
    public MatchFormat? MatchFormat { get; init; }

    public void Validate()
    {
        var path = $"phases[{Ordinal}]";

        if (Ordinal < 1)
        {
            throw new DomainException($"{path}: ordinal muss mindestens 1 sein.");
        }

        if (Ordinal == 1 && Qualification is not null)
        {
            throw new DomainException(
                $"{path}: die erste Phase bezieht ihre Teilnehmer aus der Meldeliste und darf keine Qualifikation angeben.");
        }

        if (Ordinal > 1 && Qualification is null)
        {
            throw new DomainException(
                $"{path}: jede Phase ab der zweiten braucht eine Qualifikation, sonst ist offen, wer darin spielt.");
        }

        Qualification?.Validate(path, Ordinal);
        MatchFormat?.Validate(path);

        if (Scoring is null)
        {
            throw new DomainException($"{path}: das Punktesystem fehlt.");
        }

        ValidateFormatSpecifics(path);
        ValidateTiebreakers(path);
    }

    private void ValidateFormatSpecifics(string path)
    {
        switch (Format)
        {
            case PhaseFormatKind.RoundRobin when GroupCount < 1:
                throw new DomainException($"{path}: groupCount muss mindestens 1 sein, war {GroupCount}.");

            // Die Gruppen heißen nach den Buchstaben des Alphabets. Danach hießen
            // sie „Gruppe [" und „Gruppe \", und mehr als 26 Gruppen hat ohnehin
            // kein Vereinsturnier.
            case PhaseFormatKind.RoundRobin when GroupCount > 26:
                throw new DomainException($"{path}: mehr als 26 Gruppen sind nicht vorgesehen, waren {GroupCount}.");

            case PhaseFormatKind.RoundRobin when Encounters is not (1 or 2):
                throw new DomainException($"{path}: encounters muss 1 oder 2 sein, war {Encounters}.");

            case PhaseFormatKind.Swiss when Rounds is < 1:
                throw new DomainException($"{path}: rounds muss mindestens 1 sein, war {Rounds}.");

            // Das Schweizer System paart nach Punktestand und braucht dafür in
            // der ersten Runde ein Feld, das feststeht. Als Folgephase bekäme es
            // Gruppenplätze, hinter denen noch niemand steht — es könnte seine
            // erste Runde nicht ansetzen und das Turnier bliebe stehen. Als
            // Vorrunde vor einer Endrunde ist es dagegen vorgesehen.
            case PhaseFormatKind.Swiss when Ordinal > 1:
                throw new DomainException(
                    $"{path}: das Schweizer System paart nach Punktestand und ist deshalb nur als erste Phase " +
                    "vorgesehen, nicht als Endrunde.");

            case PhaseFormatKind.Swiss when GroupCount > 1:
                throw new DomainException($"{path}: das Schweizer System kennt keine Gruppen.");

            case PhaseFormatKind.Knockout when GroupCount > 1:
                throw new DomainException($"{path}: eine K.-o.-Phase kennt keine Gruppen.");
        }
    }

    private void ValidateTiebreakers(string path)
    {
        if (Tiebreakers is null || Tiebreakers.Count == 0)
        {
            throw new DomainException($"{path}: ohne Tiebreaker bliebe Punktgleichheit unauflösbar.");
        }

        if (Tiebreakers.Distinct().Count() != Tiebreakers.Count)
        {
            throw new DomainException($"{path}: ein Tiebreaker darf nur einmal vorkommen.");
        }

        // Ohne Losentscheid am Ende kann die Reihenfolge unentschieden bleiben.
        // Steht er nicht zuletzt, sind alle folgenden Kriterien unerreichbar.
        var lot = Tiebreakers.ToList().IndexOf(Tiebreaker.Lot);
        if (lot >= 0 && lot != Tiebreakers.Count - 1)
        {
            throw new DomainException($"{path}: der Losentscheid muss das letzte Kriterium sein.");
        }
    }
}

/// <summary>
/// Ein Turniermodus als geordnete Folge von Phasen (ADR-0001).
///
/// „Gruppenphase mit anschließendem K.o." ist kein eigenes Format, sondern eine
/// Komposition. Ein eigener Modus entsteht durch eine neue Phasenfolge, nicht
/// durch neuen Code.
/// </summary>
public sealed record FormatDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<PhaseDefinition> Phases { get; init; }

    public MatchFormat MatchFormat { get; init; } = new();

    /// <summary>Das Matchformat der Phase, sonst das des Turniers.</summary>
    public MatchFormat MatchFormatOf(PhaseDefinition phase) => phase.MatchFormat ?? MatchFormat;

    /// <summary>
    /// Prüft die Definition als Ganzes. Wird beim Speichern einer Vorlage und
    /// erneut beim Einfrieren in ein Turnier aufgerufen — eine ungültige
    /// Definition darf nie in ein laufendes Turnier gelangen.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new DomainException("Eine Formatdefinition braucht eine Id.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new DomainException("Eine Formatdefinition braucht einen Namen.");
        }

        // Der Null-Test ist trotz nicht-nullbarer Annotation nötig: eine von Hand
        // geschriebene Definition mit "phases": null landet hier als null, und
        // ein Absturz wäre eine 500 statt einer verständlichen Absage.
        if (Phases is null || Phases.Count == 0)
        {
            throw new DomainException("Eine Formatdefinition braucht mindestens eine Phase.");
        }

        if (Phases.Any(phase => phase is null))
        {
            throw new DomainException("Eine Formatdefinition enthält eine leere Phase.");
        }

        var ordinals = Phases.Select(p => p.Ordinal).ToList();
        if (!ordinals.SequenceEqual(Enumerable.Range(1, Phases.Count)))
        {
            throw new DomainException(
                $"Die Phasen müssen lückenlos mit 1 beginnend nummeriert sein, waren [{string.Join(", ", ordinals)}].");
        }

        if (MatchFormat is null)
        {
            throw new DomainException("Eine Formatdefinition braucht ein Matchformat.");
        }

        MatchFormat.Validate("matchFormat");

        foreach (var phase in Phases)
        {
            phase.Validate();
        }
    }
}
