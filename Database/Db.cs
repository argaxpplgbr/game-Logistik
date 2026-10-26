using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Nusantara.Services;

namespace Nusantara.Database;

// Dapper tidak otomatis memahami DateTime di SQLite (disimpan sebagai TEXT), jadi ditangani di sini.
// Format tetap "yyyy-MM-dd HH:mm:ss.fff" supaya perbandingan teks (<=, >=) sama dengan urutan waktu.
public sealed class SimDateTimeHandler : SqlMapper.TypeHandler<DateTime>
{
    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = SimClock.ToDb(value);
    }

    public override DateTime Parse(object value) => value switch
    {
        DateTime d => d,
        string s => SimClock.FromDb(s),
        _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture)
    };
}

public static class Db
{
    public const string GameVersion = "4.0";

    sealed record Migration(int Version, string Name, string Sql);

    // Tambah migrasi baru di akhir daftar ini. Jangan ubah isi migrasi lama yang sudah dirilis.
    static readonly Migration[] Migrations =
    {
        new(1, "skema_dasar_sqlite", SchemaSqlite.V1),
        new(2, "event_ledger_berita", SchemaV2.Sql),
        new(3, "kesehatan_save_dan_job_log", SchemaV3.Sql),
        new(4, "perusahaan_ai_dan_pasar_bersama", SchemaV4.Sql)
    };

    static string? connStr;
    static string? filePath;
    static bool ready;

    public static int SchemaVersion => Migrations.Max(m => m.Version);
    public static int Generation { get; private set; }
    public static bool HasSlot => connStr != null;
    public static string FilePath => filePath ?? throw new InvalidOperationException("Belum ada slot save yang dibuka.");

    // Panggil sekali di awal program, sebelum query pertama.
    public static void Init()
    {
        if (ready) return;
        SqlMapper.RemoveTypeMap(typeof(DateTime));
        SqlMapper.RemoveTypeMap(typeof(DateTime?));
        SqlMapper.AddTypeHandler(new SimDateTimeHandler());
        ready = true;
    }

    public static string BuildConnStr(string file, bool createIfMissing = false, bool pooling = true) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = createIfMissing ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = pooling,
            ForeignKeys = true,
            DefaultTimeout = 15
        }.ToString();

    public static void UseFile(string file)
    {
        filePath = Path.GetFullPath(file);
        connStr = BuildConnStr(filePath, createIfMissing: true, pooling: true);
        Generation++;
    }

    // Lepas slot aktif dan tutup semua koneksi supaya file bisa dicadangkan/dihapus.
    public static void Release()
    {
        connStr = null;
        filePath = null;
        Generation++;
        SqliteConnection.ClearAllPools();
    }

    public static SqliteConnection Open()
    {
        if (connStr == null) throw new InvalidOperationException("Belum ada slot save yang dibuka.");
        var c = new SqliteConnection(connStr);
        c.Open();
        return c;
    }

    public static void EnableWal()
    {
        using var c = Open();
        c.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
    }

    public static string QuickCheck()
    {
        using var c = Open();
        return c.ExecuteScalar<string>("PRAGMA quick_check") ?? "tidak ada hasil";
    }

    // Jumlah migrasi yang belum diterapkan pada slot aktif.
    public static int PendingMigrations()
    {
        using var c = Open();
        bool has = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='migration_history'") > 0;
        if (!has) return Migrations.Length;
        var applied = c.Query<int>("SELECT version FROM migration_history WHERE success=1").ToHashSet();
        return Migrations.Count(m => !applied.Contains(m.Version));
    }

    // Aman dipanggil berkali-kali. Tiap migrasi satu transaksi: gagal = batal, data tidak berubah.
    public static void Migrate()
    {
        using var c = Open();
        c.Execute(@"CREATE TABLE IF NOT EXISTS migration_history (
            version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at_utc TEXT NOT NULL,
            checksum TEXT NOT NULL, success INTEGER NOT NULL)");
        var applied = c.Query<int>("SELECT version FROM migration_history WHERE success=1").ToHashSet();

        foreach (var m in Migrations.OrderBy(x => x.Version))
        {
            if (applied.Contains(m.Version)) continue;
            using var tx = c.BeginTransaction();
            try
            {
                c.Execute(m.Sql, transaction: tx);
                string sum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(m.Sql)))[..16];
                c.Execute(@"INSERT INTO migration_history(version,name,applied_at_utc,checksum,success)
                    VALUES(@v,@n,@at,@sum,1)",
                    new { v = m.Version, n = m.Name, at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), sum }, tx);
                tx.Commit();
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { /* sudah batal */ }
                throw new InvalidOperationException($"Migrasi {m.Version} ({m.Name}) gagal: {ex.Message}", ex);
            }
        }
    }

    // Potong kas atomik + catat ledger. Berhasil hanya jika kas cukup. Dipakai di dalam transaksi.
    // Kategori kosong = nama method pemanggil (dipetakan ke label di LedgerSvc).
    public static bool Spend(IDbConnection c, IDbTransaction tx, int pid, long cost,
        string cat = "", string note = "", int? tripId = null, [CallerMemberName] string caller = "")
    {
        if (cost <= 0) return true;
        if (c.Execute("UPDATE players SET cash=cash-@cost WHERE id=@pid AND cash>=@cost", new { cost, pid }, tx) != 1) return false;
        Log(c, tx, pid, -cost, cat == "" ? caller : cat, note, tripId);
        return true;
    }

    // Tambah/kurangi kas tanpa cek saldo (pemasukan, atau biaya yang memang boleh membuat kas minus).
    // Mengembalikan saldo setelah transaksi. Nilai 0 tidak dicatat.
    public static long Post(IDbConnection c, IDbTransaction tx, int pid, long amount,
        string cat, string note = "", int? tripId = null)
    {
        if (amount == 0) return c.ExecuteScalar<long>("SELECT cash FROM players WHERE id=@pid", new { pid }, tx);
        c.Execute("UPDATE players SET cash=cash+@amount WHERE id=@pid", new { amount, pid }, tx);
        return Log(c, tx, pid, amount, cat, note, tripId);
    }

    // Tulis satu baris ledger (tidak mengubah kas). Mengembalikan saldo saat ini.
    // Waktu ledger memakai jam SIMULASI, bukan jam dinding.
    public static long Log(IDbConnection c, IDbTransaction tx, int pid, long amount,
        string cat, string note = "", int? tripId = null)
    {
        long bal = c.ExecuteScalar<long>("SELECT cash FROM players WHERE id=@pid", new { pid }, tx);
        var created = SimClock.FromDb(c.ExecuteScalar<string>("SELECT created_at FROM players WHERE id=@pid", new { pid }, tx)!);
        var now = SimClock.Now;
        int day = (int)((now - created).TotalMinutes / Rules.DayMinutes);
        if (note.Length > 200) note = note[..200];
        c.Execute(@"INSERT INTO ledger(player_id,game_day,created_at,category,note,trip_id,amount,balance_after)
            VALUES(@pid,@day,@now,@cat,@note,@tripId,@amount,@bal)",
            new { pid, day, now, cat, note, tripId, amount, bal }, tx);
        return bal;
    }
}