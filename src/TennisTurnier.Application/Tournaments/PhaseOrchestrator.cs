using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

/// <summary>
/// Führt ein Turnier über seine Phasengrenzen.
///
/// Nach jedem Ergebnis wird geprüft, ob eine Phase durch ist; ist sie es, werden
/// die Gruppenplätze der Folgephase mit den Qualifizierten besetzt. Das ist
/// derselbe Vorgang wie der vom Viertel- ins Halbfinale, nur eine Ebene höher —
/// und ausdrücklich kein Sonderfall (ADR-0001).
///
/// Er sitzt in der Anwendungsschicht, weil er über mehrere Aggregate hinweg
/// arbeitet: eine Phase kennt die Tabelle einer anderen nicht.
/// </summary>
public static class PhaseOrchestrator
{
    /// <summary>
    /// Reicht die Ergebnisse abgeschlossener Phasen an die jeweils folgende
    /// weiter und setzt an, was anzusetzen ist.
    ///
    /// Läuft über alle Phasen der Reihe nach, nicht nur über die eine, in der
    /// gerade ein Ergebnis stand: eine Gruppenphase kann eine Zwischenrunde
    /// besetzen, deren Freilose sofort die Endrunde entscheiden.
    ///
    /// <paramref name="onCourt"/> nennt die Matches, die am Turniertag schon am
    /// Platz stehen; sie werden auch dann nicht zurückgenommen, wenn ihre Paarung
    /// hinfällig geworden ist. Zurückgegeben werden die Matches, deren Paarung
    /// tatsächlich verworfen wurde — ihre Ansetzungen muss der Aufrufer aus den
    /// Warteschlangen nehmen.
    /// </summary>
    public static IReadOnlyList<Guid> Advance(
        Tournament tournament,
        IReadOnlyList<Phase> phases,
        IReadOnlyDictionary<Guid, string> namesByEntry,
        IReadOnlySet<Guid> onCourt)
    {
        ArgumentNullException.ThrowIfNull(tournament);
        ArgumentNullException.ThrowIfNull(phases);
        ArgumentNullException.ThrowIfNull(namesByEntry);
        ArgumentNullException.ThrowIfNull(onCourt);

        if (tournament.Format?.Definition is not { } definition)
        {
            return [];
        }

        var ordered = phases.OrderBy(phase => phase.Ordinal).ToList();
        var withdrawn = new List<Guid>();

        foreach (var phase in ordered)
        {
            withdrawn.AddRange(Repair(tournament, definition, phase, namesByEntry, onCourt));
        }

        foreach (var phase in ordered)
        {
            var qualification = DefinitionOf(definition, phase)?.Qualification;
            if (qualification is null)
            {
                continue;
            }

            var source = ordered.FirstOrDefault(p => p.Ordinal == qualification.FromPhase);
            if (source is null || DefinitionOf(definition, source) is not { } sourceDefinition)
            {
                continue;
            }

            var sourceState = StateOf(tournament, definition, sourceDefinition, source, namesByEntry);
            var format = PhaseFormats.For(source.Format);

            // Erst wenn die Vorphase vollständig durch ist. Einen Gruppenplatz
            // vorzeitig zu besetzen hieße, eine Tabelle für endgültig zu halten,
            // in der noch gespielt wird.
            //
            // Ist sie es nicht mehr — weil ein Ergebnis zurückgenommen wurde —,
            // werden die Plätze wieder geleert. Sie stehen zu lassen hieße, in
            // der Endrunde weiterspielen zu lassen, während die Gruppenphase
            // wieder offen ist.
            var qualified = format.IsComplete(sourceState)
                ? Qualifier.Resolve(sourceState, format.ComputeStandings(sourceState))
                : new Dictionary<(string Group, int Rank), Guid>();

            phase.ResolveGroupPositions(source.Id, qualified);
        }

        return withdrawn;
    }

    /// <summary>
    /// Bringt eine Phase, die im Verlauf paart, auf den Stand ihrer Ergebnisse:
    /// hinfällige Runden zurücknehmen, die nächste ansetzen.
    ///
    /// Für K.o. und Gruppenphase geschieht hier nichts — sie paaren beim
    /// Auslosen und antworten auf beide Fragen mit „nichts". Das Schweizer System
    /// dagegen kennt seine zweite Runde erst, wenn die erste gespielt ist, und
    /// genau dieser Unterschied ist der Grund, warum die Frage an das Format
    /// geht und nicht an eine Fallunterscheidung hier (ADR-0001).
    /// </summary>
    private static IReadOnlyList<Guid> Repair(
        Tournament tournament,
        FormatDefinition definition,
        Phase phase,
        IReadOnlyDictionary<Guid, string> namesByEntry,
        IReadOnlySet<Guid> onCourt)
    {
        if (DefinitionOf(definition, phase) is not { } phaseDefinition)
        {
            return [];
        }

        var format = PhaseFormats.For(phase.Format);

        var withdrawn = phase.Withdraw(
            format.ObsoletePairings(StateOf(tournament, definition, phaseDefinition, phase, namesByEntry)),
            onCourt);

        // Erst nach dem Zurücknehmen: sonst entstünde die nächste Runde aus einer
        // Tabelle, in der die eben verworfene noch steht.
        phase.AddPairings(format.GeneratePairings(
            StateOf(tournament, definition, phaseDefinition, phase, namesByEntry)));

        return withdrawn;
    }

