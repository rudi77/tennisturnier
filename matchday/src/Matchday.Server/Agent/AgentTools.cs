using System.Text.Json;
using Matchday.Domain;
using Matchday.Server.Api;
using Matchday.Server.Storage;

namespace Matchday.Server.Agent;

/// <summary>Was ein Werkzeugaufruf hinterlässt: Text für das Modell, ein Widget für die Oberfläche.</summary>
public sealed record ToolOutcome(string ResultForModel, bool IsError, string? Widget = null, object? WidgetData = null, Guid? TournamentId = null);

public sealed record ToolDefinition(string Name, string Description, object InputSchema);

/// <summary>
/// Die Werkzeuge des Agenten — eins zu eins die Anwendungsfälle (ADR-0016).
/// Sie rufen dieselben <see cref="TournamentActions"/> wie die HTTP-API.
/// Das Modell benennt Teilnehmer und Matches mit Namen, nie mit Ids.
/// </summary>
public sealed class AgentTools(TournamentActions actions)
{
    public const string WidgetTournament = "tournament";
    public const string WidgetParticipants = "participants";
    public const string WidgetBracket = "bracket";
    public const string WidgetStandings = "standings";
    public const string WidgetShare = "share";
    public const string WidgetList = "tournaments";

    private static readonly JsonSerializerOptions Json = TournamentStore.Json;

    /// <summary>
    /// Muss über <see cref="Definitions"/> stehen, nicht darunter. Statische
    /// Initialisierer laufen in Textreihenfolge: Stünde dieses Feld später, wäre
    /// es beim Bau der Liste noch null, und jedes Werkzeug mit tournamentId
    /// schickte „tournamentId": null. Azure weist das Schema dann zurück — mit
    /// einem 400 im Gespräch, nicht mit einem Fehler beim Start.
    /// </summary>
    private static readonly object TournamentIdProperty = new
    {
        type = "string",
        description = "Id des Turniers. Weglassen heißt: das aktuelle Turnier.",
    };

    /// <summary>Steht wie <see cref="TournamentIdProperty"/> bewusst über <see cref="Definitions"/>.</summary>
    private static readonly object DisciplineProperty = new
    {
        type = "string",
        @enum = new[] { "Singles", "Doubles", "Open" },
        description = "Singles = Einzel, Doubles = Doppel, Open = noch offen. Im Doppel ist ein Teilnehmer ein Team aus zwei Spielern. Open, solange der Benutzer es nicht gesagt hat — entschieden wird spätestens vor der Auslosung.",
    };

