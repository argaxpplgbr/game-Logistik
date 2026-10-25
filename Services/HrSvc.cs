using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

public static class HrSvc
{
    static readonly string[] First = { "Slamet", "Agus", "Budi", "Joko", "Rudi", "Hendra", "Dedi", "Yanto", "Eko", "Bambang", "Usman", "Wahyu", "Asep", "Ujang", "Tono", "Supri" };
    static readonly string[] Last = { "Santoso", "Wijaya", "Siregar", "Nasution", "Hidayat", "Saputra", "Pratama", "Lubis", "Gunawan", "Setiawan", "Ritonga", "Kusuma" };

    public static List<Driver> Candidates(int pid) { using var c = Db.Open(); return c.Query<Driver>("SELECT * FROM drivers WHERE player_id=@pid AND status='CANDIDATE'", new { pid }).ToList(); }
    public static List<Driver> Staff(int pid) { using var c = Db.Open(); return c.Query<Driver>("SELECT * FROM drivers WHERE player_id=@pid AND status IN ('IDLE','ON_TRIP')", new { pid }).ToList(); }

    public static string Refresh(int pid, bool charge)
    {
        var p = PlayerSvc.Get(pid); var R = Rules.Rng;
        using var c = Db.Open(); using var tx = c.BeginTransaction();
        if (charge && !Db.Spend(c, tx, pid, 250_000)) return "Uang kurang untuk pasang lowongan (Rp 250.000).";
        c.Execute("DELETE FROM drivers WHERE player_id=@pid AND status='CANDIDATE'", new { pid }, tx);
        for (int i = 0; i < 6; i++)
        {
            string sim = p.Tier == 1 ? (R.NextDouble() < 0.8 ? "A" : "B1")
                : p.Tier == 2 ? (R.NextDouble() < 0.6 ? "B1" : (R.NextDouble() < 0.5 ? "A" : "B2"))
                : (R.NextDouble() < 0.7 ? "B2" : "B1");
            int exp = R.Next(0, 21), honesty = R.Next(2, 11);
            long baseSal = sim == "A" ? 3_200_000 : sim == "B1" ? 4_800_000 : 6_500_000;
            long sal = (long)(baseSal * (1 + exp * 0.02) * (0.9 + R.NextDouble() * 0.25)) / 50_000 * 50_000;
            c.Execute(@"INSERT INTO drivers(player_id,name,sim,speed,eco,honesty,honesty_claim,exp_years,crash_count,salary_exp,uj_exp,commission_pct,morale,status)
                VALUES(@pid,@name,@sim,@sp,@eco,@h,@hc,@exp,@cr,@sal,@uj,@com,70,'CANDIDATE')",
                new { pid, name = First[R.Next(First.Length)] + " " + Last[R.Next(Last.Length)], sim, sp = R.Next(3, 11), eco = R.Next(2, 11),
                      h = honesty, hc = Math.Min(10, honesty + R.Next(0, 4)), exp, cr = R.Next(0, 4), sal, uj = 1.0 + R.Next(0, 6) * 0.05, com = 1 + R.Next(0, 5) }, tx);
        }
        tx.Commit();
        return charge ? "Lowongan baru dipasang, 6 CV masuk." : "";
    }

    public static string Interview(int pid, int id)
    {
        using var c = Db.Open(); using var tx = c.BeginTransaction();
        int n = c.Execute("UPDATE drivers SET interviewed=1 WHERE id=@id AND player_id=@pid AND status='CANDIDATE'", new { id, pid }, tx);
        if (n == 0) return "Kandidat tidak ada.";
        if (!Db.Spend(c, tx, pid, 100_000)) return "Uang kurang (Rp 100.000).";
        var d = c.QueryFirst<Driver>("SELECT * FROM drivers WHERE id=@id", new { id }, tx);
        tx.Commit();
        return $"Hasil interview {d.Name}: kejujuran ASLI {d.Honesty}/10 (di CV: {d.HonestyClaim}), riwayat nabrak {d.CrashCount}x.";
    }

    public static string Hire(int pid, int id, int offerPct)
    {
        using var c = Db.Open();
        var d = c.QueryFirstOrDefault<Driver>("SELECT * FROM drivers WHERE id=@id AND player_id=@pid AND status='CANDIDATE'", new { id, pid });
        if (d == null) return "Kandidat tidak ada.";
        var offerErr = Rules.ValidateOffer(offerPct);
        if (offerErr != null) return offerErr;

        int limit = Rules.MaxDrivers(FleetSvc.Fleet(pid).Count);
        if (Staff(pid).Count >= limit)
            return $"Jumlah supir sudah mencapai batas ({limit} = jumlah truk + 1 cadangan). Beli truk dulu sebelum menambah supir.";

        long sal = d.SalaryExp * offerPct / 100; int morale = Rules.Clamp(60 + (offerPct - 100), 20, 95);
        int n = c.Execute("UPDATE drivers SET status='IDLE',salary=@sal,morale=@morale WHERE id=@id AND status='CANDIDATE'", new { sal, morale, id });
        if (n == 0) return "Kandidat tidak ada.";
        Events.Append(c, null, "DRIVER_FIRED", new {name = d.Name, sim = d.Sim, salary = sal}, pid);
        long monthly = sal + (long)(sal * 0.1024);
        return $"{d.Name} (SIM {d.Sim}) diterima. Gaji {Rules.Rp(sal)}/bln + BPJS (beban tetap {Rules.Rp(monthly)}/bln), komisi {d.CommissionPct}%.";
    }

    public static string Fire(int pid, int id)
    {
        using var c = Db.Open();
        int n = c.Execute("UPDATE drivers SET status='QUIT' WHERE id=@id AND player_id=@pid AND status='IDLE'", new { id, pid });
        if (n == 0) return "Supir tidak ada / sedang jalan.";
        c.Execute("UPDATE trucks SET driver_id=NULL WHERE driver_id=@id", new { id });
        Events.Append(c, null, "DRIVER_FIRED", new { name = c.ExecuteScalar<string>("SELECT name FROM drivers WHERE id=@id", new { id }) ?? "supir" }, pid);
        return "Supir diberhentikan.";
    }

    public static string Raise(int pid, int id, int pct)
    {
        var err = Rules.ValidateRaise(pct);
        if (err != null) return err;
        int gain = Rules.RaiseMorale(pct);
        using var c = Db.Open();
        int n = c.Execute("UPDATE drivers SET salary=salary*(100+@pct)/100,morale=MIN(100,morale+@gain) WHERE id=@id AND player_id=@pid AND status IN('IDLE','ON_TRIP')", new { pct, gain, id, pid });
        return n > 0 ? $"Gaji naik {pct}%, moral +{gain}." : "Supir tidak ada.";
    }
}