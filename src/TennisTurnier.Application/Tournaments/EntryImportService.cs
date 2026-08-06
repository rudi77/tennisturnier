using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

/// <summary>Der Teilnehmer-Import aus einer hochgeladenen Liste.</summary>
public interface IEntryImportService
{
    Task<ImportEntriesResult> ImportAsync(
        Guid tournamentId,
        ImportEntriesRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Welche Spalte was bedeutet.
///
/// Die Zuordnung steht hier und nicht als Zahlen im Ablauf: dort las sie sich
/// als <c>row.At(4)</c> mit einer Fallunterscheidung daneben, und die Prosa in
/// <see cref="ImportEntriesRequest"/> war eine zweite, unabhängige Fassung
/// derselben Aussage. Jetzt ist sie eine.
///
/// Im Doppel stehen beide Namenspaare vorn und die freiwilligen Angaben hinten,
/// damit sich das Weglassen nicht rächt: „Anna;Müller;Bea;Berger" ist ein
/// vollständiges Doppel, ohne dass jemand leere Felder abzählen müsste.
/// </summary>
internal readonly record struct EntryColumns(CsvRow Row, bool NeedsPartner)
{
    public string FirstName => Row.At(0);

    public string LastName => Row.At(1);

    public string PartnerFirstName => NeedsPartner ? Row.At(2) : string.Empty;

    public string PartnerLastName => NeedsPartner ? Row.At(3) : string.Empty;

    public string Email => Row.At(NeedsPartner ? 4 : 2);

    /// <summary>Nur im Einzel erhoben — im Doppel steht dort der Partner.</summary>
    public string? Phone => NeedsPartner ? null : Row.At(3);

    public string PartnerEmail => NeedsPartner ? Row.At(5) : string.Empty;

    public string? TeamName => NeedsPartner ? Row.At(6) : null;

    /// <summary>
    /// Nennt die Zeile einen Partner? Ein halb ausgefüllter reicht — sonst
    /// führte ein vergessener Nachname zu „braucht einen Partner" statt zu
    /// „braucht einen Nachnamen".
    /// </summary>
    public bool HasPartner => PartnerFirstName.Length > 0 || PartnerLastName.Length > 0;
}

/// <summary>
/// Eine Teilnehmerliste am Stück.
///
/// Der zweite Weg ins Feld, neben dem Anmeldelink: wer sein Feld schon kennt —
/// aus der Vereinsliste, aus dem Vorjahr, aus einer Tabelle —, soll es nicht
/// Zeile für Zeile abtippen müssen.
///
/// Zwei Entscheidungen prägen das Verhalten:
///
///  1. <b>Eine schlechte Zeile kippt nicht die Datei.</b> Wer dreißig Namen
///     hochlädt und beim achtundzwanzigsten einen Tippfehler hat, will keine
///     Absage für alle dreißig. Gültige Zeilen kommen ins Feld, der Rest wird
///     benannt — mit Zeilennummer und Wortlaut, damit er auffindbar ist.
///  2. <b>Importierte Meldungen stehen sofort im Feld.</b> Die Turnierleitung
///     lädt hier ihre eigene Liste hoch; sie danach noch einzeln anzunehmen
///     wäre eine Bestätigung dessen, was sie gerade selbst behauptet hat. Wer
///     Meldungen prüfen will, nimmt den Anmeldelink — dort ist das Annehmen der
///     Sinn der Sache.
/// </summary>
public sealed class EntryImportService : IEntryImportService
{
    /// <summary>
    /// Eine Obergrenze, damit eine versehentlich hochgeladene Datenbank nicht
    /// zeilenweise gegen die Datenbank läuft. Großzügig über jedem Vereins- und
    /// Verbandsturnier.
    /// </summary>
    private const int MaxRows = 512;

    private readonly ITournamentRepository _tournaments;
    private readonly ParticipantResolver _participants;
    private readonly IPublicViewService _publicView;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public EntryImportService(
        ITournamentRepository tournaments,
        ParticipantResolver participants,
        IPublicViewService publicView,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        IClock clock)
    {
        _tournaments = tournaments;
        _participants = participants;
        _publicView = publicView;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<ImportEntriesResult> ImportAsync(
        Guid tournamentId,
        ImportEntriesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tournament = await _tournaments.FindAsync(tournamentId, cancellationToken)
            ?? throw new NotFoundException("Turnier", tournamentId);

        _userContext.Current.Require(
            Permission.ManageTournament,
            ResourceScope.Tournament(tournament.Id));

        // Einmal vorn statt je Zeile: sonst stünde derselbe Satz fünfzigmal im
        // Bericht, und der eigentliche Grund ginge darin unter. Gefragt wird die
        // Domäne — eine zweite Fassung derselben Schranke bliebe stehen, wenn
        // die erste sich ändert.
        tournament.RequireRegistrationOpen();

        var rows = CsvTable.Parse(request.Csv);

        if (rows.Count == 0)
        {
            throw new DomainException(
                "Die Datei enthält keine Zeile, die sich als Teilnehmer lesen ließe.");
        }

        if (rows.Count > MaxRows)
        {
            throw new DomainException(
                $"Die Datei enthält {rows.Count} Zeilen; mehr als {MaxRows} nimmt ein Import nicht an.");
        }

        // Einmal geladen und dann fortgeschrieben. Als Abfrage je Zeile lud der
        // k-te Name alle k-1 vorherigen Teilnehmer erneut — und fand die eben
        // angelegten ohnehin nicht, weil sie bis zum Speichern nur im
        // Änderungsverfolger stehen.
        var lineups = await _participants.LoadLineupsAsync(tournament, cancellationToken);

        var needsPartner = tournament.Discipline.NeedsPartner();
        var problems = new List<ImportProblem>();
        var imported = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            try
            {
                if (await ImportRowAsync(
                    tournament, new EntryColumns(row, needsPartner), lineups, cancellationToken))
                {
                    imported++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (DomainException failure)
            {
                // Was diese Zeile angelegt hat, zählt nicht: sonst blieben ihre
                // Spieler samt Kontaktdaten in der Datenbank stehen — ohne
                // Teilnehmer, ohne Meldung, und bei jedem neuen Versuch einer mehr.
                _participants.Discard();

                // Nur die fachliche Absage wird zur Zeile gemeldet. Alles andere
                // ist ein Fehler dieses Systems und darf nicht als „Zeile 12 ist
                // krumm" beim Hochladenden landen.
                problems.Add(new ImportProblem(row.Line, row.Text, failure.Message));
            }
        }

        if (imported > 0)
        {
            await _unitOfWork.FlushAsync(cancellationToken);
            await _publicView.RebuildAsync(tournament.Id, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ImportEntriesResult(imported, skipped, problems);
    }

    /// <summary>
    /// Eine E-Mail-Spalte enthält eine E-Mail oder nichts.
    ///
    /// Bewusst nur das @-Zeichen und keine Adressprüfung: geprüft wird hier
    /// nicht die Zustellbarkeit, sondern ob die Spalte das ist, wofür die
    /// Zuordnung sie hält.
    /// </summary>
    private static void RequireEmailOrNothing(string value, string column)
    {
        if (value.Length > 0 && !value.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainException(
                $"In der Spalte {column} steht „{value}“ und damit keine E-Mail-Adresse. " +
                "Stimmt die Spaltenreihenfolge — ist das vielleicht eine Doppelliste?");
        }
    }

    /// <summary>
    /// Eine Zeile. Gibt zurück, ob daraus eine neue Meldung entstanden ist —
    /// <c>false</c> heißt: dieselbe Aufstellung steht bereits im Feld.
    /// </summary>
    private async Task<bool> ImportRowAsync(
        Tournament tournament,
        EntryColumns columns,
        EnteredLineups lineups,
        CancellationToken cancellationToken)
    {
        // Die Ausschreibung entscheidet, ob ein Partner dazugehört — dieselbe
        // Prüfung wie bei der Selbstmeldung und bei der Handeingabe.
        tournament.RequireMatchesDiscipline(columns.HasPartner);

        // Im Einzel steht in der dritten Spalte eine E-Mail. Ein Name dort heißt
        // fast immer: hier wurde eine Doppelliste in ein Einzelturnier geladen.
        // Ungeprüft landete „Bea“ als E-Mail und „Berger“ als Telefonnummer am
        // Spieler — und „Bea“ wurde damit zum Schlüssel, nach dem jede spätere
        // Meldung diesen Menschen sucht.
        RequireEmailOrNothing(columns.Email, "E-Mail");
        RequireEmailOrNothing(columns.PartnerEmail, "Partner-E-Mail");

        var self = await _participants.ResolveAsync(
            columns.FirstName, columns.LastName, columns.Email, columns.Phone, cancellationToken);

        Player? partner = columns.HasPartner
            ? await _participants.ResolveAsync(
                columns.PartnerFirstName,
                columns.PartnerLastName,
                columns.PartnerEmail,
                phone: null,
                cancellationToken)
            : null;

        if (lineups.Find(self, partner) is not null)
        {
            // Auch die übersprungene Zeile hinterlässt nichts: sie hat unter
            // Umständen einen Spieler aufgeloest, der noch nicht existierte.
            _participants.Discard();
            return false;
        }

        // Zwei gleiche Spieler weist Participant.Team von sich aus ab, mit
        // demselben Satz — die Absage landet über denselben Weg im Bericht.
        var participant = _participants.CreateParticipant(self, partner, columns.TeamName);

        var entry = tournament.Enter(Guid.NewGuid(), participant.Id, registeredAt: _clock.Now);
        tournament.Accept(entry.Id);

        lineups.Remember(self, partner, entry);
        _participants.Commit();

        return true;
    }
}