    public static IReadOnlyList<ToolDefinition> Definitions { get; } =
    [
        new("list_tournaments",
            "Die Turniere, die in diesem Browser angelegt wurden, mit Zustand und Teilnehmerzahl.",
            Schema(new { })),

        new("create_tournament",
            "Legt ein neues Turnier an und macht es zum aktuellen Turnier. Nur der Name ist Pflicht; frag nicht nach dem Rest, außer der Benutzer will es angeben. Standard: Einzel oder Doppel offen, K.o., zwei Gewinnsätze mit Match-Tiebreak.",
            Schema(new
            {
                name = new { type = "string", description = "Name des Turniers" },
                date = new { type = "string", description = "Datum als YYYY-MM-DD, falls genannt" },
                time = new { type = "string", description = "Uhrzeit des Starts als HH:mm, falls genannt — darauf zählt der Countdown" },
                location = new { type = "string", description = "Ort, falls genannt" },
                mode = new { type = "string", @enum = new[] { "Knockout", "RoundRobin" }, description = "Knockout = K.o., RoundRobin = jeder gegen jeden" },
                discipline = DisciplineProperty,
                bestOf = new { type = "integer", @enum = new[] { 1, 3, 5 }, description = "Sätze insgesamt: 1, 3 oder 5" },
                finalSet = new { type = "string", @enum = new[] { "Regular", "MatchTiebreak10", "Advantage" }, description = "Letzter Satz: normal, Match-Tiebreak bis 10, oder ohne Tiebreak" },
                tiebreakAt = new { type = "integer", description = "Ab wie vielen Spielen der Satz endet, üblich 6; kurze Sätze 4" },
                participants = new { type = "array", items = new { type = "string" }, description = "Teilnehmer, falls gleich genannt. Im Doppel je Team ein Eintrag mit beiden Spielern: „Anna / Tom“." },
            }, "name")),

        new("get_tournament",
            "Zeigt ein Turnier: Rahmen, Teilnehmer, Bracket oder Tabelle, je nach Zustand. Macht es zum aktuellen Turnier.",
            Schema(new { tournamentId = TournamentIdProperty }, "tournamentId")),

        new("update_tournament",
            "Ändert Name, Datum, Uhrzeit, Ort, Modus, Disziplin oder Satzformat des aktuellen Turniers. Modus, Disziplin und Format nur, solange das Turnier nicht gestartet ist; ist schon ausgelost, lost ein neuer Modus neu aus. Wechselt ein Einzel mit Namen ins Doppel, stehen die Spieler danach ohne Partner da; zurück ins Einzel zerfallen die Teams in ihre Spieler.",
            Schema(new
            {
                tournamentId = TournamentIdProperty,
                name = new { type = "string" },
                date = new { type = "string", description = "YYYY-MM-DD, oder leerer String zum Entfernen" },
                time = new { type = "string", description = "Uhrzeit des Starts als HH:mm, oder leerer String zum Entfernen" },
                location = new { type = "string", description = "Ort, oder leerer String zum Entfernen" },
                mode = new { type = "string", @enum = new[] { "Knockout", "RoundRobin" } },
                discipline = DisciplineProperty,
                bestOf = new { type = "integer", @enum = new[] { 1, 3, 5 } },
                finalSet = new { type = "string", @enum = new[] { "Regular", "MatchTiebreak10", "Advantage" } },
                tiebreakAt = new { type = "integer" },
            })),

        new("add_participants",
            "Trägt Teilnehmer in das aktuelle Turnier ein. Geht, solange das Turnier nicht gestartet ist; war schon ausgelost, wird neu ausgelost. Im Doppel ist ein Teilnehmer ein Team: je Eintrag beide Spieler, getrennt durch „/“. Stehen die Teams noch nicht fest, trag die Spieler einzeln ein — sie stehen dann ohne Partner auf der Liste. Ein Paar aus zwei Spielern, die schon ohne Partner dastehen, macht die beiden zum Team.",
            Schema(new
            {
                tournamentId = TournamentIdProperty,
                names = new { type = "array", items = new { type = "string" }, description = "Namen der Teilnehmer; im Doppel je Team „Anna / Tom“ oder ein Spieler allein, wenn sein Partner noch offen ist" },
            }, "names")),

        new("add_random_teams",
            "Würfelt Doppel-Teams — das Los für die Paarungen. Gepaart werden alle, die ohne Partner auf der Liste stehen, samt den Spielern, die hier noch genannt werden. Nur im Doppel und nur, solange das Turnier nicht gestartet ist. Nimm dieses Werkzeug, wenn der Benutzer zufällige Teams will, statt selbst Paare zu bilden; gemischt wird in der Anwendung. Zusammen muss die Zahl gerade sein.",
            Schema(new
            {
                tournamentId = TournamentIdProperty,
                players = new { type = "array", items = new { type = "string" }, description = "Weitere Spieler einzeln, je Eintrag ein Name — keine Paare. Weglassen, wenn nur die schon Eingetragenen gepaart werden sollen." },
            })),

        new("remove_participants",
            "Streicht Teilnehmer aus dem aktuellen Turnier. Geht, solange das Turnier nicht begonnen hat; war schon ausgelost, wird neu ausgelost. Im Doppel genügt ein Spieler des Teams.",
            Schema(new
            {
                tournamentId = TournamentIdProperty,
                names = new { type = "array", items = new { type = "string" } },
            }, "names")),

        new("draw",
            "Lost das aktuelle Turnier aus und legt alle Matches an. Danach ist die Teilnehmerliste eingefroren. Nur nach ausdrücklicher Zustimmung des Benutzers.",
            Schema(new { tournamentId = TournamentIdProperty })),

        new("start_tournament",
            "Startet das ausgeloste Turnier: Der Countdown ist vorbei, ab jetzt wird gezählt und eingetragen, und Teilnehmer, Modus und Format stehen fest. Vorher lassen sich keine Punkte und keine Ergebnisse eintragen. Nur, wenn der Benutzer es ausdrücklich will.",
            Schema(new { tournamentId = TournamentIdProperty })),

        new("undo_draw",
            "Nimmt die Auslosung zurück; alle Matches und Ergebnisse gehen verloren. Nur nach ausdrücklicher Zustimmung des Benutzers.",
            Schema(new { tournamentId = TournamentIdProperty })),

        new("record_result",
            "Trägt ein Ergebnis ein. Das Match wird über die beiden Namen gefunden; im Doppel genügt je Team ein Spieler oder das Paar als „Anna / Tom“. Sätze aus Sicht des Siegers: bei 6:4, 3:6, 10:8 für den Sieger also [[6,4],[3,6],[10,8]]. Bei Nichtantreten (walkover) oder Aufgabe (retired) ist winner der, der weiterkommt.",
            Schema(new
            {
                tournamentId = TournamentIdProperty,
                winner = new { type = "string", description = "Name des Siegers" },
                loser = new { type = "string", description = "Name des Verlierers" },
                kind = new { type = "string", @enum = new[] { "played", "walkover", "retired" }, description = "Standard: played" },
                sets = new
                {
                    type = "array",
                    description = "Je Satz [Spiele des Siegers, Spiele des Verlierers] oder [Spiele, Spiele, Tiebreak-Punkte des Unterlegenen]",
                    items = new { type = "array", items = new { type = "integer" }, minItems = 2, maxItems = 3 },
                },
                abandonedSet = new { type = "array", items = new { type = "integer" }, description = "Bei Aufgabe: der laufende Satz [Sieger, Verlierer], falls einer lief" },
            }, "winner", "loser")),

        new("clear_result",
            "Nimmt ein Ergebnis zurück. Das Match wird über die beiden Namen gefunden.",
            Schema(new
            {
                tournamentId = TournamentIdProperty,
                nameA = new { type = "string" },
                nameB = new { type = "string" },
            }, "nameA", "nameB")),

        new("share_links",
            "Die Links zum aktuellen Turnier: einer zum Mitschauen für alle, einer zum Eintragen von Spielständen (für Mitspieler und Helfer) und einer zum Verwalten (geheim).",
            Schema(new { tournamentId = TournamentIdProperty })),

        new("renew_viewer_link",
            "Erneuert den Mitschau-Link: Der alte gilt ab sofort nicht mehr, und wer damit gerade zuschaut, wird abgemeldet. Für den Fall, dass der Link in falsche Hände geraten ist. Eintragen-Link und Verwalterlink bleiben, wie sie sind.",
            Schema(new { tournamentId = TournamentIdProperty })),

        new("delete_tournament",
            "Löscht ein Turnier endgültig. Nur nach ausdrücklicher Zustimmung des Benutzers.",
            Schema(new { tournamentId = TournamentIdProperty }, "tournamentId")),
    ];

