using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der eine Weg, im Test ein Turnier aufzubauen.
///
/// Bis hierher hatte jede Testklasse ihre eigene Fassung derselben zwanzig
/// Zeilen: Verein anlegen, Plätze, Vorlage suchen, Turnier, Meldung öffnen,
/// Spieler, Teilnehmer, Meldung, annehmen, schließen, auslosen. Sie waren nicht
/// gleich, sondern nur ähnlich — und jede Änderung am Weg dorthin musste an elf
/// Stellen nachgezogen werden.
///
/// Genau das steht bevor: der Verein verschwindet als Wurzel, und das Turnier
/// tritt an seine Stelle. Danach ändert sich hier eine Methode statt elf
/// Dateien. Der Aufbau geht bewusst weiterhin über HTTP und nicht über die
/// Repositories — ein Test, der die Datenbank direkt füllt, überspränge genau
/// die Verdrahtung, an der ein Autorisierungsfehler entstünde.
/// </summary>
internal sealed record TurnierWunsch
{
    public string Vorlage { get; init; } = BuiltInFormats.Knockout.Name;

    public string Name { get; init; } = "Clubmeisterschaft";

    /// <summary>Namenspräfix des Vereins; die Eindeutigkeit ergänzt der Aufbau.</summary>
    public string Verein { get; init; } = "TC Test";

    public DateOnly Beginn { get; init; } = new(2026, 5, 16);

    public DateOnly Ende { get; init; } = new(2026, 5, 17);

    /// <summary>Einzelteilnehmer. Bei <see cref="Teams"/> ohne Bedeutung.</summary>
    public int Teilnehmer { get; init; }

    /// <summary>
    /// Ein Eintrag je Doppel, der Wert ist der Teamname. Gesetzt heißt: das Feld
    /// besteht aus Paaren statt aus Einzelspielern.
    /// </summary>
    public IReadOnlyList<string>? Teams { get; init; }

    /// <summary>Setzt jede Meldung auf ihre laufende Nummer — die Tabelle wird damit vorhersagbar.</summary>
    public bool Setzen { get; init; } = true;

    /// <summary>
    /// Spieler mit vollständigen Kontaktdaten. Ohne sie prüfte die
    /// Datensparsamkeit der öffentlichen Ansicht gegen Daten, die es gar nicht
    /// gibt.
    /// </summary>
    public bool Kontaktdaten { get; init; }

    public int Plaetze { get; init; }

    /// <summary>Macht Platz 1 zum Center Court — eine weiche Regel des Spielplans.</summary>
    public bool CenterCourt { get; init; }

    /// <summary>
    /// Öffnungszeiten für jeden Platz, Samstag und Sonntag 8 bis 21 Uhr. Ohne
    /// sie hat kein Platz ein freies Fenster und der Solver nichts, worin er
    /// planen könnte.
    /// </summary>
    public bool Oeffnungszeiten { get; init; }

    public bool Auslosen { get; init; } = true;

    /// <summary>Rechnet einen Spielplanvorschlag und bestätigt ihn unverändert.</summary>
    public bool Spielplan { get; init; }

    /// <summary>Schaltet in den Turniertagbetrieb. Setzt <see cref="Spielplan"/> voraus.</summary>
    public bool Turniertag { get; init; }

    /// <summary>
    /// Stellt die Uhr der Fabrik. Der Turniertag braucht das: ohne sie läge das
    /// Turnier in der Vergangenheit der Systemuhr, und alles, was „ab jetzt"
    /// rechnet, spränge in die Gegenwart.
    /// </summary>
    public DateTimeOffset? Uhr { get; init; }
}

/// <summary>Was der Aufbau hinterlassen hat.</summary>
internal sealed record AufgebautesTurnier(
    HttpClient Admin,
    Guid ClubId,
    Guid TournamentId,
    IReadOnlyList<Guid> CourtIds,
    IReadOnlyList<Guid> EntryIds);

