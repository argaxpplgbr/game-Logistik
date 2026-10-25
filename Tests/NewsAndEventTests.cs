using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Nusantara.Database;
using Nusantara.Services;
using Xunit;

namespace Nusantara.Tests;

// Komposer berita: murni, tanpa database.
public class NewsComposerTests
{
    static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact]
    public void Headline_numbers_come_only_from_payload()
    {
        var d = NewsComposer.Compose("TRUCK_BOUGHT",
            Json("{\"company\":\"PT Uji\",\"model\":\"Grand Maxx Pick-Up\",\"price\":190000000,\"used\":false}"), 7)!;
        Assert.Equal("ARMADA", d.Category);
        Assert.Contains(Rules.Rp(190_000_000), d.Body);
        Assert.Contains("Grand Maxx Pick-Up", d.Headline + d.Body);
    }

    [Fact]
    public void Same_event_always_gives_same_text()
    {
        var p = Json("{\"tripId\":3,\"truck\":\"Elf\",\"origin\":\"Jakarta\",\"dest\":\"Bogor\",\"ok\":true,\"income\":5000000,\"net\":1200000}");
        var a = NewsComposer.Compose("TRIP_COMPLETED", p, 42)!;
        var b = NewsComposer.Compose("TRIP_COMPLETED", p, 42)!;
        Assert.Equal(a, b);
    }

    [Fact]
    public void Failed_trip_does_not_read_like_success()
    {
        var d = NewsComposer.Compose("TRIP_COMPLETED",
            Json("{\"tripId\":9,\"origin\":\"Jakarta\",\"dest\":\"Medan\",\"ok\":false,\"total\":true,\"net\":-90000000}"), 1)!;
        Assert.Contains("gagal", d.Headline);
        Assert.Contains("total loss", d.Body);
    }

    [Fact]
    public void Unknown_or_silent_event_types_make_no_news()
    {
        Assert.Null(NewsComposer.Compose("TRUCK_SERVICED", Json("{}"), 1));
        Assert.Null(NewsComposer.Compose("TIPE_TIDAK_ADA", Json("{}"), 1));
    }
}

[Collection("sqlite")]
public class EventLedgerTests : IDisposable
{
    readonly string dir;
    readonly Func<long> oldStamp;
    long fake = 1_000;

    public EventLedgerTests()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        Db.Init();
        dir = Path.Combine(Path.GetTempPath(), "nusantara-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", dir);
        oldStamp = SimClock.Stamp;
        SimClock.Stamp = () => fake;
    }

    public void Dispose()
    {
        SaveSlots.Shutdown();
        SimClock.Unload();
        SimClock.Stamp = oldStamp;
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", null);
        try { Directory.Delete(dir, true); } catch { /* sementara */ }
    }

    void Advance(double minutes) => fake += (long)(minutes * 60 * Stopwatch.Frequency);

    [Fact]
    public void New_company_creates_founding_event_and_news()
    {
        SaveSlots.Create(1, "PT Berita");
        using var c = Db.Open();
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM event_ledger WHERE type='COMPANY_FOUNDED'"));
        Assert.True(c.ExecuteScalar<int>("SELECT COUNT(*) FROM news_item WHERE headline LIKE '%PT Berita%'") >= 1);
    }

    [Fact]
    public void Event_sequence_increases_and_ledger_is_immutable()
    {
        int pid = SaveSlots.Create(1, "PT Urutan");
        using var c = Db.Open();
        long a = Events.Append(c, null, "TRUCK_SERVICED", new { model = "X" }, pid, news: false);
        long b = Events.Append(c, null, "TRUCK_SERVICED", new { model = "Y" }, pid, news: false);
        Assert.True(b > a && a > 0);
        Assert.Equal(c.ExecuteScalar<long>("SELECT MAX(sequence) FROM event_ledger"), c.ExecuteScalar<long>("SELECT last_event_sequence FROM game_state"));
        Assert.Throws<SqliteException>(() => c.Execute("UPDATE event_ledger SET type='X'"));
        Assert.Throws<SqliteException>(() => c.Execute("DELETE FROM event_ledger"));
    }

    [Fact]
    public void Event_inside_rolled_back_transaction_disappears()
    {
        int pid = SaveSlots.Create(1, "PT Rollback");
        using var c = Db.Open();
        int before = c.ExecuteScalar<int>("SELECT COUNT(*) FROM event_ledger");
        using (var tx = c.BeginTransaction())
        {
            Events.Append(c, tx, "LOAN_TAKEN", new { amount = 1, months = 12, installment = 1 }, pid);
            // tidak di-Commit: rollback otomatis
        }
        Assert.Equal(before, c.ExecuteScalar<int>("SELECT COUNT(*) FROM event_ledger"));
    }

    [Fact]
    public void Digest_is_created_once_and_uses_ledger_facts()
    {
        int pid = SaveSlots.Create(1, "PT Digest");
        using (var c = Db.Open())
        {
            Events.Append(c, null, "TRIP_COMPLETED",
                new { tripId = 1, truck = "Elf", origin = "Jakarta", dest = "Bogor", ok = true, income = 5_000_000L, net = 1_234_000L, incident = false }, pid);
        }

        Advance(Rules.DayMinutes * 5.2);          // lewati hari ke-5
        var first = DigestSvc.RunDue(pid);
        var second = DigestSvc.RunDue(pid);

        Assert.Single(first);
        Assert.Empty(second);
        var d = NewsSvc.Digests(5).Single();
        Assert.Contains("1 selesai berhasil", d.Output);
        Assert.Contains(Rules.Rp(1_234_000), d.Output);
        Assert.Contains("hari 0 sampai 4", d.Output);
    }

    [Fact]
    public void Schema_version_is_at_least_two()
    {
        SaveSlots.Create(1, "PT Skema");
        Assert.True(Db.SchemaVersion >= 2);
        Assert.Equal(0, Db.PendingMigrations());
    }
}