    private static object Schema(object properties, params string[] required) => new
    {
        type = "object",
        properties,
        required,
        additionalProperties = false,
    };

    public async Task<ToolOutcome> ExecuteAsync(string name, JsonElement input, Actor actor, Guid? currentTournamentId, string baseUrl, CancellationToken ct)
    {
        try
        {
            return name switch
            {
                "list_tournaments" => await ListAsync(actor, ct),
                "create_tournament" => await CreateAsync(input, actor, baseUrl, ct),
                "get_tournament" => Show(await actions.ReadAsync(actor, Id(input, currentTournamentId), ct)),
                "update_tournament" => await UpdateAsync(input, actor, Id(input, currentTournamentId), ct),
                "add_participants" => Show(await actions.AddParticipantsAsync(actor, Id(input, currentTournamentId), Strings(input, "names"), ct), WidgetParticipants),
                "add_random_teams" => Show(await actions.AddRandomTeamsAsync(actor, Id(input, currentTournamentId), Strings(input, "players"), ct), WidgetParticipants),
                "remove_participants" => await RemoveAsync(input, actor, Id(input, currentTournamentId), ct),
                "draw" => Show(await actions.DrawAsync(actor, Id(input, currentTournamentId), ct)),
                "start_tournament" => Show(await actions.StartAsync(actor, Id(input, currentTournamentId), ct)),
                "undo_draw" => Show(await actions.UndoDrawAsync(actor, Id(input, currentTournamentId), ct), WidgetParticipants),
                "record_result" => await RecordAsync(input, actor, Id(input, currentTournamentId), ct),
                "clear_result" => await ClearAsync(input, actor, Id(input, currentTournamentId), ct),
                "share_links" => await ShareAsync(actor, Id(input, currentTournamentId), baseUrl, ct),
                "renew_viewer_link" => await RenewViewerAsync(actor, Id(input, currentTournamentId), baseUrl, ct),
                "delete_tournament" => await DeleteAsync(actor, Id(input, currentTournamentId), ct),
                _ => new ToolOutcome($"Unbekanntes Werkzeug: {name}", IsError: true),
            };
        }
        catch (DomainException e)
        {
            return new ToolOutcome(e.Message, IsError: true);
        }
        catch (NotFoundException e)
        {
            return new ToolOutcome(e.Message, IsError: true);
        }
        catch (ForbiddenException e)
        {
            return new ToolOutcome(e.Message, IsError: true);
        }
    }

