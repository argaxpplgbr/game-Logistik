using System.Data;
using Dapper;
using MySqlConnector;

namespace Nusantara.Database;

public static class Db
{
   // JIKA PAKE XAMPP PASSOWRD KOSONGKAN 
    const string Placeholder =
        "Server=localhost;Port=3306;Database=nusantara_logistics;User=root;Password=PASSWORD_MU;";

    public static readonly string ConnStr =
        Environment.GetEnvironmentVariable("NUSANTARA_DB_CONN") is { Length: > 0 } s ? s : Placeholder;

    public static MySqlConnection Open()
    {
        var c = new MySqlConnection(ConnStr);
        c.Open();
        return c;
    }

    // Potong kas secara atomik: berhasil hanya jika kas cukup. Dipakai di dalam transaksi.
    public static bool Spend(IDbConnection c, IDbTransaction tx, int pid, long cost)
    {
        if (cost <= 0) return true;
        return c.Execute("UPDATE players SET cash=cash-@cost WHERE id=@pid AND cash>=@cost", new { cost, pid }, tx) == 1;
    }

    // Menambah kolom baru kalau belum ada (aman dijalankan berkali-kali, tidak menghapus data).
    public static void EnsureMigrations()
    {
        using var c = Open();
        bool Has(string col) => c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='trips' AND COLUMN_NAME=@col",
            new { col }) > 0;

        // Snapshot polis asuransi saat truk berangkat
        if (!Has("ins_type")) c.Execute("ALTER TABLE trips ADD COLUMN ins_type VARCHAR(10) NOT NULL DEFAULT 'NONE'");
        if (!Has("cargo_ins")) c.Execute("ALTER TABLE trips ADD COLUMN cargo_ins TINYINT(1) NOT NULL DEFAULT 0");
    }
}