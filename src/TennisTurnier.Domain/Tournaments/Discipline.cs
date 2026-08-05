namespace TennisTurnier.Domain.Tournaments;

/// <summary>
/// Was gespielt wird.
///
/// Bislang ergab sich das nur daraus, was die Turnierleitung als Teilnehmer
/// anlegte — ein Doppelturnier war eines, dessen Teilnehmer zufällig Paare
/// waren. Das trug, solange nur die Turnierleitung meldete. Sobald sich jemand
/// selbst meldet, muss die Ausschreibung sagen, ob ein Partner dazugehört:
/// sonst entscheidet der erste Melder, was für ein Turnier es wird.
/// </summary>
public enum Discipline
{
    Singles,

    Doubles,

    /// <summary>
    /// Ein Doppel mit gemischten Paaren. Für Meldung und Spielplan verhält es
    /// sich wie <see cref="Doubles"/> — die Paarungsregel ist eine Sache der
    /// Ausschreibung und wird hier nicht geprüft.
    /// </summary>
    Mixed,
}

public static class Disciplines
{
    /// <summary>Braucht eine Meldung in dieser Disziplin einen Partner?</summary>
    public static bool NeedsPartner(this Discipline discipline) =>
        discipline is Discipline.Doubles or Discipline.Mixed;
}
