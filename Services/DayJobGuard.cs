using System.Data;
using System.Globalization;
using Dapper;

namespace Nusantara.Services;

// Penjaga pekerjaan berkala. Dipakai DI DALAM transaksi pekerjaan itu sendiri:
//   if (!DayJobGuard.TryClaim(c, tx, "GAJI_HARIAN", day, day)) return;   // sudah pernah dijalankan
//   ...lakukan pekerjaan dan posting uang di transaksi yang sama...
// Kalau transaksi dibatalkan, klaim ikut batal, jadi pekerjaan bisa diulang dengan benar.
public static class DayJobGuard
{
    public static bool TryClaim(IDbConnection c, IDbTransaction tx, string job, int period, int currentDay)
    {
        int n = c.Execute(@"INSERT OR IGNORE INTO sim_job_log(job,period,claimed_day,claimed_at_utc)
            VALUES(@job,@period,@currentDay,@at)",
            new { job, period, currentDay, at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) }, tx);
        return n == 1;
    }

    public static bool WasRun(IDbConnection c, string job, int period) =>
        c.ExecuteScalar<int>("SELECT COUNT(*) FROM sim_job_log WHERE job=@job AND period=@period", new { job, period }) > 0;
}