using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TennisTurnier.Application.PublicView;

namespace TennisTurnier.Api.Pages;

/// <summary>
/// Der Aushang: das Turnier ohne Anmeldung.
///
/// Die Seite liest ausschließlich die Projektion aus ADR-0003 und baut sich
/// nichts selbst zusammen. Das ist der Punkt: was in <see cref="PublicTournamentView"/>
/// kein Feld hat, kann auch hier nicht auf den Bildschirm kommen — es gibt keinen
/// zweiten Weg, auf dem interne Daten öffentlich würden.
/// </summary>
[AllowAnonymous]
public sealed class LiveModel : PageModel
{
    /// <summary>Muss zur Serialisierung in <see cref="PublicViewService"/> passen.</summary>
    private static readonly JsonSerializerOptions PublicJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IPublicViewService _view;

    public LiveModel(IPublicViewService view) => _view = view;

    public Guid Id { get; private set; }

    public PublicTournamentView? Tournament { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        Id = tournamentId;

        await LoadAsync(cancellationToken);

        return Page();
    }

    /// <summary>Nur der Inhalt — dafür fragt die Seite regelmäßig nach.</summary>
    public async Task<IActionResult> OnGetContentAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        Id = tournamentId;

        await LoadAsync(cancellationToken);

        return Partial("_Live", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _view.GetAsync(Id, cancellationToken);
        if (snapshot is null)
        {
            return;
        }

        Tournament = JsonSerializer.Deserialize<PublicTournamentView>(snapshot.Json, PublicJson);
        UpdatedAt = snapshot.UpdatedAt;
    }
}
