using System.Globalization;
using Dapper;
using Nusantara.Database;

namespace Nusantara.Services;

public sealed record SaveResult(bool Ok, string Message);

// Penyimpanan jam game yang MELAPORKAN hasilnya (SaveSlots.Persist menelan error).
// Slot hanya ditandai berhasil (last_save_ok=1) kalau penulisan dan pembacaan ulang cocok.
// Kolom last_save_* ada di migrasi v3; kalau belum terpasang, status hanya disimpan di memori.
public static class SaveHealth
{
    sealed class HealthRow { public long Ok { get; set; } public string? Err { get; set; } }

    static readonly object gate = new();

    // Hanya untuk pengujian: dipanggil di tengah transaksi simpan untuk mensimulasikan kegagalan.
    public static Action? FaultInjector;

    public static string? LastError { get; private set; }

    static string UtcText() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static bool HasHealthColumns(System.Data.IDbConnection c) =>
        c.Query<string>("SELECT name FROM pragma_table_info('save_meta')").Contains("last_save_ok");

    public static SaveResult TrySave()
    {
        lock (gate)
        {
            if (!Db.HasSlot || !SimClock.Loaded)
                return new SaveResult(false, "Tidak ada slot aktif yang bisa disimpan.");

            double minutes = SimClock.Minutes, speed = SimClock.Speed;
            try
            {
                using var c = Db.Open();
                bool cols = HasHealthColumns(c);
                using (var tx = c.BeginTransaction())
                {
                    c.Execute("UPDATE game_state SET sim_minutes=@minutes, speed=@speed WHERE id=1", new { minutes, speed }, tx);
                    FaultInjector?.Invoke();
                    c.Execute("UPDATE save_meta SET updated_at_utc=@u WHERE id=1", new { u = UtcText() }, tx);
                    if (cols)
                        c.Execute("UPDATE save_meta SET last_save_ok=1, last_save_error=NULL, last_save_utc=@u WHERE id=1", new { u = UtcText() }, tx);
                    tx.Commit();
                }

                // Baca ulang: jangan percaya penulisan yang belum terbukti.
                double stored = c.ExecuteScalar<double>("SELECT sim_minutes FROM game_state WHERE id=1");
                if (Math.Abs(stored - minutes) > 1e-6)
                    throw new InvalidDataException("Data yang tersimpan tidak sama dengan data di memori.");

                LastError = null;
                return new SaveResult(true, "Tersimpan.");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                MarkFailed(ex.Message);
                return new SaveResult(false, "Gagal menyimpan, progres terakhir BELUM aman. Penyebab: " + ex.Message);
            }
        }
    }

    // Upaya terbaik menandai slot gagal-simpan. Kalau database-nya sendiri yang rusak, cukup status memori.
    static void MarkFailed(string reason)
    {
        try
        {
            using var c = Db.Open();
            if (!HasHealthColumns(c)) return;
            c.Execute("UPDATE save_meta SET last_save_ok=0, last_save_error=@r WHERE id=1",
                new { r = reason.Length > 300 ? reason[..300] : reason });
        }
        catch { /* sudah dicatat di LastError */ }
    }

    // Status slot yang tersimpan (untuk daftar slot). null = belum dikenal / skema belum v3.
    public static (bool Ok, string? Error)? ReadStored()
    {
        if (!Db.HasSlot) return null;
        using var c = Db.Open();
        if (!HasHealthColumns(c)) return null;
        var row = c.QueryFirst<HealthRow>("SELECT last_save_ok AS ok, last_save_error AS err FROM save_meta WHERE id=1");
        return (row.Ok == 1, row.Err);
    }
}