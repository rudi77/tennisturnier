using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TennisTurnier.Application.Common;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Web;

/// <summary>
/// Die gemeinsame Grundlage aller Seiten: fachliche Fehler werden zu einer
/// Meldung, nicht zu einer Fehlerseite.
///
/// Eine Turnierleitung, die um zwei Uhr nachmittags ein Ergebnis einträgt, darf
/// bei „Satz nicht zu Ende gespielt" nicht auf einer weißen Seite landen und
/// alles neu eingeben müssen. Deshalb fängt <see cref="TryAsync"/> genau die
/// Ausnahmen ab, die der <see cref="DomainExceptionHandler"/> für die API auf
/// 4xx abbildet, und lässt alles andere durch — ein Programmierfehler soll
/// weiterhin laut scheitern.
/// </summary>
public abstract class AppPageModel : PageModel
{
    /// <summary>Die Meldung des zuletzt gescheiterten Versuchs.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Führt die Handlung aus und meldet, ob sie durchgegangen ist.
    ///
    /// Setzt im Fehlerfall 422: htmx tauscht dann nichts in die Seite ein, und
    /// der Rumpf landet über den <c>htmx:beforeSwap</c>-Haken im Layout in der
    /// Meldungszeile. Der angezeigte Zustand bleibt also der, den der Server
    /// tatsächlich hat.
    /// </summary>
    protected async Task<bool> TryAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (AccessDeniedException)
        {
            // Wie in der API: kein Hinweis darauf, dass es die Ressource gibt.
            Error = "Nicht gefunden.";
        }
        catch (NotFoundException exception)
        {
            Error = exception.Message;
        }
        catch (ConcurrencyConflictException exception)
        {
            Error = $"{exception.Message} Bitte neu laden.";
        }
        catch (DomainException exception)
        {
            Error = exception.Message;
        }

        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;

        return false;
    }

    /// <summary>Die Antwort auf einen gescheiterten Versuch: nur die Meldung.</summary>
    protected IActionResult ErrorFragment() => Partial("_Toast", Error ?? "Unbekannter Fehler.");
}
