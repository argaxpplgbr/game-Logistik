using Dapper;
using Nusantara.Database;
using Nusantara.Services;
using Xunit;

namespace Nusantara.Tests;

[Collection("sqlite")]
public class FoundationGapTests : IDisposable
{
    readonly string dir;
    readonly Func<long> oldStamp;
    readonly Func<int, List<string>> oldTick;

    public FoundationGapTests()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        Db.Init();
        dir = Path.Combine(Path.GetTempPath(), "nusantara-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", dir);
        oldStamp = SimClock.Stamp;
        oldTick = SimTicker.TickAction;
        long fake = 1_000;
        SimClock.Stamp = () => fake;
    }

    public void Dispose()
    {
        SimTicker.Stop();
        SimTicker.TickAction = oldTick;
        SaveHealth.FaultInjector = null;
        SaveSlots.Shutdown();
        SimClock.Unload();
        SimClock.Stamp = oldStamp;
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", null);
        try { Directory.Delete(dir, true); } catch { /* sementara */ }
    }

    // Menerapkan SchemaV3 secara manual, karena migrasi v3 belum didaftarkan di Db.Migrations.
    static void ApplyV3()
    {
        using var c = Db.Open();
        if (!SaveHealth.HasHealthColumns(c)) c.Execute(SchemaV3.Sql);
    }

    // ---------- IntegrityAudit ----------

    [Fact]
    public void Fresh_company_has_no_audit_errors()
    {
        SaveSlots.Create(1, "PT Bersih");
        Assert.False(IntegrityAudit.HasErrors(IntegrityAudit.Run()));
    }

    [Fact]
    public void Audit_detects_duplicate_trip_payment_and_orphan_ledger_trip()
    {
        int pid = SaveSlots.Create(1, "PT Ganda");
        using var c = Db.Open();
        long cash = c.ExecuteScalar<long>("SELECT cash FROM players WHERE id=@pid", new { pid });
        string now = SimClock.ToDb(SimClock.Now);
        for (int i = 0; i < 2; i++)
            c.Execute(@"INSERT INTO ledger(player_id,game_day,created_at,category,note,trip_id,amount,balance_after)
                VALUES(@pid,0,@now,'PENDAPATAN_TRIP','uji',999,0,@cash)", new { pid, now, cash });

        var issues = IntegrityAudit.Run(c);
        Assert.Contains(issues, i => i.Code == "DUPLICATE_TRIP_PAYMENT" && i.Severity == AuditSeverity.Error);
        Assert.Contains(issues, i => i.Code == "ORPHAN_LEDGER_TRIP");
    }

    [Fact]
    public void Audit_detects_cash_that_differs_from_ledger()
    {
        int pid = SaveSlots.Create(1, "PT Selisih");
        using var c = Db.Open();
        c.Execute("UPDATE players SET cash=cash+1 WHERE id=@pid", new { pid });
        Assert.Contains(IntegrityAudit.Run(c), i => i.Code == "CASH_LEDGER_MISMATCH");
    }

    [Fact]
    public void Audit_marks_old_COMPANY_FOUNDER_as_legacy_info_not_error()
    {
        int pid = SaveSlots.Create(1, "PT Lama");
        using var c = Db.Open();
        Events.Append(c, null, "COMPANY_FOUNDER", new { company = "PT Lama", cash = 1 }, pid, news: false);

        var issues = IntegrityAudit.Run(c);
        var legacy = Assert.Single(issues, i => i.Code == "LEGACY_EVENT_TYPE");
        Assert.Equal(AuditSeverity.Info, legacy.Severity);
        Assert.False(IntegrityAudit.HasErrors(issues));
    }

    // ---------- DayJobGuard ----------

    [Fact]
    public void Job_can_be_claimed_only_once_per_period()
    {
        SaveSlots.Create(1, "PT Job");
        ApplyV3();
        using var c = Db.Open();
        using var tx = c.BeginTransaction();
        Assert.True(DayJobGuard.TryClaim(c, tx, "GAJI_HARIAN", 3, 3));
        Assert.False(DayJobGuard.TryClaim(c, tx, "GAJI_HARIAN", 3, 3));
        Assert.True(DayJobGuard.TryClaim(c, tx, "GAJI_HARIAN", 4, 4));
        tx.Commit();
        Assert.True(DayJobGuard.WasRun(c, "GAJI_HARIAN", 3));
    }

    [Fact]
    public void Rolled_back_claim_can_be_claimed_again()
    {
        SaveSlots.Create(1, "PT Batal");
        ApplyV3();
        using var c = Db.Open();
        using (var tx = c.BeginTransaction())
        {
            Assert.True(DayJobGuard.TryClaim(c, tx, "BIAYA", 1, 1));
            // tidak di-Commit: klaim ikut batal
        }
        using var tx2 = c.BeginTransaction();
        Assert.True(DayJobGuard.TryClaim(c, tx2, "BIAYA", 1, 1));
    }

    // ---------- SaveHealth (skenario uji wajib no. 6) ----------

    [Fact]
    public void Failed_save_is_reported_and_slot_is_not_marked_ok()
    {
        SaveSlots.Create(1, "PT Gagal");
        ApplyV3();
        Assert.True(SaveHealth.TrySave().Ok);

        SaveHealth.FaultInjector = () => throw new IOException("disk penuh (simulasi)");
        var res = SaveHealth.TrySave();

        Assert.False(res.Ok);
        Assert.Contains("BELUM aman", res.Message);
        Assert.Contains("disk penuh", SaveHealth.LastError);
        var stored = SaveHealth.ReadStored();
        Assert.NotNull(stored);
        Assert.False(stored!.Value.Ok);

        SaveHealth.FaultInjector = null;
        Assert.True(SaveHealth.TrySave().Ok);
        Assert.True(SaveHealth.ReadStored()!.Value.Ok);
        Assert.Null(SaveHealth.LastError);
    }

    [Fact]
    public void Successful_save_persists_game_clock()
    {
        SaveSlots.Create(1, "PT Jam");
        ApplyV3();
        SimClock.Load(1234.5, 1);
        Assert.True(SaveHealth.TrySave().Ok);
        using var c = Db.Open();
        Assert.Equal(1234.5, c.ExecuteScalar<double>("SELECT sim_minutes FROM game_state WHERE id=1"), 3);
    }

    // ---------- SimTicker ----------

    [Fact]
    public void Ticker_never_runs_two_ticks_at_once_and_stops_cleanly()
    {
        int pid = SaveSlots.Create(1, "PT Ticker");
        int calls = 0;
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        SimTicker.TickAction = _ =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return new List<string> { "tick" };
        };

        var t = Task.Run(SimTicker.TickOnce);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        SimTicker.TickOnce();                       // tick kedua saat yang pertama masih jalan: harus diabaikan
        Assert.Equal(1, Volatile.Read(ref calls));

        release.Set();
        t.Wait(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "tick" }, SimTicker.Drain());

        SimTicker.Start(pid, TimeSpan.FromMinutes(10));
        Assert.True(SimTicker.IsStarted);
        SimTicker.Stop();
        Assert.False(SimTicker.IsStarted);
    }

    [Fact]
    public void Paused_clock_does_not_tick()
    {
        SaveSlots.Create(1, "PT Jeda");
        int calls = 0;
        SimTicker.TickAction = _ => { calls++; return new List<string>(); };
        SimClock.Pause();
        SimTicker.TickOnce();
        Assert.Equal(0, calls);
    }
}