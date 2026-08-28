using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Rollen an einem Turnier vergeben und entziehen.
///
/// Der Punkt, an dem eine frische Instanz lange stehenblieb: Rollen vergibt, wer
/// eine Rolle hat, und einen Endpunkt dafür gab es nicht. Zwei Sperren tragen
/// den Anwendungsfall, und beide sind hier festgehalten — die Eskalationssperre
/// und das herrenlose Turnier.
/// </summary>
public sealed class RollenApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public RollenApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>Ein Turnier samt dem Client seines Anlegers — er ist Turnierleiter.</summary>
    private async Task<(HttpClient Leitung, Guid TournamentId)> TurnierAsync()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"rollen-leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Anlage = "TC Rollenvergabe", Auslosen = false });

        return (aufbau.Admin, aufbau.TournamentId);
    }

    /// <summary>
    /// Ein Konto, das es schon gibt. Berufen lässt sich nur, wer sich einmal
    /// angemeldet hat — die Einladung eines Unbekannten ist ein offener Punkt.
    /// </summary>
    private async Task<(HttpClient Client, string Email)> AngemeldeterBenutzerAsync(string rolle)
    {
        var email = $"{rolle}.{Guid.NewGuid():N}"[..24] + "@example.invalid";
        var client = _factory.CreateClientAs($"{rolle}-{Guid.NewGuid():N}", email);

        // Der erste Aufruf legt das Konto an — vorher kennt es niemand.
        await client.GetAsync("/api/me");

        return (client, email);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_laesst_sich_berufen_und_traegt_dann_Ergebnisse_ein()
    {
        var (leitung, tournamentId) = await TurnierAsync();
        var (referee, email) = await AngemeldeterBenutzerAsync("referee");

        // Vorher sieht er das Turnier nicht.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await referee.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        var response = await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Referee),
            Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Und danach schon — die Rolle wirkt beim nächsten Request.
        Assert.Equal(
            HttpStatusCode.OK,
            (await referee.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        // Führen darf er es trotzdem nicht.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await referee.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null)).StatusCode);
    }

    [Fact]
    public async Task Die_Rollenliste_nennt_den_Anleger_als_Turnierleiter()
    {
        var (leitung, tournamentId) = await TurnierAsync();

        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        var eintrag = Assert.Single(rollen!);
        Assert.Equal(Role.TournamentDirector, eintrag.Role);
    }

    [Fact]
    public async Task Dieselbe_Rolle_zweimal_zu_vergeben_aendert_nichts()
    {
        // Der zweite Klick auf dieselbe Schaltfläche ist keine Änderung.
        var (leitung, tournamentId) = await TurnierAsync();
        var (_, email) = await AngemeldeterBenutzerAsync("referee");

        var request = new GrantRoleRequest(email, Role.Referee);

        await leitung.PostAsJsonAsync($"/api/tournaments/{tournamentId}/roles", request, Json);
        await leitung.PostAsJsonAsync($"/api/tournaments/{tournamentId}/roles", request, Json);

        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.Single(rollen!, r => r.Role == Role.Referee);
    }

    [Fact]
    public async Task Ein_Mitglied_sieht_sein_Turnier_und_aendert_nichts_daran()
    {
        // Die Rolle, die ein Turnier zur Gruppe macht. Sie ist der ganze
        // Unterschied zwischen „kennt die Adresse" und „gehoert dazu" — und
        // sie gewaehrt trotzdem kein einziges Recht.
        var (leitung, tournamentId) = await TurnierAsync();
        var (mitglied, email) = await AngemeldeterBenutzerAsync("mitglied");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await mitglied.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        var response = await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Member),
            Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Es sieht das Turnier — und findet es unter seinen eigenen.
        Assert.Equal(
            HttpStatusCode.OK,
            (await mitglied.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        var meine = await mitglied.GetFromJsonAsync<List<TournamentSummary>>(
            "/api/tournaments", Json);
        Assert.Contains(meine!, t => t.Id == tournamentId);

        // Aendern darf es nichts: die Meldung zu oeffnen ist Sache der Leitung.
        var versuch = await mitglied.PostAsync(
            $"/api/tournaments/{tournamentId}/registration/open", null);
        Assert.Equal(HttpStatusCode.NotFound, versuch.StatusCode);
    }

    [Theory]
    [InlineData(Role.SystemAdmin)]
    [InlineData(Role.Organizer)]
    public async Task Ein_Turnierleiter_darf_keine_globale_Rolle_vergeben(Role global)
    {
        // Die Eskalationssperre. Ohne sie machte sich ein Turnierleiter über ein
        // zweites Konto, das ihm gehört, zum Systemadministrator — und das ist
        // kein theoretischer Weg, sondern ein einziger Aufruf.
        var (leitung, tournamentId) = await TurnierAsync();
        var (_, email) = await AngemeldeterBenutzerAsync("kandidat");

        var response = await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, global),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.DoesNotContain(rollen!, r => r.Role == global);
    }

    [Fact]
    public async Task Die_letzte_Turnierleitung_laesst_sich_nicht_entziehen()
    {
        // Sonst entstünde ein herrenloses Turnier: der Query-Filter kennt keinen
        // zweiten Weg dorthin, und ohne Sicht darauf ließe sich auch keine
        // Rolle mehr daran vergeben. Eine Einbahnstraße, kein Ärgernis.
        var (leitung, tournamentId) = await TurnierAsync();

        var eigene = (await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json))!.Single();

        var response = await leitung.DeleteAsync(
            $"/api/tournaments/{tournamentId}/roles/{eigene.AssignmentId}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("letzte", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(
            HttpStatusCode.OK,
            (await leitung.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Mit_einer_zweiten_Turnierleitung_laesst_sich_die_erste_entziehen()
    {
        // Die Gegenprobe: ohne sie wäre die Regel oben auch dann erfüllt, wenn
        // sich überhaupt keine Turnierleitung entziehen ließe — und der
        // Übergabefall ist der Normalfall.
        var (leitung, tournamentId) = await TurnierAsync();
        var (nachfolger, email) = await AngemeldeterBenutzerAsync("nachfolge");

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.TournamentDirector),
            Json);

        var eigene = (await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json))!
            .First(r => r.Email != email && r.Role == Role.TournamentDirector);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await leitung.DeleteAsync(
                $"/api/tournaments/{tournamentId}/roles/{eigene.AssignmentId}")).StatusCode);

        // Der Nachfolger führt es weiter …
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await nachfolger.PostAsync(
                $"/api/tournaments/{tournamentId}/registration/open", null)).StatusCode);

        // … und der Vorgänger sieht es nicht mehr.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await leitung.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_darf_keine_Rollen_vergeben()
    {
        var (leitung, tournamentId) = await TurnierAsync();
        var (referee, refereeEmail) = await AngemeldeterBenutzerAsync("referee");
        var (_, kandidat) = await AngemeldeterBenutzerAsync("kandidat");

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(refereeEmail, Role.Referee),
            Json);

        // Er sieht das Turnier — und darf trotzdem nicht darüber verfügen.
        Assert.Equal(
            HttpStatusCode.OK,
            (await referee.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await referee.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/roles",
                new GrantRoleRequest(kandidat, Role.Referee),
                Json)).StatusCode);
    }

    [Fact]
    public async Task Ein_Aussenstehender_sieht_die_Rollenliste_nicht()
    {
        // Als 404 und nicht als 403: ein 403 bestätigte, dass es dieses Turnier
        // gibt (ADR-0004).
        var (_, tournamentId) = await TurnierAsync();
        var fremder = _factory.CreateClientAs($"fremder-{Guid.NewGuid():N}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fremder.GetAsync($"/api/tournaments/{tournamentId}/roles")).StatusCode);
    }

    [Fact]
    public async Task Wer_noch_nie_angemeldet_war_wird_eingeladen()
    {
        // Hier endete die Rollenvergabe bis zuletzt an einer Fehlermeldung:
        // „berufen lässt sich nur, wer sich schon einmal angemeldet hat". Sie
        // war richtig und trotzdem eine Sackgasse — wer jemanden einladen
        // wollte, musste ihn zuerst dazu bringen, sich anzumelden, ohne ihm
        // sagen zu können, wofür (ADR-0007, jetzt ADR-0012).
        var (leitung, tournamentId) = await TurnierAsync();
        var email = $"kommt.noch.{Guid.NewGuid():N}"[..24] + "@example.invalid";

        var response = await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Referee),
            Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(
            (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("invited").GetBoolean());

        // Sie steht in derselben Liste wie die Berufenen — als Zeile, auf die
        // man wartet.
        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        var offen = Assert.Single(rollen!, r => r.Pending);
        Assert.Equal(email.ToLowerInvariant(), offen.Email);
        Assert.Equal(Role.Referee, offen.Role);
        Assert.Equal(Guid.Empty, offen.UserId);
        Assert.Null(offen.DisplayName);
    }

    [Fact]
    public async Task Zweimal_einladen_legt_nur_eine_Einladung_an()
    {
        var (leitung, tournamentId) = await TurnierAsync();
        var email = $"doppelt.{Guid.NewGuid():N}"[..24] + "@example.invalid";
        var wunsch = new GrantRoleRequest(email, Role.Member);

        await leitung.PostAsJsonAsync($"/api/tournaments/{tournamentId}/roles", wunsch, Json);
        await leitung.PostAsJsonAsync($"/api/tournaments/{tournamentId}/roles", wunsch, Json);

        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.Single(rollen!, r => r.Pending);
    }

    [Fact]
    public async Task Dieselbe_Adresse_laesst_sich_zu_zwei_Rollen_einladen()
    {
        // Zwei Einladungen, nicht eine ueberschriebene: wer als Mitglied und
        // als Schiedsrichter vorgesehen ist, ist beides.
        var (leitung, tournamentId) = await TurnierAsync();
        var email = $"zweifach.{Guid.NewGuid():N}"[..24] + "@example.invalid";
        var andere = $"jemand.{Guid.NewGuid():N}"[..24] + "@example.invalid";

        // Eine fremde Adresse steht schon da — sie darf die zweite Einladung
        // weder verhindern noch beantworten.
        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(andere, Role.Member),
            Json);

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Member),
            Json);

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Referee),
            Json);

        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.Equal(3, rollen!.Count(r => r.Pending));
        Assert.Equal(2, rollen!.Count(r => r.Pending && r.Email == email.ToLowerInvariant()));
    }

    [Fact]
    public async Task Eine_Einladung_laesst_sich_wieder_zuruecknehmen()
    {
        // Sie verfällt nicht — zurücknehmen ist der Weg, der gebraucht wird.
        var (leitung, tournamentId) = await TurnierAsync();
        var email = $"doch.nicht.{Guid.NewGuid():N}"[..24] + "@example.invalid";

        var angelegt = await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Referee),
            Json);

        var id = (await angelegt.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await leitung.DeleteAsync($"/api/tournaments/{tournamentId}/roles/{id}")).StatusCode);

        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.DoesNotContain(rollen!, r => r.Pending);
    }

    [Fact]
    public async Task Eine_Einladung_wird_bei_der_ersten_Anmeldung_eingeloest()
    {
        // Der Weg, den ADR-0007 skizziert hat: eine Vorabzuweisung, eingelöst
        // beim ersten Login. Vorher gab es sie nicht.
        var (leitung, tournamentId) = await TurnierAsync();
        var email = $"neu.{Guid.NewGuid():N}"[..24] + "@example.invalid";

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Referee),
            Json);

        // Jetzt kommt er zum ersten Mal — dasselbe Konto gab es vorher nicht.
        var eingeladener = _factory.CreateClientAs($"eingeladen-{Guid.NewGuid():N}", email);

        var me = await eingeladener.GetFromJsonAsync<MeResponse>("/api/me", Json);
        Assert.Contains(me!.Roles, r => r.Role == Role.Referee && r.ResourceId == tournamentId);

        Assert.Equal(
            HttpStatusCode.OK,
            (await eingeladener.GetAsync($"/api/tournaments/{tournamentId}")).StatusCode);

        // Und die Einladung ist verbraucht: in der Liste steht jetzt ein
        // Mensch mit Konto und keine offene Zeile mehr.
        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.DoesNotContain(rollen!, r => r.Pending);
        Assert.Contains(rollen!, r => r.Role == Role.Referee && r.Email == email.ToLowerInvariant());
    }

    [Fact]
    public async Task Die_Schreibweise_der_Adresse_entscheidet_nichts()
    {
        // „Anna@Verein.at" und „anna@verein.at" sind derselbe Mensch. Stünde
        // beides nebeneinander, bekäme er seine Rolle je nach Schreibweise
        // seines Ausstellers — oder eben nicht.
        var (leitung, tournamentId) = await TurnierAsync();
        var email = $"Gross.{Guid.NewGuid():N}"[..24] + "@Example.Invalid";

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Member),
            Json);

        var client = _factory.CreateClientAs(
            $"gross-{Guid.NewGuid():N}", email.ToLowerInvariant());

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);
        Assert.Contains(me!.Roles, r => r.Role == Role.Member && r.ResourceId == tournamentId);
    }

    [Fact]
    public async Task Eine_Einladung_an_jemanden_der_schon_dabei_ist_verschwindet_trotzdem()
    {
        // Er ist inzwischen selbst beigetreten oder wurde von Hand berufen. Die
        // Einladung ist damit erledigt — bliebe sie stehen, stünde sie für
        // immer in der Liste.
        var (leitung, tournamentId) = await TurnierAsync();
        var email = $"schon.da.{Guid.NewGuid():N}"[..24] + "@example.invalid";
        var subject = $"schon-da-{Guid.NewGuid():N}";

        // Erst einladen, solange es das Konto noch nicht gibt.
        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Referee),
            Json);

        // Dann meldet er sich an — die Einladung wird eingelöst.
        var client = _factory.CreateClientAs(subject, email);
        await client.GetAsync("/api/me");

        // Und noch einmal: jetzt gibt es keine Einladung mehr einzulösen.
        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);

        Assert.Single(me!.Roles, r => r.Role == Role.Referee && r.ResourceId == tournamentId);
    }

    [Fact]
    public async Task Ohne_Adresse_am_Konto_bleibt_die_Einladung_stehen()
    {
        // Nicht jeder Aussteller liefert eine E-Mail. Ohne sie fehlt der
        // Schlüssel — die Einladung geht dann an niemanden, statt an den
        // Falschen.
        var (leitung, tournamentId) = await TurnierAsync();
        var email = $"wartet.{Guid.NewGuid():N}"[..24] + "@example.invalid";

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Member),
            Json);

        var ohneAdresse = _factory.CreateClientAs($"ohne-mail-{Guid.NewGuid():N}");
        var me = await ohneAdresse.GetFromJsonAsync<MeResponse>("/api/me", Json);

        Assert.DoesNotContain(me!.Roles, r => r.ResourceId == tournamentId);

        var rollen = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.Single(rollen!, r => r.Pending);
    }

    [Fact]
    public async Task Ein_Mitglied_sieht_wer_dazugehoert()
    {
        // Der Punkt, an dem die Gruppe vorher keine war: wer beitrat, sah
        // niemanden — die Liste hing an ManageTournament, und ein Mitglied
        // bekam ein 404. Jetzt trägt sie Permission.ViewMembers.
        var (leitung, tournamentId) = await TurnierAsync();
        var (mitglied, email) = await AngemeldeterBenutzerAsync("liste");

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Member),
            Json);

        var rollen = await mitglied.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        // Es sieht sich selbst und die Turnierleitung — mit Namen.
        Assert.Equal(2, rollen!.Count);
        Assert.Contains(rollen, r => r.Role == Role.TournamentDirector);
        Assert.Contains(rollen, r => r.Role == Role.Member);
    }

    [Fact]
    public async Task Ein_Mitglied_sieht_weder_Adressen_noch_offene_Einladungen()
    {
        // Wer dazugehört, ist eine Auskunft an die Gruppe. Die Adresse eines
        // anderen ist es nicht, und eine offene Einladung ist eine Absicht der
        // Turnierleitung — beides bleibt bei ihr.
        var (leitung, tournamentId) = await TurnierAsync();
        var (mitglied, email) = await AngemeldeterBenutzerAsync("sparsam");

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest(email, Role.Member),
            Json);

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/roles",
            new GrantRoleRequest($"noch.nicht.{Guid.NewGuid():N}"[..28] + "@example.invalid", Role.Member),
            Json);

        var ausSicht = await mitglied.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.All(ausSicht!, r => Assert.Null(r.Email));
        Assert.DoesNotContain(ausSicht!, r => r.Pending);

        // Die Turnierleitung sieht beides — sonst wüsste sie nicht, auf wen
        // sie noch wartet.
        var ausLeitung = await leitung.GetFromJsonAsync<List<TournamentRoleSummary>>(
            $"/api/tournaments/{tournamentId}/roles", Json);

        Assert.Contains(ausLeitung!, r => r.Email == email);
        Assert.Single(ausLeitung!, r => r.Pending);
    }

    [Fact]
    public async Task Ein_Fremder_sieht_die_Liste_weiterhin_nicht()
    {
        // Sie hängt am Turnier und nicht am Angemeldetsein: wer nicht
        // dazugehört, bekommt 404 — nicht 403, der verriete die Existenz.
        var (_, tournamentId) = await TurnierAsync();
        var fremder = _factory.CreateClientAs($"fremd-{Guid.NewGuid():N}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fremder.GetAsync($"/api/tournaments/{tournamentId}/roles")).StatusCode);
    }

    [Fact]
    public async Task Das_Turnier_sagt_dem_Aufrufer_was_er_darf()
    {
        // Damit die Maske nicht raten muss. Sie ist eine Auskunft für die
        // Darstellung und keine Zusicherung — entschieden wird weiterhin am
        // Endpunkt.
        var (leitung, tournamentId) = await TurnierAsync();
        var (mitglied, mitgliedMail) = await AngemeldeterBenutzerAsync("darf");
        var (schiri, schiriMail) = await AngemeldeterBenutzerAsync("pfeift");

        foreach (var (mail, rolle) in new[] { (mitgliedMail, Role.Member), (schiriMail, Role.Referee) })
        {
            await leitung.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/roles",
                new GrantRoleRequest(mail, rolle),
                Json);
        }

        var alsLeitung = await leitung.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);
        var alsMitglied = await mitglied.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);
        var alsSchiri = await schiri.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);

        Assert.Equal(new TournamentAbilities(true, true), alsLeitung!.You);
        Assert.Equal(new TournamentAbilities(false, false), alsMitglied!.You);
        Assert.Equal(new TournamentAbilities(false, true), alsSchiri!.You);
    }
}
