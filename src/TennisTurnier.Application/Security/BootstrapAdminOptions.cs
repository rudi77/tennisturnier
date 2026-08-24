namespace TennisTurnier.Application.Security;

/// <summary>
/// Die Systemadministratoren, die aus der Konfiguration kommen statt aus der
/// Datenbank.
///
/// Nur für den ersten: wer einmal angemeldet ist und die Rolle hat, vergibt
/// weitere fachlich. Ein dauerhaft gepflegter Eintrag hier wäre eine zweite,
/// stille Quelle für Berechtigungen — genau das, was ADR-0007 vermeiden will.
/// </summary>
public sealed class BootstrapAdminOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// E-Mail-Adressen oder Subject-IDs. Wer hier steht, bekommt bei seiner
    /// nächsten Anmeldung die globale Rolle <c>SystemAdmin</c>.
    /// </summary>
    public IList<string> BootstrapSystemAdmins { get; set; } = [];

    /// <summary>
    /// Darf jeder angemeldete Benutzer Turniere anlegen?
    ///
    /// Vorgabe ja — das ist der Zweck: wer ein Turnier veranstalten will, soll
    /// es ausschreiben können, ohne dass ihn zuvor jemand freischaltet. Eine
    /// Instanz, die geschlossen laufen soll, schaltet es hier ab; die Rolle
    /// vergibt dann ein Systemadministrator.
    /// </summary>
    public bool SelfServiceOrganizers { get; set; } = true;

    /// <summary>
    /// Betrieb ganz ohne Anmeldung.
    ///
    /// Gedacht für den ersten Schritt: eine Instanz steht, bevor ein Identity
    /// Provider steht. Jeder Aufruf gilt dann als derselbe Benutzer, und der
    /// ist Systemadministrator — wer die Adresse kennt, darf alles, auch
    /// löschen. Das ist keine Lücke, sondern die Ansage; deshalb steht sie in
    /// der Oberfläche und im Protokoll, statt still zu wirken.
    ///
    /// Ausdrücklich einzuschalten und nicht aus einer fehlenden Authority
    /// abzuleiten: „kein Aussteller konfiguriert" heißt sonst plötzlich „offen
    /// für alle", und genau dieser Wechsel darf niemandem versehentlich
    /// passieren. Beides zusammen — Anmeldung und offener Betrieb — lehnt die
    /// Anwendung beim Start ab.
    /// </summary>
    public bool OpenAccess { get; set; }
}
