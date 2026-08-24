namespace TennisTurnier.Domain.Tournaments;

/// <summary>
/// Woher die Paare eines Doppels kommen.
///
/// Zwei Turniere, die sich für den Melder grundlegend unterscheiden. Beim
/// Vereinsdoppel bringt jeder seinen Partner mit und meldet als Paar. Beim
/// Schleiferl- oder Mixed-Abend meldet sich jeder für sich, und wer mit wem
/// spielt, entscheidet die Turnierleitung — oft per Los, manchmal von Hand,
/// weil die Stärken zusammenpassen sollen.
///
/// Bislang gab es nur den ersten Fall: eine Doppelmeldung ohne Partner wies das
/// Turnier ab. Das zwang den zweiten Fall in eine Form, die er nicht hat — die
/// Turnierleitung musste Paare erfinden, bevor jemand gemeldet war.
///
/// Im Einzel hat die Angabe keine Bedeutung; dort gibt es nichts zu bilden.
/// </summary>
public enum TeamFormation
{
    /// <summary>
    /// Die Meldenden bringen ihren Partner mit. Eine Meldung ist ein Paar.
    /// </summary>
    Registered,

    /// <summary>
    /// Es meldet sich jeder für sich; die Turnierleitung bildet die Teams —
    /// ausgelost oder von Hand. Erst danach lässt sich auslosen: eine
    /// Einzelmeldung ohne Team hat im Draw eines Doppels nichts verloren.
    /// </summary>
    ByOrganiser,
}