    /// <summary>
    /// Ist alles gespielt, was zu spielen war?
    ///
    /// Gefragt wird das Format und nicht der abgeleitete Phasenstatus. Der Grund
    /// ist das Schweizer System: es kennt seine nächste Runde erst, wenn die
    /// vorige gespielt ist, und eine Phase, deren einzige angesetzte Runde
    /// durch ist, sähe an ihren Matches abgelesen fertig aus. Nur das Format
    /// weiß, dass noch vier Runden folgen (ADR-0001).
    ///
    /// Eine Phase ohne Definition zählt als offen. Das ist der vorsichtigere
    /// Fehler: ein Turnier fälschlich als abgeschlossen zu melden hieße, dass
    /// niemand mehr ein Ergebnis einträgt.
    /// </summary>
    public static bool IsFinished(
        Tournament tournament,
        IReadOnlyList<Phase> phases,
        IReadOnlyDictionary<Guid, string> namesByEntry)
    {
        ArgumentNullException.ThrowIfNull(tournament);
        ArgumentNullException.ThrowIfNull(phases);
        ArgumentNullException.ThrowIfNull(namesByEntry);

        if (tournament.Format?.Definition is not { } definition || phases.Count == 0)
        {
            return false;
        }

        return phases.All(phase =>
            DefinitionOf(definition, phase) is { } phaseDefinition
            && PhaseFormats.For(phase.Format).IsComplete(
                StateOf(tournament, definition, phaseDefinition, phase, namesByEntry)));
    }

    /// <summary>Die Phasendefinition zu einer angelegten Phase.</summary>
    public static PhaseDefinition? DefinitionOf(FormatDefinition definition, Phase phase)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(phase);

        return definition.Phases.FirstOrDefault(p => p.Ordinal == phase.Ordinal);
    }

    /// <summary>
    /// Der Rechenstand einer Phase — Startplätze, Matchformat und Matches.
    ///
    /// Die eine Stelle, an der aus Aggregaten wird, was die Formate zum Rechnen
    /// brauchen. Gäbe es sie zweimal, liefen Tabelle und Qualifikation
    /// auseinander, sobald jemand die eine anfasst und die andere vergisst.
    /// </summary>
    public static PhaseState StateOf(
        Tournament tournament,
        FormatDefinition definition,
        PhaseDefinition phaseDefinition,
        Phase phase,
        IReadOnlyDictionary<Guid, string> namesByEntry)
    {
        ArgumentNullException.ThrowIfNull(tournament);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(phaseDefinition);
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(namesByEntry);

        return new PhaseState(
            phaseDefinition,
            StartersOf(tournament, phase, namesByEntry),
            phase.Matches);
    }

    /// <summary>
    /// Die Startplätze einer Phase: wer tatsächlich in ihr steht.
    ///
    /// Sie werden aus den Matches zurückgewonnen und nicht aus der Meldeliste
    /// gerechnet. Das ist der Unterschied zwischen „wer sollte hier spielen" und
    /// „wer spielt hier": ein Rückzug nach der Auslosung verändert den Baum
    /// ausdrücklich nicht und wird als Nichtantreten gewertet. Aus der Meldeliste
    /// gerechnet fiele der Zurückgezogene samt seiner Siege aus der
    /// Endplatzierung, stünde aber weiter als Sieger im Baum.
    ///
    /// Vor der Auslosung hat eine Phase keine Matches und damit keine
    /// Startplätze — dann gibt es auch nichts zu rechnen.
    /// </summary>
    private static IReadOnlyList<SeededEntry> StartersOf(
        Tournament tournament,
        Phase phase,
        IReadOnlyDictionary<Guid, string> namesByEntry)
    {
        var seeds = tournament.Entries.ToDictionary(entry => entry.Id, entry => entry.Seed);

        return
        [
            .. phase.Matches
                .SelectMany(match => new[] { match.Side1.EntryId, match.Side2.EntryId })
                .Where(entryId => entryId is not null)
                .Select(entryId => entryId!.Value)
                .Distinct()
                .Select(entryId => new SeededEntry(
                    entryId, seeds.GetValueOrDefault(entryId), Name(namesByEntry, entryId))),
        ];
    }

    /// <summary>
    /// Der Anzeigename einer Meldung. Er entscheidet mit, wer sich bei völliger
    /// Gleichheit qualifiziert — deshalb muss es derselbe sein, der auch in der
    /// Tabelle steht, und nicht ein Ersatz.
    /// </summary>
    private static string Name(IReadOnlyDictionary<Guid, string> namesByEntry, Guid entryId) =>
        namesByEntry.GetValueOrDefault(entryId, entryId.ToString());
}
