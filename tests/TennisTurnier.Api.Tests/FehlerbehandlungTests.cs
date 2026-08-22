using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TennisTurnier.Application.Common;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Wie eine Ausnahme beim Aufrufer ankommt.
///
/// Die Zuordnung entscheidet, was die Oberfläche tut: 409 heißt neu laden und
/// wiederholen, 422 heißt die Eingabe ändern, 404 heißt aufhören. Ein
/// unbekannter Fehler darf sich in keine dieser Antworten verkleiden — er
/// gehört als Serverfehler an die Rahmenbehandlung zurück, sonst probiert die
/// Oberfläche etwas zu reparieren, das kaputt ist.
/// </summary>
public sealed class FehlerbehandlungTests
{
    /// <summary>Schreibt nichts, merkt sich aber, was geschrieben worden wäre.</summary>
    private sealed class MitschriftDerProbleme : IProblemDetailsService
    {
        internal ProblemDetails? Letztes { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Letztes = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Letztes = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }
    }

    private static async Task<(bool Behandelt, int Status, ProblemDetails? Problem)> BehandeltAsync(
        Exception exception)
    {
        var probleme = new MitschriftDerProbleme();
        var handler = new DomainExceptionHandler(
            NullLogger<DomainExceptionHandler>.Instance, probleme);

        var context = new DefaultHttpContext();
        var behandelt = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        return (behandelt, context.Response.StatusCode, probleme.Letztes);
    }

    [Fact]
    public async Task Ein_verweigerter_Zugriff_sieht_aus_wie_nicht_vorhanden()
    {
        var (behandelt, status, problem) = await BehandeltAsync(
            new AccessDeniedException(Permission.ManageTournament, [ResourceScope.Global]));

        Assert.True(behandelt);
        Assert.Equal(StatusCodes.Status404NotFound, status);

        // Der Grund gehört ins Protokoll, nicht in die Antwort: sonst verriete
        // sie, was es zu sehen gäbe.
        Assert.Equal("Die angeforderte Ressource existiert nicht.", problem!.Detail);
    }

    [Fact]
    public async Task Eine_fehlende_Ressource_nennt_sich()
    {
        var id = Guid.NewGuid();

        var (behandelt, status, problem) = await BehandeltAsync(new NotFoundException("Turnier", id));

        Assert.True(behandelt);
        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Contains(id.ToString(), problem!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_Nebenlaeufigkeitskonflikt_bittet_um_Wiederholung()
    {
        var (behandelt, status, _) = await BehandeltAsync(
            new ConcurrencyConflictException(new InvalidOperationException("zu spät")));

        Assert.True(behandelt);
        Assert.Equal(StatusCodes.Status409Conflict, status);
    }

    [Fact]
    public async Task Eine_fachliche_Regel_kommt_als_422()
    {
        var (behandelt, status, problem) = await BehandeltAsync(
            new DomainException("Ein Doppel braucht zwei Spieler."));

        Assert.True(behandelt);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, status);
        Assert.Equal("Ein Doppel braucht zwei Spieler.", problem!.Detail);
    }

    [Fact]
    public async Task Ein_unbekannter_Fehler_bleibt_ein_Serverfehler()
    {
        // Nicht behandelt heißt: die Rahmenbehandlung übernimmt und antwortet
        // mit 500. Hier eine Antwort zu erfinden, hieße einen Programmfehler als
        // Eingabefehler auszugeben.
        var (behandelt, _, problem) = await BehandeltAsync(new InvalidOperationException("kaputt"));

        Assert.False(behandelt);
        Assert.Null(problem);
    }
}