    // --- Die Werkzeuge ---------------------------------------------------

    private async Task<ToolOutcome> ListAsync(Actor actor, CancellationToken ct)
    {
        var mine = await actions.ListMineAsync(actor, ct);
        var summaries = mine.Select(t => new { t.Id, t.Name, t.Date, t.Location, t.Mode, t.State, participants = t.Participants.Count }).ToList();

        return new ToolOutcome(
            summaries.Count == 0 ? "Noch keine Turniere in diesem Browser." : JsonSerializer.Serialize(summaries, Json),
            IsError: false,
            WidgetList,
            mine.Select(ViewBuilder.Summarize).ToList());
    }

    private async Task<ToolOutcome> CreateAsync(JsonElement input, Actor actor, string baseUrl, CancellationToken ct)
    {
        var format = FormatFrom(input, MatchFormat.Standard);
        var request = new CreateTournamentRequest(
            String(input, "name") ?? "",
            Date(input, "date"),
            String(input, "location"),
            Enum<Mode>(input, "mode") ?? Mode.Knockout,
            format,
            Enum<Discipline>(input, "discipline") ?? Discipline.Open,
            Strings(input, "participants"),
            Time(input, "time"));

        // Teilnehmer gehören in dieselbe Anfrage: Scheitert ein Name — im
        // Doppel etwa ein Team mit nur einem Spieler —, steht sonst ein halb
        // gefülltes Turnier da, das niemand bestellt hat.
        var t = await actions.CreateAsync(actor, request, ct);

        var links = ViewBuilder.Links(t, baseUrl);
        return Show(t, WidgetTournament, extra: $"Mitschau-Link: {links.PublicUrl}");
    }

    private async Task<ToolOutcome> UpdateAsync(JsonElement input, Actor actor, Guid id, CancellationToken ct)
    {
        var current = await actions.GetAsync(id, ct);
        var date = String(input, "date");
        var location = String(input, "location");
        var time = String(input, "time");
        var hasFormat = input.TryGetProperty("bestOf", out _) || input.TryGetProperty("finalSet", out _) || input.TryGetProperty("tiebreakAt", out _);

        var request = new UpdateTournamentRequest(
            Name: String(input, "name"),
            Date: date is { Length: > 0 } ? Date(input, "date") : null,
            ClearDate: date is { Length: 0 },
            Location: location is { Length: > 0 } ? location : null,
            ClearLocation: location is { Length: 0 },
            Mode: Enum<Mode>(input, "mode"),
            Discipline: Enum<Discipline>(input, "discipline"),
            Format: hasFormat ? FormatFrom(input, current.Format) : null,
            StartTime: time is { Length: > 0 } ? Time(input, "time") : null,
            ClearStartTime: time is { Length: 0 });

        return Show(await actions.UpdateAsync(actor, id, request, ct), WidgetTournament);
    }

