using Microsoft.AspNetCore.Mvc.RazorPages;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Tournaments;

namespace TennisTurnier.Api.Pages;

/// <summary>
/// Der Einstieg: alle Turniere, die der angemeldete Benutzer sehen darf.
///
/// Die Liste kommt aus den Vereinen und nicht aus einer eigenen Abfrage, weil
/// der Query-Filter aus ADR-0004 an den Vereinen hängt — was hier fehlt, fehlt
/// also aus einem Grund.
/// </summary>
public sealed class IndexModel : PageModel
{
    private readonly IClubService _clubs;
    private readonly ITournamentService _tournaments;

    public IndexModel(IClubService clubs, ITournamentService tournaments)
    {
        _clubs = clubs;
        _tournaments = tournaments;
    }

    public IReadOnlyList<ClubSummary> Clubs { get; private set; } = [];

    public IReadOnlyList<(ClubSummary Club, TournamentSummary Tournament)> Tournaments { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        Clubs = await _clubs.ListAsync(cancellationToken);

        var rows = new List<(ClubSummary, TournamentSummary)>();
        foreach (var club in Clubs)
        {
            foreach (var tournament in await _tournaments.ListAsync(club.Id, cancellationToken))
            {
                rows.Add((club, tournament));
            }
        }

        Tournaments = rows
            .OrderByDescending(r => r.Item2.StartsOn)
            .ThenBy(r => r.Item2.Name, StringComparer.CurrentCulture)
            .ToList();
    }
}
