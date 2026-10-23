using Dapper;
using Nusantara.Database;

namespace Nusantara.Services;

public class TutorialStep { public int No; public string Title = ""; public string Hint = ""; public bool Done; }

// Progres tutorial DIHITUNG dari kondisi nyata game, jadi tidak mengunci menu lain dan tidak hilang kalau pemain salah langkah.
public static class TutorialSvc
{
    public static int State(int pid)
    {
        using var c = Db.Open();
        return c.ExecuteScalar<int>("SELECT tutorial_state FROM players WHERE id=@pid", new { pid });
    }

    public static List<TutorialStep> Steps(int pid)
    {
        using var c = Db.Open();
        int Count(string sql) => c.ExecuteScalar<int>(sql, new { pid });
        int trucks = Count("SELECT COUNT(*) FROM trucks WHERE player_id=@pid AND status<>'TOTALLOSS'");
        int staff = Count("SELECT COUNT(*) FROM drivers WHERE player_id=@pid AND status IN ('IDLE','ON_TRIP')");
        int assigned = Count("SELECT COUNT(*) FROM trucks WHERE player_id=@pid AND status<>'TOTALLOSS' AND driver_id IS NOT NULL");
        int started = Count("SELECT COUNT(*) FROM trips WHERE player_id=@pid");
        int done = Count("SELECT COUNT(*) FROM trips WHERE player_id=@pid AND status='DONE'");
        int seen = Count("SELECT tutorial_report FROM players WHERE id=@pid");

        return new List<TutorialStep>
        {
            new() { No = 1, Done = trucks >= 1, Title = "Beli truk pertama",
                    Hint = "Menu 2 Dealer > Truk baru. Pilih Grand Maxx Pick-Up atau Blind Van (bak sudah terpasang, langsung jalan)." },
            new() { No = 2, Done = staff >= 1, Title = "Rekrut supir",
                    Hint = "Menu 4 Supir > lihat CV > (opsional interview Rp 100.000 untuk tahu kejujuran asli) > Rekrut. Tier 1 cukup SIM A." },
            new() { No = 3, Done = assigned >= 1, Title = "Pasangkan supir ke truk",
                    Hint = "Menu 1 Armada > Pasang supir ke truk." },
            new() { No = 4, Done = started >= 1, Title = "Pilih kontrak yang LAYAK lalu berangkat",
                    Hint = "Menu 5 Kontrak & Berangkat. Pilih filter 'layak', baca preview biaya & laba, lalu konfirmasi." },
            new() { No = 5, Done = done >= 1, Title = "Tunggu truk tiba",
                    Hint = "Cek menu 6 Perjalanan. Hasil muncul otomatis saat kamu kembali ke menu utama." },
            new() { No = 6, Done = seen >= 1, Title = "Baca laporan laba/rugi trip",
                    Hint = "Menu 6 Perjalanan > ketik ID trip yang selesai." }
        };
    }

    public static string? Hint(int pid)
    {
        if (State(pid) != 0) return null;
        var steps = Steps(pid);
        var next = steps.FirstOrDefault(s => !s.Done);
        if (next == null) return null;
        return $"PANDUAN {next.No}/{steps.Count}: {next.Title}. {next.Hint}  (menu 13 untuk detail/lewati)";
    }

    public static string Skip(int pid)
    {
        using var c = Db.Open();
        return c.Execute("UPDATE players SET tutorial_state=2 WHERE id=@pid AND tutorial_state=0", new { pid }) == 1
            ? "Tutorial dilewati. Kamu masih bisa memakai semua menu seperti biasa (tanpa hadiah tutorial)."
            : "Tutorial sudah selesai atau pernah dilewati.";
    }

    // Dipanggil saat pemain membuka laporan trip yang sudah selesai
    public static string? OnReportViewed(int pid, int tripId)
    {
        using var c = Db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM trips WHERE id=@tripId AND player_id=@pid AND status='DONE'", new { tripId, pid }) == 0) return null;
        c.Execute("UPDATE players SET tutorial_report=1 WHERE id=@pid AND tutorial_state=0", new { pid });
        return TryComplete(pid);
    }

    // Hadiah hanya sekali: klaim state dan kredit kas dalam satu transaksi.
    static string? TryComplete(int pid)
    {
        if (State(pid) != 0 || Steps(pid).Any(s => !s.Done)) return null;
        using var c = Db.Open(); using var tx = c.BeginTransaction();
        if (c.Execute("UPDATE players SET tutorial_state=1, tutorial_reward=1 WHERE id=@pid AND tutorial_state=0 AND tutorial_reward=0", new { pid }, tx) != 1)
            return null;
        Db.Post(c, tx, pid, Accounting.TutorialReward, "HADIAH_TUTORIAL", "Tutorial selesai");
        tx.Commit();
        return $"TUTORIAL SELESAI! Hadiah satu kali {Rules.Rp(Accounting.TutorialReward)} masuk ke kas.";
    }
}