    private async Task<ToolOutcome> RemoveAsync(JsonElement input, Actor actor, Guid id, CancellationToken ct)
    {
        var t = await actions.GetAsync(id, ct);
        var missing = new List<string>();

        foreach (var name in Strings(input, "names"))
        {
            var participant = t.FindParticipant(name);

            if (participant is null)
            {
                missing.Add(name);
                continue;
            }

            t = await actions.RemoveParticipantAsync(actor, id, participant.Id, ct);
        }

        var note = missing.Count == 0 ? null : $"Nicht gefunden: {string.Join(", ", missing)}.";
        return Show(t, WidgetParticipants, extra: note);
    }

    private async Task<ToolOutcome> RecordAsync(JsonElement input, Actor actor, Guid id, CancellationToken ct)
    {
        var t = await actions.GetAsync(id, ct);
        var (match, winnerSide) = FindMatch(t, String(input, "winner") ?? "", String(input, "loser") ?? "");

        var kind = (String(input, "kind") ?? "played").ToLowerInvariant() switch
        {
            "walkover" => ResultKind.Walkover,
            "retired" => ResultKind.Retired,
            _ => ResultKind.Played,
        };

        var sets = input.TryGetProperty("sets", out var setsElement) && setsElement.ValueKind == JsonValueKind.Array
            ? setsElement.EnumerateArray().Select(SetFrom).ToList()
            : [];
        var abandoned = input.TryGetProperty("abandonedSet", out var abandonedElement) && abandonedElement.ValueKind == JsonValueKind.Array
            ? SetFrom(abandonedElement)
            : null;

        var request = new ResultRequest(kind, winnerSide, sets, abandoned);
        return Show(await actions.RecordResultAsync(actor, id, match.Id, request, ct));
    }

    private async Task<ToolOutcome> ClearAsync(JsonElement input, Actor actor, Guid id, CancellationToken ct)
    {
        var t = await actions.GetAsync(id, ct);
        var (match, _) = FindMatch(t, String(input, "nameA") ?? "", String(input, "nameB") ?? "");
        return Show(await actions.ClearResultAsync(actor, id, match.Id, ct));
    }

    private async Task<ToolOutcome> ShareAsync(Actor actor, Guid id, string baseUrl, CancellationToken ct)
    {
        var t = await actions.GetAsync(id, ct);

        if (!actor.MayManage(t))
        {
            throw new ForbiddenException("Dafür braucht es den Verwalterlink dieses Turniers.");
        }

        return Share(t, baseUrl, "");
    }

    private async Task<ToolOutcome> RenewViewerAsync(Actor actor, Guid id, string baseUrl, CancellationToken ct) =>
        Share(await actions.RotateViewerTokenAsync(actor, id, ct), baseUrl, "Der alte Mitschau-Link gilt nicht mehr. ");

    /// <summary>Die drei Links, als Text für das Modell und als Widget für die Bühne.</summary>
    private static ToolOutcome Share(Tournament t, string baseUrl, string vorweg)
    {
        var links = ViewBuilder.Links(t, baseUrl);
        return new ToolOutcome(
            $"{vorweg}Mitschau-Link (für alle): {links.PublicUrl}\nEintragen-Link (für alle, die Spielstände eintragen sollen): {links.ScorerUrl}\nVerwalterlink (geheim, nur für die Turnierleitung): {links.AdminUrl}",
            IsError: false,
            WidgetShare,
            new { tournament = ViewBuilder.Build(t), links },
            t.Id);
    }

