using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

public static class WorldSvc
{
    static readonly (string code, string title, int days, double mag, string cargos)[] Templates =
    {
        ("SOLAR_LANGKA", "Solar Langka di Pantura! Antrean SPBU mengular, solar eceran melonjak.", 3, 2.5, ""),
        ("PANEN_SAWIT", "Panen Raya Sawit Sumatra: Kebutuhan Dump Truck Naik 200%!", 4, 3.0, "SAWIT,TAMBANG"),
        ("MUDIK", "Jelang Mudik Lebaran: Truk Sumbu 3 Dilarang Beroperasi di Jalan Tol.", 5, 1.0, ""),
        ("CUACA_BURUK", "Gelombang Tinggi: Penyeberangan Kapal Roro Terganggu.", 3, 1.0, ""),
        ("DEMAND_PABRIK", "Permintaan Pabrik Melonjak: Tarif Barang Pabrik & Tekstil Naik 50%!", 4, 1.5, "PABRIK,TEKSTIL"),
        ("RAZIA_ODOL", "Operasi Zero ODOL Digelar Serentak di Jembatan Timbang!", 3, 2.0, "")
    };

    public static List<WorldEvent> Active(int pid, int day)
    {
        using var c = Db.Open();
        return c.Query<WorldEvent>("SELECT * FROM world_events WHERE player_id=@pid AND start_day<=@day AND end_day>=@day", new { pid, day }).ToList();
    }
    public static List<WorldEvent> Recent(int pid)
    {
        using var c = Db.Open();
        return c.Query<WorldEvent>("SELECT * FROM world_events WHERE player_id=@pid ORDER BY id DESC LIMIT 8", new { pid }).ToList();
    }
    public static double RateMult(List<WorldEvent> ev, string cargo) =>
        ev.Where(e => e.Cargos != "" && e.Cargos.Split(',').Contains(cargo)).Aggregate(1.0, (a, e) => a * e.Magnitude);

    public static void Daily(int pid, int day)
    {
        var R = Rules.Rng; using var c = Db.Open();
        var active = Active(pid, day);
        if (R.NextDouble() < 0.35)
        {
            var t = Templates[R.Next(Templates.Length)];
            if (!active.Any(e => e.Code == t.code))
            {
                c.Execute("INSERT INTO world_events(player_id,code,title,start_day,end_day,magnitude,cargos) VALUES(@pid,@code,@title,@s,@e,@mag,@cg)",
                                new { pid, code = t.code, title = t.title, s = day, e = Rules.EventLastDay(day, t.days), mag = t.mag, cg = t.cargos });
                if (t.cargos != "") ContractSvc.Refresh(pid, day, t.cargos.Split(','), 3);
            }
        }
        ContractSvc.Refresh(pid, day);
        if (day % 5 == 0) FleetSvc.RefreshUsed(pid);
    }

    public static List<string> Tick(int pid)
    {
        var log = new List<string>(); var p = PlayerSvc.Get(pid);
        if (p.Bankrupt) return log;
        int day = PlayerSvc.Day(p);
        FleetSvc.FinishBodies(pid);
        log.AddRange(TripSvc.CompleteTrips(pid));
        if (day > p.LastDay)
        {
            for (int d = Math.Max(p.LastDay + 1, day - 9); d <= day; d++) Daily(pid, d);
            using var c = Db.Open();
            c.Execute("UPDATE players SET last_day=@day WHERE id=@pid", new { day, pid });
        }
        p = PlayerSvc.Get(pid); int guard = 0;
        while (day >= p.NextMonthDay && guard++ < 12)
        {
            log.AddRange(FinanceSvc.CloseMonth(pid));
            p = PlayerSvc.Get(pid);
            if (p.Bankrupt) return log;
        }
        if (p.Manager) log.AddRange(ManagerSvc.Run(pid));
        return log;
    }
}