using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TennisTurnier.Api.Web;
using TennisTurnier.Application.Clubs;

namespace TennisTurnier.Api.Pages.Clubs;

[Authorize]
public sealed class IndexModel : AppPageModel
{
    private readonly IClubService _clubs;

    public IndexModel(IClubService clubs) => _clubs = clubs;

    public IReadOnlyList<ClubSummary> Clubs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Clubs = await _clubs.ListAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(
        string name,
        string timeZoneId,
        string? city,
        CancellationToken cancellationToken)
    {
        var created = await TryAsync(() =>
            _clubs.CreateAsync(new CreateClubRequest(name, timeZoneId, city), cancellationToken));

        if (!created)
        {
            return ErrorFragment();
        }

        await OnGetAsync(cancellationToken);

        return Partial("_ClubList", this);
    }
}