    private async Task<ToolOutcome> DeleteAsync(Actor actor, Guid id, CancellationToken ct)
    {
        var t = await actions.GetAsync(id, ct);
        await actions.DeleteAsync(actor, id, ct);
        return new ToolOutcome($"„{t.Name}“ ist gelöscht.", IsError: false, WidgetList, null, TournamentId: null);
    }

    // --- Hilfen ----------------------------------------------------------

    /// <summary>Die Sicht als Werkzeugergebnis, Widget passend zum Zustand.</summary>
    private static ToolOutcome Show(Tournament t, string? widget = null, string? extra = null)
    {
        widget ??= t.State == TournamentState.Setup
            ? WidgetTournament
            : t.Mode == Mode.Knockout ? WidgetBracket : WidgetStandings;

        var text = Summarize(t);
        return new ToolOutcome(extra is null ? text : $"{text}\n{extra}", IsError: false, widget, ViewBuilder.Build(t), t.Id);
    }

    /// <summary>Kompakt für das Modell: was es zum Erzählen braucht, nicht die ganze Sicht.</summary>
    internal static string Summarize(Tournament t)
    {
        var lines = new List<string>
        {
            $"Turnier „{t.Name}“ (id {t.Id}), {DisciplineText(t.Discipline)}, {ModeText(t.Mode)}, {t.Format.Describe()}, Zustand: {StateText(t)}",
        };

        if (t.Date is { } date)
        {
            lines.Add($"Datum: {date:yyyy-MM-dd}");
        }

        if (t.StartTime is { } time)
        {
            lines.Add($"Start: {time:HH\\:mm} Uhr");
        }

        if (t.Location is not null)
        {
            lines.Add($"Ort: {t.Location}");
        }

        var what = t.Discipline.Entries();
        var teams = t.Participants.Except(t.Unpaired).ToList();
        lines.Add($"{what} ({teams.Count}): {string.Join("; ", teams.Select(p => p.Name))}");

        if (t.Unpaired.Count > 0)
        {
            lines.Add($"Ohne Partner: {string.Join(", ", t.Unpaired.Select(p => p.Name))} — vor der Auslosung braucht jeder ein Team.");
        }

        if (t.Matches.Count > 0)
        {
            lines.Add("Matches:");
            lines.AddRange(t.Matches.Select(m =>
                $"- {m.Label}: {t.NameOf(m.Side1)} – {t.NameOf(m.Side2)}" +
                (m.Score is not null ? $" → {t.NameOf(m.SideOf(m.Score.WinnerSide))} {ScoreFromWinner(m.Score)}"
                    : m.Status == MatchStatus.Playing ? $" (läuft: {LiveText(t.LiveStateOf(m)!)})"
                    : m.Status == MatchStatus.Ready ? " (offen)"
                    : " (Gegner offen)")));
        }

        if (t.State != TournamentState.Setup)
        {
            lines.Add(t.Mode == Mode.RoundRobin ? "Tabelle:" : "Platzierung:");
            lines.AddRange(t.Standings().Select(s =>
                $"{s.Rank}. {s.Name} — {s.Won} Siege, {s.Lost} Niederlagen, Sätze {s.SetsWon}:{s.SetsLost}, Spiele {s.GamesWon}:{s.GamesLost}"));
        }

        return string.Join("\n", lines);
    }

    /// <summary>Das Ergebnis aus Sicht des Siegers — so, wie das Modell es auch entgegennimmt.</summary>
    private static string ScoreFromWinner(Score score)
    {
        if (score.WinnerSide == 1 || score.Sets.Count == 0)
        {
            return score.ToString();
        }

        var text = string.Join(", ", score.Sets.Select(s => new SetScore(s.Games2, s.Games1, s.TiebreakPoints)));
        return score.Outcome == MatchOutcome.Retirement ? $"{text} (Aufgabe)" : text;
    }

