using System.Globalization;
using System.Text.RegularExpressions;
using Matchday.Domain;
using Matchday.Server.Storage;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.FileProviders;

namespace Matchday.Server.Api;

/// <summary>
/// Die Vorschau eines geteilten Links. WhatsApp, Signal und Co. holen die
/// Seite ab und lesen die Open-Graph-Angaben, ohne ein Skript auszuführen —
/// was die Oberfläche später zeichnet, sehen sie nie. Für den Mitschau-Link
/// schreibt der Server deshalb Turniername und Termin selbst in die Seite, und
/// das Bild bekommt eine absolute Adresse, weil die Abholer relative nicht
/// auflösen.
/// </summary>
/// <summary>Woher die Seite kommt: im Betrieb aus wwwroot, im Test aus einem eigenen Ordner.</summary>
public sealed record IndexPage(IFileProvider Files);

public static partial class Preview
{
    // Die Anwendung läuft ohne ICU-Kulturen (InvariantGlobalization) — die
    // paar Namen, die es hier braucht, stehen deshalb selbst da.
    private static readonly string[] Wochentage = ["So.", "Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa."];

    private static readonly string[] Monate =
        ["Jänner", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

    public static void UsePreview(this WebApplication app)
    {
        app.Use(async (http, next) =>
        {
            var seite = http.Request.Path.Value is "/" or "/index.html";
            var index = http.RequestServices.GetRequiredService<IndexPage>().Files.GetFileInfo("index.html");

            if (!HttpMethods.IsGet(http.Request.Method) || !seite || !index.Exists)
            {
                await next(http);
                return;
            }

            using var reader = new StreamReader(index.CreateReadStream());
            var html = await reader.ReadToEndAsync(http.RequestAborted);
            var turnier = Guid.TryParse(http.Request.Query["t"], out var id)
                ? await http.RequestServices.GetRequiredService<TournamentStore>().FindAsync(id, http.RequestAborted)
                : null;

            http.Response.ContentType = "text/html; charset=utf-8";
            http.Response.Headers.CacheControl = "no-cache";
            await http.Response.WriteAsync(Render(html, turnier, Endpoints.BaseUrl(http), http.Request.GetEncodedPathAndQuery()), http.RequestAborted);
        });
    }

    /// <summary>Die Seite mit den Angaben zu diesem Turnier — oder, ohne Turnier, mit den allgemeinen.</summary>
    internal static string Render(string html, Tournament? t, string baseUrl, string pathAndQuery)
    {
        html = Setze(html, "og:image", $"{baseUrl}/icon-512.png");
        html = Setze(html, "og:url", $"{baseUrl}{pathAndQuery}");

        if (t is null)
        {
            return html;
        }

        var beschreibung = Beschreibe(t);
        html = Setze(html, "og:title", t.Name);
        html = Setze(html, "og:description", beschreibung);
        html = MetaDescription().Replace(html, $"<meta name=\"description\" content=\"{Escape(beschreibung)}\" />", 1);
        return TitleTag().Replace(html, $"<title>{Escape(t.Name)} · MATCHDAY</title>", 1);
    }

    /// <summary>„Sa., 26. September 2026 · 18:30 Uhr · Baden — Einzel, K.o., 8 Teilnehmer, läuft“</summary>
    internal static string Beschreibe(Tournament t)
    {
        var termin = new List<string>();

        if (t.Date is { } date)
        {
            termin.Add($"{Wochentage[(int)date.DayOfWeek]}, {date.Day}. {Monate[date.Month - 1]} {date.Year}");
        }

        if (t.StartTime is { } time)
        {
            termin.Add($"{time.ToString("HH:mm", CultureInfo.InvariantCulture)} Uhr");
        }

        if (t.Location is not null)
        {
            termin.Add(t.Location);
        }

        var wer = t.Discipline == Discipline.Doubles ? "Teams" : "Teilnehmer";
        var zustand = t.State switch
        {
            TournamentState.Completed => "abgeschlossen",
            _ when t.IsStarted => "läuft gerade — live mitschauen",
            _ => "noch nicht gestartet",
        };
        var rahmen = $"{(t.Discipline == Discipline.Doubles ? "Doppel" : "Einzel")}, {(t.Mode == Mode.Knockout ? "K.o." : "jeder gegen jeden")}, {t.Participants.Count} {wer}, {zustand}";

        return termin.Count == 0 ? rahmen : $"{string.Join(" · ", termin)} — {rahmen}";
    }

    /// <summary>Den Inhalt eines Open-Graph-Eintrags ersetzen — oder ihn anhängen, wenn es ihn nicht gibt.</summary>
    private static string Setze(string html, string property, string wert)
    {
        var tag = $"<meta property=\"{property}\" content=\"{Escape(wert)}\" />";
        var muster = new Regex($"<meta property=\"{Regex.Escape(property)}\"[^>]*>");

        return muster.IsMatch(html) ? muster.Replace(html, tag, 1) : html.Replace("</head>", $"    {tag}\n  </head>", StringComparison.Ordinal);
    }

    /// <summary>
    /// Nur, was im Attribut und im Titel stört. WebUtility.HtmlEncode schriebe
    /// auch Umlaute und „·“ als Zahlen — gültig, aber unleserlich in der Quelle.
    /// </summary>
    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);

    [GeneratedRegex("<title>[^<]*</title>")]
    private static partial Regex TitleTag();

    [GeneratedRegex("<meta name=\"description\"[^>]*>")]
    private static partial Regex MetaDescription();
}
