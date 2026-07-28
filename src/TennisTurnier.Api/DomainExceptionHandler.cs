using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TennisTurnier.Application.Common;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api;

/// <summary>
/// Bildet fachliche Fehler auf HTTP-Status ab.
///
/// Der wichtigste Fall ist <see cref="AccessDeniedException"/> → <c>404</c> statt
/// <c>403</c>: ein 403 bestätigt die Existenz einer Ressource, die der Aufrufer
/// nicht sehen darf (ADR-0004).
/// </summary>
internal sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DomainExceptionHandler> _logger;
    private readonly IProblemDetailsService _problemDetails;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> logger, IProblemDetailsService problemDetails)
    {
        _logger = logger;
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            AccessDeniedException denied => Denied(denied),
            NotFoundException notFound => NotFound(notFound),
            ConcurrencyConflictException conflict => Conflict(conflict),
            DomainException domain => Unprocessable(domain),
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private ProblemDetails Denied(AccessDeniedException exception)
    {
        // Der Grund gehört ins Log, nicht in die Antwort — sonst verrät die
        // Fehlermeldung, was es zu sehen gäbe.
        _logger.LogInformation(
            "Zugriff verweigert: {Permission} in [{Scopes}].",
            exception.Permission,
            string.Join(", ", exception.Scopes));

        return new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Nicht gefunden",
            Detail = "Die angeforderte Ressource existiert nicht.",
        };
    }

    private static ProblemDetails NotFound(NotFoundException exception) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title = "Nicht gefunden",
        Detail = exception.Message,
    };

    /// <summary>
    /// Zwei Eingaben zum selben Match sind am Turniertag der Normalfall. Der
    /// Aufrufer soll neu laden und es wiederholen — dafür ist 409 da.
    /// </summary>
    private static ProblemDetails Conflict(ConcurrencyConflictException exception) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Zwischenzeitlich geändert",
        Detail = exception.Message,
    };

    private static ProblemDetails Unprocessable(DomainException exception) => new()
    {
        Status = StatusCodes.Status422UnprocessableEntity,
        Title = "Fachliche Regel verletzt",
        Detail = exception.Message,
    };
}
