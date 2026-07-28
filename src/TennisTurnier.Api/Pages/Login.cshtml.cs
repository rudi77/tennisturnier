using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TennisTurnier.Adapters.Identity.Oidc;
using TennisTurnier.Api.Web;
using TennisTurnier.Application.Ports;

namespace TennisTurnier.Api.Pages;

/// <summary>
/// Die Anmeldung der Entwicklungsumgebung.
///
/// Sie steht nur, solange kein Identity Provider konfiguriert ist — sonst
/// antwortet die Seite mit 404, statt neben der echten Anmeldung eine zweite
/// offenzuhalten.
/// </summary>
public sealed class LoginModel : PageModel
{
    private readonly IUserDirectory _directory;
    private readonly OidcOptions _oidc;

    public LoginModel(IUserDirectory directory, IConfiguration configuration)
    {
        _directory = directory;
        _oidc = configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();
    }

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    public string? Error { get; private set; }

    public IActionResult OnGet() => _oidc.IsConfigured ? NotFound() : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (_oidc.IsConfigured)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "Bitte einen Namen angeben.";
            return Page();
        }

        var principal = await DevAuthentication.SignInPrincipalAsync(_directory, Name, cancellationToken);
        await HttpContext.SignInAsync(DevAuthentication.SchemeName, principal);

        return RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        if (!_oidc.IsConfigured)
        {
            await HttpContext.SignOutAsync(DevAuthentication.SchemeName);
        }

        return RedirectToPage("/Index");
    }
}