internal static class TurnierAufbau
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Baut ein Turnier so weit auf, wie der Wunsch es verlangt, und tritt dabei
    /// als der angegebene Benutzer auf.
    ///
    /// Der Weg wird nur so weit gegangen, wie etwas zu tun ist: ohne Teilnehmer
    /// wird die Meldung gar nicht erst geöffnet, und das Turnier bleibt im
    /// Entwurf. Ausgelost wird ausdrücklich — ein Wunsch, der auslosen will,
    /// aber kein Feld mitbringt, ist ein Versehen und wird hier laut, nicht erst
    /// im 422 einer Zusicherung, die den Aufbau meinte.
    /// </summary>
    public static async Task<AufgebautesTurnier> NeuesTurnierAsync(
        this TennisTurnierApiFactory factory,
        string benutzer,
        TurnierWunsch? wunsch = null)
    {
        var w = wunsch ?? new TurnierWunsch();

        if (w.Uhr is { } uhr)
        {
            factory.Clock.Now = uhr;
        }

        var feldgroesse = w.Teams?.Count ?? w.Teilnehmer;

        if (w.Auslosen && feldgroesse < 2)
        {
            throw new InvalidOperationException(
                $"Auslosen verlangt mindestens zwei Meldungen, der Wunsch nannte {feldgroesse}. " +
                "Für ein Turnier im Entwurf gehört Auslosen = false dazu.");
        }

        if (w.Turniertag && !w.Spielplan)
        {
            throw new InvalidOperationException(
                "Der Turniertagbetrieb setzt einen bestätigten Spielplan voraus.");
        }

        await factory.GrantAsync(benutzer, Role.SystemAdmin, ResourceScope.Global);
        var admin = factory.CreateClientAs(benutzer);

        var clubId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest($"{w.Verein} {Guid.NewGuid():N}", "Europe/Vienna", null),
            Json));

        var courtIds = await CourtsAsync(admin, clubId, w);

        var templates = await admin.GetFromJsonAsync<List<FormatTemplateSummary>>(
            $"/api/clubs/{clubId}/format-templates", Json);

        var templateId = templates!.Single(t => t.Name == w.Vorlage).Id;

        var tournamentId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            $"/api/clubs/{clubId}/tournaments",
            new CreateTournamentRequest(w.Name, w.Beginn, w.Ende, templateId),
            Json));

        var entryIds = feldgroesse == 0
            ? []
            : await MeldungenAsync(admin, tournamentId, w);

        if (w.Auslosen)
        {
            await GelungenAsync(
                await admin.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null),
                "Meldung schließen");

            await GelungenAsync(
                await admin.PostAsync($"/api/tournaments/{tournamentId}/draw", null),
                $"Auslosen ({feldgroesse} Meldungen)");
        }

        if (w.Spielplan)
        {
            await SpielplanAsync(admin, tournamentId);
        }

        if (w.Turniertag)
        {
            await GelungenAsync(
                await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/match-day", null),
                "Turniertag starten");
        }

        return new AufgebautesTurnier(admin, clubId, tournamentId, courtIds, entryIds);
    }

    private static async Task<IReadOnlyList<Guid>> CourtsAsync(
        HttpClient admin,
        Guid clubId,
        TurnierWunsch w)
    {
        var courtIds = new List<Guid>();

        for (var i = 1; i <= w.Plaetze; i++)
        {
            var courtId = await CreatedIdAsync(await admin.PostAsJsonAsync(
                $"/api/clubs/{clubId}/courts",
                new CreateCourtRequest(
                    $"Platz {i}",
                    CourtSurface.Clay,
                    CourtLocation.Outdoor,
                    IsCenterCourt: w.CenterCourt && i == 1),
                Json));

            courtIds.Add(courtId);

            if (!w.Oeffnungszeiten)
            {
                continue;
            }

            foreach (var day in new[] { DayOfWeek.Saturday, DayOfWeek.Sunday })
            {
                await admin.PostAsJsonAsync(
                    $"/api/clubs/{clubId}/courts/{courtId}/availability",
                    new CreateAvailabilityRequest(
                        day, new TimeOnly(8, 0), new TimeOnly(21, 0), new DateOnly(2026, 1, 1), null),
                    Json);
            }
        }

        return courtIds;
    }

    private static async Task<IReadOnlyList<Guid>> MeldungenAsync(
        HttpClient admin,
        Guid tournamentId,
        TurnierWunsch w)
    {
        await admin.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null);

        var entryIds = new List<Guid>();
        var feldgroesse = w.Teams?.Count ?? w.Teilnehmer;

        for (var i = 1; i <= feldgroesse; i++)
        {
            var request = w.Teams is null
                ? new CreateParticipantRequest(await SpielerAsync(admin, i, w), null)
                : new CreateParticipantRequest(
                    await SpielerAsync(admin, i, w),
                    await SpielerAsync(admin, i, w, partner: true),
                    w.Teams[i - 1]);

            var participant = await (await admin.PostAsJsonAsync("/api/participants", request, Json))
                .Content.ReadFromJsonAsync<ParticipantSummary>(Json);

            var entryId = await CreatedIdAsync(await admin.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/entries",
                new EnterTournamentRequest(participant!.Id, w.Setzen ? i : null),
                Json));

            await admin.PostAsync($"/api/tournaments/{tournamentId}/entries/{entryId}/accept", null);
            entryIds.Add(entryId);
        }

        return entryIds;
    }

    /// <summary>
    /// Ein Spieler. Der Nachname ist zufällig, weil Spieler vereinsübergreifend
    /// existieren (ADR-0008) und über Testklassen hinweg in derselben Datenbank
    /// stehen — ein fester Name machte die Suche mehrdeutig.
    /// </summary>
    private static async Task<Guid> SpielerAsync(
        HttpClient admin,
        int nummer,
        TurnierWunsch w,
        bool partner = false) =>
        await CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/players",
            new CreatePlayerRequest(
                partner ? $"Partner{nummer:00}" : $"Vorname{nummer:00}",
                $"N{Guid.NewGuid():N}"[..10],
                w.Kontaktdaten ? $"privat{nummer}@example.invalid" : null,
                w.Kontaktdaten ? "+43 1 2345678" : null,
                w.Kontaktdaten ? new DateOnly(1990, 3, 14) : null),
            Json));

    /// <summary>
    /// Rechnen und Bestätigen sind zwei Schritte, und das bleiben sie auch im
    /// Testaufbau: der Vorschlag wird unverändert übernommen, damit ein Test,
    /// der später etwas anderes behauptet, es selbst tun muss (ADR-0002).
    /// </summary>
    private static async Task SpielplanAsync(HttpClient admin, Guid tournamentId)
    {
        var proposal = (await (await admin.PostAsync(
            $"/api/tournaments/{tournamentId}/schedule/proposal", null))
            .Content.ReadFromJsonAsync<SchedulePlanResult>(Json))!;

        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            new ConfirmScheduleRequest([.. proposal.Assignments.Select(a => new ConfirmedAssignment(
                a.MatchId, a.CourtId, a.SequenceOnCourt, a.PlannedStart, a.EstimatedDuration))]),
            Json);
    }

    public static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        await GelungenAsync(response, "Anlegen");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Ein gescheiterter Schritt nennt seinen Grund.
    ///
    /// Ein nacktes „Expected NoContent, actual UnprocessableEntity" aus dem
    /// Aufbau heraus ist die unbrauchbarste Fehlermeldung, die es hier geben
    /// kann: der Aufbau steht in jedem Test, und die Antwort trägt die
    /// eigentliche Auskunft im Rumpf.
    /// </summary>
    private static async Task GelungenAsync(HttpResponseMessage response, string schritt)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        Assert.Fail(
            $"{schritt} scheiterte mit {(int)response.StatusCode} {response.StatusCode}: " +
            await response.Content.ReadAsStringAsync());
    }
}