    /// <summary>Der laufende Stand aus Sicht von Seite 1: Sätze, Spiele im Satz, Punkte im Spiel.</summary>
    internal static string LiveText(LiveState s)
    {
        var parts = s.CompletedSets.Select(set => set.ToString()).ToList();
        parts.Add(s.InMatchTiebreak ? $"Match-Tiebreak {s.Points1}:{s.Points2}" : $"{s.Games1}:{s.Games2}");

        if (!s.InMatchTiebreak)
        {
            parts.Add(s.InTiebreak ? $"Tiebreak {s.Points1}:{s.Points2}" : $"{s.PointsText(1)}:{s.PointsText(2)}");
        }

        return string.Join(", ", parts);
    }

    internal static string ModeText(Mode mode) => mode == Mode.Knockout ? "K.o." : "jeder gegen jeden";

    internal static string DisciplineText(Discipline discipline) => discipline.Describe();

    internal static string StateText(Tournament t) => t.State switch
    {
        TournamentState.Setup => "Vorbereitung (noch nicht ausgelost)",
        TournamentState.Running when !t.IsStarted => "ausgelost, wartet auf den Start",
        TournamentState.Running => "läuft",
        _ => "abgeschlossen",
    };

    /// <summary>Das Match zu zwei Namen — und auf welcher Seite der erste steht.</summary>
    internal static (Match Match, int SideOfFirst) FindMatch(Tournament t, string first, string second)
    {
        var a = t.FindParticipant(first) ?? throw new DomainException($"„{first}“ steht nicht in der Teilnehmerliste.");
        var b = t.FindParticipant(second) ?? throw new DomainException($"„{second}“ steht nicht in der Teilnehmerliste.");

        if (a.Id == b.Id)
        {
            throw new DomainException("Sieger und Verlierer sind dieselbe Person.");
        }

        foreach (var match in t.Matches)
        {
            if (match.Side1.ParticipantId == a.Id && match.Side2.ParticipantId == b.Id)
            {
                return (match, 1);
            }

            if (match.Side1.ParticipantId == b.Id && match.Side2.ParticipantId == a.Id)
            {
                return (match, 2);
            }
        }

        throw new DomainException($"{a.Name} und {b.Name} spielen (noch) nicht gegeneinander.");
    }

    private static Guid Id(JsonElement input, Guid? current)
    {
        var text = String(input, "tournamentId");

        if (text is { Length: > 0 })
        {
            return Guid.TryParse(text, out var id) ? id : throw new DomainException($"„{text}“ ist keine Turnier-Id.");
        }

        return current ?? throw new DomainException("Kein aktuelles Turnier. Erst eines anlegen oder mit get_tournament wählen.");
    }

    private static string? String(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> Strings(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : [];

    private static int? Int(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static DateOnly? Date(JsonElement input, string name)
    {
        var text = String(input, name);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var date)
            ? date
            : throw new DomainException($"„{text}“ ist kein Datum im Format YYYY-MM-DD.");
    }

    private static TimeOnly? Time(JsonElement input, string name)
    {
        var text = String(input, name);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return TimeOnly.TryParseExact(text.Trim(), ["HH:mm", "H:mm"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time)
            ? time
            : throw new DomainException($"„{text}“ ist keine Uhrzeit im Format HH:mm.");
    }

    private static T? Enum<T>(JsonElement input, string name)
        where T : struct, Enum
    {
        var text = String(input, name);
        return text is null ? null : System.Enum.TryParse<T>(text, ignoreCase: true, out var value) ? value : throw new DomainException($"„{text}“ ist kein gültiger Wert für {name}.");
    }

    private static MatchFormat FormatFrom(JsonElement input, MatchFormat fallback) => new(
        Int(input, "bestOf") ?? fallback.BestOf,
        Enum<FinalSetMode>(input, "finalSet") ?? fallback.FinalSetMode,
        Int(input, "tiebreakAt") ?? fallback.TiebreakAt);

    private static SetScore SetFrom(JsonElement element)
    {
        var numbers = element.EnumerateArray().Select(e => e.GetInt32()).ToList();

        return numbers.Count switch
        {
            2 => new SetScore(numbers[0], numbers[1]),
            3 => new SetScore(numbers[0], numbers[1], numbers[2]),
            _ => throw new DomainException("Ein Satz besteht aus zwei Zahlen, mit Tiebreak aus drei."),
        };
    }
}
