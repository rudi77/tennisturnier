using Microsoft.EntityFrameworkCore;
using TennisTurnier.Adapters.Persistence.Sqlite;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Adapters.Persistence.Tests;

public sealed class ZZProbeTests : IAsyncLifetime
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"probe-{Guid.NewGuid():N}.db");

    private Guid _clubA;
    private Guid _clubB;
    private Guid _courtB;

    private sealed class Ctx : IUserContext
    {
        public UserPrincipal Current { get; set; } = UserPrincipal.System;
    }

    private TennisTurnierDbContext New(IUserContext ctx)
    {
        var options = new DbContextOptionsBuilder<TennisTurnierDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options;
        return new TennisTurnierDbContext(options, ctx);
    }

    public async Task InitializeAsync()
    {
        await using var db = New(new Ctx());
        await db.Database.MigrateAsync();

        var a = new Club(Guid.NewGuid(), "TC Alpha", "Europe/Vienna");
        var ca = a.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);
        ca.AddBlock(Guid.NewGuid(), Slot(10), BlockReason.Training, "geheim A");

        var b = new Club(Guid.NewGuid(), "TC Beta", "Europe/Vienna");
        var cb = b.AddCourt(Guid.NewGuid(), "Platz 1", CourtSurface.Hard, CourtLocation.Indoor);
        cb.AddAvailability(Guid.NewGuid(), DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(20, 0), new DateOnly(2026, 1, 1));
        cb.AddBlock(Guid.NewGuid(), Slot(12), BlockReason.Training, "geheim B");

        db.Clubs.AddRange(a, b);
        await db.SaveChangesAsync();

        _clubA = a.Id;
        _clubB = b.Id;
        _courtB = cb.Id;
    }

    private static TimeSlot Slot(int hour) => new(
        new DateTimeOffset(2026, 5, 16, hour, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 5, 16, hour + 1, 0, 0, TimeSpan.Zero));

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, $"{_path}-shm", $"{_path}-wal" })
        {
            if (File.Exists(f)) File.Delete(f);
        }
        return Task.CompletedTask;
    }

    private UserPrincipal AdminOf(Guid clubId)
    {
        var u = Guid.NewGuid();
        return new UserPrincipal(u, [new RoleAssignment(Guid.NewGuid(), u, Role.ClubAdmin, ResourceScope.Club(clubId))]);
    }

    [Fact]
    public async Task Probe_A_getrennte_UserContext_Instanzen()
    {
        // Wie in der echten App: jeder Request hat SEINE EIGENE IUserContext-Instanz.
        await using (var db1 = New(new Ctx { Current = AdminOf(_clubA) }))
        {
            var ids = await db1.Clubs.Select(c => c.Id).ToListAsync();
            Assert.Equal([_clubA], ids);
        }

        await using (var db2 = New(new Ctx { Current = AdminOf(_clubB) }))
        {
            var ids = await db2.Clubs.Select(c => c.Id).ToListAsync();
            Assert.Equal([_clubB], ids);
        }
    }

    [Fact]
    public async Task Probe_B_CourtBlock_und_AvailabilityWindow_ohne_Filter()
    {
        await using var db = New(new Ctx { Current = AdminOf(_clubA) });

        var blocks = await db.Set<CourtBlock>().Select(b => b.Note).ToListAsync();
        var windows = await db.Set<AvailabilityWindow>().CountAsync();

        // Erwartung falls Filter fehlt: beide Notizen sichtbar.
        Console.WriteLine($"PROBE-B blocks=[{string.Join(",", blocks)}] windows={windows}");
        Assert.Fail($"PROBE-B blocks=[{string.Join(",", blocks)}] windows={windows}");
    }

    [Fact]
    public async Task Probe_C_Navigation_von_ungefiltertem_Kind_zum_fremden_Court()
    {
        await using var db = New(new Ctx { Current = AdminOf(_clubA) });

        // Kann man über die ungefilterte Kindtabelle den fremden Platz identifizieren?
        var courtIds = await db.Set<CourtBlock>().Select(b => b.CourtId).ToListAsync();
        Assert.Fail($"PROBE-C courtIds=[{string.Join(",", courtIds)}] fremd={_courtB}");
    }

    [Fact]
    public async Task Probe_D_Court_umbenennen_auf_vorhandenen_Namen()
    {
        await using var db = New(new Ctx());
        var club = await db.Clubs.SingleAsync(c => c.Id == _clubA);
        club.AddCourt(Guid.NewGuid(), "Platz 2", CourtSurface.Clay, CourtLocation.Outdoor);
        await db.SaveChangesAsync();

        var c2 = club.Courts.Single(x => x.Name == "Platz 2");
        c2.Rename("Platz 1");
        var ex = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        Assert.Fail($"PROBE-D exception={ex?.GetType().Name}: {ex?.Message}");
    }

    [Fact]
    public async Task Probe_E_generiertes_SQL_fuer_Clubs()
    {
        await using var db = New(new Ctx { Current = AdminOf(_clubA) });
        var sql = db.Clubs.ToQueryString();
        Assert.Fail("PROBE-E\n" + sql);
    }

    [Fact]
    public async Task Probe_F_gespeicherte_Textform_der_Zeitpunkte()
    {
        await using var db = New(new Ctx());
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT StartsAt, EndsAt, typeof(StartsAt) FROM CourtBlocks";
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await r.ReadAsync())
        {
            rows.Add($"{r.GetString(0)}|{r.GetString(1)}|{r.GetString(2)}");
        }
        Assert.Fail("PROBE-F " + string.Join(" ;; ", rows));
    }
}
