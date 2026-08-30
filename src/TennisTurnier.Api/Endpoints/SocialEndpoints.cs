using TennisTurnier.Application.Social;

namespace TennisTurnier.Api.Endpoints;

/// <summary>
/// Die Endpunkte, die aus MATCHDAY mehr machen als eine Turnierverwaltung:
/// Profile, Verbindungen und Verabredungen.
///
/// Alle verlangen ein Konto. Das ist keine zusätzliche Schranke, sondern die
/// Feststellung, dass es hier ohne eines nichts zu holen gäbe: die Sichtbarkeit
/// hängt an den Turnieren des Aufrufers (ADR-0013), und wer nicht angemeldet
/// ist, hat keine. Ein anonymer Aufruf bekäme auf jede dieser Adressen ein 404
/// — ein 401 sagt ihm wenigstens, woran es liegt.
/// </summary>
internal static class SocialEndpoints
{
    public static IEndpointRouteBuilder MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        MapProfiles(app);

        return app;
    }

    private static void MapProfiles(IEndpointRouteBuilder app)
    {
        var profiles = app.MapGroup("/api").WithTags("Profil").RequireAuthorization();

        // Unter /players/{id}/profile und nicht unter /players/{id}: dort steht
        // seit ADR-0008 die Auskunft mit Kontaktdaten, und die hängt an einem
        // Turnier. Zwei Auskünfte über denselben Spieler mit verschiedenen
        // Regeln sollen auch verschieden heißen.
        profiles.MapGet("/players/{playerId:guid}/profile", async (
            Guid playerId, IPlayerProfileService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(playerId, ct)));

        // „Noch keiner" ist eine gültige Antwort und kein Fehler — wer
        // beigetreten ist, ohne je zu melden, hat keinen Spieler. Dieselbe
        // Form wie /api/me daneben.
        profiles.MapGet("/me/profile", async (IPlayerProfileService service, CancellationToken ct) =>
            await service.GetMineAsync(ct) is { } profile
                ? Results.Ok(profile)
                : Results.NoContent());

        // Antwortet mit dem fertigen Profil und nicht mit 204: dieser Aufruf
        // kann den Spieler anlegen, und der Aufrufer erfährt hier seine Id,
        // ohne ein zweites Mal zu fragen.
        profiles.MapPut("/me/profile", async (
            UpdateMyProfileRequest request,
            IPlayerProfileService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateMineAsync(request, ct)));
    }
}
