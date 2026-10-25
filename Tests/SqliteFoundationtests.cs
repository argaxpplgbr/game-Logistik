using System.Diagnostics;
using Dapper;
using Microsoft.Data.Sqlite;
using Nusantara.Database;
using Nusantara.Services;
using Xunit;

namespace Nusantara.Tests;

// Semua tes yang menyentuh Db/SimClock ada di satu collection supaya berjalan berurutan.
[Collection("sqlite")]
public class SqliteFoundationTests : IDisposable
{
    readonly string dir;
    readonly Func<long> oldStamp;
    long fake = 1_000;

    public SqliteFoundationTests()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        Db.Init();
        dir = Path.Combine(Path.GetTempPath(), "nusantara-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", dir);
        oldStamp = SimClock.Stamp;
        SimClock.Stamp = () => fake;     // jam palsu: waktu hanya maju kalau tes yang memajukan
    }

    public void Dispose()
    {
        SaveSlots.Shutdown();
        SimClock.Unload();
        SimClock.Stamp = oldStamp;
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", null);
        try { Directory.Delete(dir, true); } catch { /* file sementara */ }
    }

    void Advance(double minutes) => fake += (long)(minutes * 60 * Stopwatch.Frequency);

    [Fact]
    public void Clock_advances_only_while_running()
    {
        SimClock.Load(0, 1);
        Advance(5);
        Assert.Equal(5.0, SimClock.Minutes, 6);

        SimClock.Pause();
        Advance(100);
        Assert.Equal(5.0, SimClock.Minutes, 6);

        SimClock.Resume();
        Advance(2);
        Assert.Equal(7.0, SimClock.Minutes, 6);

        SimClock.SetSpeed(2);
        Advance(1);
        Assert.Equal(9.0, SimClock.Minutes, 6);
    }

    [Fact]
    public void New_slot_has_starting_cash_and_balanced_ledger()
    {
        int pid = SaveSlots.Create(1, "PT Uji Coba");
        var p = PlayerSvc.Get(pid);

        Assert.Equal(EconomySvc.StartingCash, p.Cash);
        Assert.Equal(SimClock.Epoch, p.CreatedAt);
        var (sum, cash) = LedgerSvc.Reconcile(pid);
        Assert.Equal(cash, sum);
    }

    [Fact]
    public void Closed_game_does_not_advance_time()
    {
        SaveSlots.Create(1, "PT Tutup");
        Advance(10);
        SaveSlots.Persist();
        double saved = SimClock.Minutes;
        Assert.Equal(10.0, saved, 4);

        SaveSlots.Shutdown();
        Advance(60 * 24 * 5);            // lima hari nyata berlalu saat game tertutup

        SaveSlots.Open(1);
        Assert.Equal(saved, SimClock.Minutes, 4);
    }

    [Fact]
    public void Slots_are_isolated_from_each_other()
    {
        int a = SaveSlots.Create(1, "Perusahaan A");
        using (var c = Db.Open()) c.Execute("UPDATE players SET cash=123 WHERE id=@a", new { a });
        SaveSlots.Shutdown();

        int b = SaveSlots.Create(2, "Perusahaan B");
        Assert.Equal(EconomySvc.StartingCash, PlayerSvc.Get(b).Cash);
        SaveSlots.Shutdown();

        int a2 = SaveSlots.Open(1);
        Assert.Equal(123L, PlayerSvc.Get(a2).Cash);
        Assert.Equal("Perusahaan A", PlayerSvc.Get(a2).Name);
    }

    [Fact]
    public void Migrations_are_idempotent_and_seed_catalog_exists()
    {
        SaveSlots.Create(1, "PT Migrasi");
        Db.Migrate();
        Db.Migrate();

        using var c = Db.Open();
        Assert.Equal(Db.SchemaVersion, c.ExecuteScalar<int>("SELECT COUNT(*) FROM migration_history WHERE success=1"));
        Assert.Equal(0, Db.PendingMigrations());
        Assert.Equal(13, c.ExecuteScalar<int>("SELECT COUNT(*) FROM truck_catalog"));
        Assert.Equal(28, c.ExecuteScalar<int>("SELECT COUNT(*) FROM nodes"));
    }

    [Fact]
    public void Backup_file_is_a_valid_database()
    {
        SaveSlots.Create(1, "PT Cadangan");
        Assert.StartsWith("Tersimpan", SaveSlots.Backup());

        var bak = SaveSlots.PathOf(1) + ".bak";
        Assert.True(File.Exists(bak));
        using var c = new SqliteConnection(Db.BuildConnStr(bak, createIfMissing: false, pooling: false));
        c.Open();
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM players"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("A", false)]
    [InlineData("PT Maju Jaya", true)]
    public void Company_name_validation(string raw, bool valid) =>
        Assert.Equal(valid, SaveSlots.ValidateName(raw, out _) == null);

    [Fact]
    public void Company_name_too_long_is_rejected() =>
        Assert.NotNull(SaveSlots.ValidateName(new string('x', 41), out _));
}