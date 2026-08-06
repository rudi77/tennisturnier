using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Registration;

/// <summary>
/// Die öffentliche Selbstmeldung.
///
/// Drei Regeln machen den Unterschied zwischen einem funktionierenden Endpunkt
/// und einem, der die Meldung speichert und dem Melder trotzdem 404 antwortet.
/// Sie sind keine Stilfrage:
///
///  1. Das Turnier kommt <em>ausschließlich</em> über
///     <see cref="ITournamentRepository.FindByRegistrationTokenAsync"/>. Jeder
///     zweite Zugriff über den normalen Weg liefe in den Query-Filter, der für
///     einen Anonymen alles ausblendet.
///  2. Kein Neuaufbau der öffentlichen Projektion. Er lädt das Turnier über
///     denselben Filter und würde aus demselben Grund scheitern — und er wäre
///     ohnehin gegenstandslos: gemeldet wird nur im Zustand
///     <c>RegistrationOpen</c>, und dort gibt es keine Projektion.
///  3. Kein <c>FlushAsync</c>. Es leert den ChangeTracker; die eben angelegten
///     Spieler und der Teilnehmer wären danach nicht mehr Teil derselben
///     Arbeitseinheit. Genau ein <c>SaveChangesAsync</c>, am Ende.
/// </summary>
public sealed class RegistrationService : IRegistrationService
{
    private readonly ITournamentRepository _tournaments;
    private readonly ParticipantResolver _participants;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RegistrationService(
        ITournamentRepository tournaments,
        ParticipantResolver participants,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _tournaments = tournaments;
        _participants = participants;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<PublicRegistrationView> GetAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadAsync(token, cancellationToken);

        return new PublicRegistrationView(
            tournament.Name,
            tournament.Venue.Name,
            tournament.Venue.City,
            tournament.StartsOn,
            tournament.EndsOn,
            tournament.Discipline,
            tournament.Discipline.NeedsPartner(),
            IsOpen(tournament),
            FreeSlots(tournament),
            tournament.Registration.Deadline);
    }

    public async Task<SelfRegistrationResult> RegisterAsync(
        string token,
        SelfRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tournament = await LoadAsync(token, cancellationToken);

        // Zustand und Meldeschluss beantworten dieselbe Frage wie ein
        // unbekanntes Token — „über diesen Link geht gerade nichts" — und
        // bekommen deshalb dieselbe Antwort. Was am Formular liegt, wird
        // dagegen benannt: ein 404 auf einen vergessenen Partner wäre für den
        // Melder nicht erklärbar.
        if (!IsOpen(tournament))
        {
            throw new NotFoundException("Anmeldelink");
        }

        var hasPartner = HasPartner(request);
        tournament.RequireMatchesDiscipline(hasPartner);

        var self = await _participants.ResolveAsync(
            request.FirstName, request.LastName, request.Email, request.Phone, cancellationToken);

        var partner = hasPartner
            ? await _participants.ResolveAsync(
                request.PartnerFirstName!,
                request.PartnerLastName!,
                request.PartnerEmail,
                phone: null,
                cancellationToken)
            : null;

        // Idempotenz vor Kapazität: der Doppelklick auf „Absenden" darf nicht
        // beim zweiten Mal auf der Warteliste landen, nur weil der erste das
        // Feld gerade vollgemacht hat.
        var lineups = await _participants.LoadLineupsAsync(tournament, cancellationToken);

        if (lineups.Find(self, partner) is { } existing)
        {
            return new SelfRegistrationResult(existing.ConfirmationCode, existing.Status);
        }

        var participant = _participants.CreateParticipant(self, partner, request.TeamName);

        // Vor dem Melden gezählt: danach zählt die eigene Meldung mit, und ein
        // Feld mit genau einem freien Platz wäre plötzlich voll.
        var full = tournament.Registration.IsFull(tournament.CountAgainstCapacity());

        var entry = tournament.Enter(
            Guid.NewGuid(),
            participant.Id,
            seed: null,
            origin: EntryOrigin.SelfService,
            registeredAt: _clock.Now);

        // Bei erschöpfter Kapazität entsteht die Meldung als Warteliste statt
        // als Fehler. Für den Melder ist das die bessere Antwort — und die
        // Turnierleitung entscheidet ohnehin, wer nachrückt.
        if (full)
        {
            tournament.MoveToWaitingList(entry.Id);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SelfRegistrationResult(entry.ConfirmationCode, entry.Status);
    }

    /// <summary>
    /// Der Tokenweg. Ein unbekanntes Token und ein Turnier, über das gerade
    /// nichts geht, sind von außen nicht zu unterscheiden — sonst wäre der
    /// Endpunkt ein Orakel dafür, welche Token es gibt.
    /// </summary>
    private async Task<Tournament> LoadAsync(string token, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(token)
            ? throw new NotFoundException("Anmeldelink")
            : await _tournaments.FindByRegistrationTokenAsync(token.Trim(), cancellationToken)
              ?? throw new NotFoundException("Anmeldelink");

    private bool IsOpen(Tournament tournament) =>
        tournament.State == TournamentState.RegistrationOpen
        && !tournament.Registration.IsPastDeadline(_clock.Now);

    private static int? FreeSlots(Tournament tournament) =>
        tournament.Registration.Capacity is { } capacity
            ? Math.Max(0, capacity - tournament.CountAgainstCapacity())
            : null;

    private static bool HasPartner(SelfRegistrationRequest request) =>
        !string.IsNullOrWhiteSpace(request.PartnerFirstName)
        || !string.IsNullOrWhiteSpace(request.PartnerLastName);
}
