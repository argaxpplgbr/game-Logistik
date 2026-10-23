using System.Data;
using System.Runtime.CompilerServices;
using Dapper;
using MySqlConnector;
using Nusantara.Services;

namespace Nusantara.Database;

public static class Db
{
   // JIKA PAKE XAMPP PASSOWRD KOSONGKAN 
    const string Placeholder =
        "Server=localhost;Port=3306;Database=nusantara_logistics;User=root;Password=belajar123;";

    public static readonly string ConnStr =
        Environment.GetEnvironmentVariable("NUSANTARA_DB_CONN") is { Length: > 0 } s ? s : Placeholder;

    public static MySqlConnection Open()
    {
        var c = new MySqlConnection(ConnStr);
        c.Open();
        return c;
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
    public static long Log(IDbConnection c, IDbTransaction tx, int pid, long amount,
        string cat, string note = "", int? tripId = null)
    {
        long bal = c.ExecuteScalar<long>("SELECT cash FROM players WHERE id=@pid", new { pid }, tx);
        var created = c.ExecuteScalar<DateTime>("SELECT created_at FROM players WHERE id=@pid", new { pid }, tx);
        int day = (int)((DateTime.UtcNow - created).TotalMinutes / Rules.DayMinutes);
        if (note.Length > 200) note = note[..200];
        c.Execute(@"INSERT INTO ledger(player_id,game_day,created_at,category,note,trip_id,amount,balance_after)
            VALUES(@pid,@day,@now,@cat,@note,@tripId,@amount,@bal)",
            new { pid, day, now = DateTime.UtcNow, cat, note, tripId, amount, bal }, tx);
        return bal;
    }

    // Menambah kolom/tabel baru kalau belum ada (aman dijalankan berkali-kali, tidak menghapus data).
    public static void EnsureMigrations()
    {
        using var c = Open();
        bool Has(string table, string col) => c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND COLUMN_NAME=@col",
            new { table, col }) > 0;

        // Snapshot polis asuransi saat truk berangkat
        if (!Has("trips", "ins_type")) c.Execute("ALTER TABLE trips ADD COLUMN ins_type VARCHAR(10) NOT NULL DEFAULT 'NONE'");
        if (!Has("trips", "cargo_ins")) c.Execute("ALTER TABLE trips ADD COLUMN cargo_ins TINYINT(1) NOT NULL DEFAULT 0");

        // v3: tutorial per pemain (0=aktif, 1=selesai, 2=dilewati)
        if (!Has("players", "tutorial_state")) c.Execute("ALTER TABLE players ADD COLUMN tutorial_state INT NOT NULL DEFAULT 0");
        if (!Has("players", "tutorial_reward")) c.Execute("ALTER TABLE players ADD COLUMN tutorial_reward INT NOT NULL DEFAULT 0");
        if (!Has("players", "tutorial_report")) c.Execute("ALTER TABLE players ADD COLUMN tutorial_report INT NOT NULL DEFAULT 0");

        // v3: buku kas. amount bertanda (+masuk / -keluar), integer rupiah.
        c.Execute(@"CREATE TABLE IF NOT EXISTS ledger (
            id INT AUTO_INCREMENT PRIMARY KEY, player_id INT NOT NULL, game_day INT NOT NULL, created_at DATETIME NOT NULL,
            category VARCHAR(30) NOT NULL, note VARCHAR(200) NOT NULL DEFAULT '', trip_id INT NULL,
            amount BIGINT NOT NULL, balance_after BIGINT NOT NULL,
            KEY idx_ledger_player (player_id, id), KEY idx_ledger_trip (trip_id))");
    }
}