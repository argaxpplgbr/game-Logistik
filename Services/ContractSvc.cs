using System.Data;
using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

public static class ContractSvc
{
    static readonly string[] ClientsT2 = { "PT Mayora Nusantara", "CV Tekstil Jaya Abadi", "Pabrik Roti Sari Rasa", "PT Furnitur Jepara Mandiri" };
    static readonly string[] ClientsT3 = { "PT Baja Nusantara Perkasa", "BUMN Pupuk Indonesia", "PT Tambang Borneo Raya", "Terminal Kontainer Tanjung Perak", "PT Siba Surya Logistik" };

    public static List<Contract> Open(int pid)
    {
        using var c = Db.Open();
        return c.Query<Contract>("SELECT * FROM contracts WHERE player_id=@pid AND status IN ('OPEN','ACTIVE') ORDER BY kind,id", new { pid }).ToList();
    }

    public static void Refresh(int pid, int day, string[]? only = null, int count = -1)
    {
        var p = PlayerSvc.Get(pid); var R = Rules.Rng; using var c = Db.Open();
        c.Execute("UPDATE contracts SET status='EXPIRED' WHERE player_id=@pid AND kind='SPOT' AND status='OPEN' AND expire_day<@day", new { pid, day });
        int open = c.ExecuteScalar<int>("SELECT COUNT(*) FROM contracts WHERE player_id=@pid AND kind='SPOT' AND status='OPEN'", new { pid });
        int need = count >= 0 ? count : Math.Max(0, 8 - open);
        var cargos = c.Query<CargoType>("SELECT * FROM cargo_types WHERE min_tier<=@t", new { t = p.Tier }).ToList();
        if (only != null) cargos = cargos.Where(x => only.Contains(x.Code)).ToList();
        var ev = WorldSvc.Active(pid, day);
        for (int i = 0; i < need && cargos.Count > 0; i++) Make(p, c, cargos[R.Next(cargos.Count)], "SPOT", day, ev);
        if (p.Tier >= 2 && only == null)
        {
            int offers = c.ExecuteScalar<int>("SELECT COUNT(*) FROM contracts WHERE player_id=@pid AND kind='DEDICATED' AND status='OPEN'", new { pid });
            var big = cargos.Where(x => p.Tier == 3 ? x.MinTier == 3 : x.MinTier == 2).ToList();
            for (int i = offers; i < 3 && big.Count > 0; i++) Make(p, c, big[R.Next(big.Count)], "DEDICATED", day, ev);
        }
    }

    static void Make(Player p, IDbConnection c, CargoType cg, string kind, int day, List<WorldEvent> ev)
    {
        var g = RoadGraph.I; var R = Rules.Rng; var nodes = g.Nodes.Values.ToList();
        for (int tr = 0; tr < 50; tr++)
        {
            var a = nodes[R.Next(nodes.Count)]; var b = nodes[R.Next(nodes.Count)];
            if (a.Id == b.Id) continue;
            if (p.Tier < 3 && (a.Island != "Jawa" || b.Island != "Jawa")) continue;
            var path = g.Path(a.Id, b.Id, p.Tier == 3, 1.0);
            if (path == null) continue;
            double road = path.Where(e => e.Kind == "ROAD").Sum(e => (double)e.Km), sea = path.Where(e => e.Kind == "FERRY").Sum(e => (double)e.Km);
            if (p.Tier == 1 && road > 150) continue;
            if (p.Tier == 2 && road > 1000) continue;
            if (p.Tier == 3 && road + sea < 150) continue;
            int kg = R.Next(cg.MinKg, cg.MaxKg + 1) / 10 * 10;
            double em = WorldSvc.RateMult(ev, cg.Code);
            long ferryExtra = (long)(path.Where(e => e.Kind == "FERRY").Sum(e => (double)e.FerryFee) * 1.3);
            long fee = (long)((road + sea * 0.5) * (kg / 1000.0) * cg.Rate * em * (0.9 + R.NextDouble() * 0.2)) + ferryExtra;
            fee = Math.Max(150_000, fee / 1000 * 1000);
            if (kind == "SPOT")
            {
                int rep = R.NextDouble() < 0.7 ? 0 : R.Next(1, 4) * 40;
                c.Execute(@"INSERT INTO contracts(player_id,kind,client,origin,dest,cargo_code,weight_kg,fee,rep_required,status,created_day,expire_day)
                    VALUES(@pid,'SPOT','Spot Charter',@o,@d,@cg,@kg,@fee,@rep,'OPEN',@day,@exp)",
                    new { pid = p.Id, o = a.Name, d = b.Name, cg = cg.Code, kg, fee, rep, day, exp = day + 6 });
            }
            else
            {
                fee = (long)(fee * 0.85);                      // borongan: margin sedang, cashflow pasti
                int trips = R.Next(2, 7), months = R.Next(1, 13);
                int rep = p.Tier == 2 ? R.Next(60, 201) : R.Next(150, 451);
                string client = (p.Tier == 2 ? ClientsT2 : ClientsT3)[R.Next((p.Tier == 2 ? ClientsT2 : ClientsT3).Length)];
                c.Execute(@"INSERT INTO contracts(player_id,kind,client,origin,dest,cargo_code,weight_kg,fee,rep_required,status,created_day,expire_day,months_total,trips_per_month,monthly_pay)
                    VALUES(@pid,'DEDICATED',@client,@o,@d,@cg,@kg,@fee,@rep,'OPEN',@day,@exp,@months,@trips,@mp)",
                    new { pid = p.Id, client, o = a.Name, d = b.Name, cg = cg.Code, kg, fee, rep, day, exp = day + 30, months, trips, mp = fee * trips });
            }
            return;
        }
    }

    public static string Accept(int pid, int id)
    {
        var p = PlayerSvc.Get(pid); using var c = Db.Open();
        var k = c.QueryFirstOrDefault<Contract>("SELECT * FROM contracts WHERE id=@id AND player_id=@pid AND kind='DEDICATED' AND status='OPEN'", new { id, pid });
        if (k == null) return "Kontrak tidak ada.";
        if (p.Reputation < k.RepRequired) return $"Reputasi kurang! Butuh {k.RepRequired}, kamu {p.Reputation}. Uang top-up tidak bisa membeli rating.";
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM contracts WHERE player_id=@pid AND kind='DEDICATED' AND status='ACTIVE'", new { pid }) >= 4) return "Maksimal 4 kontrak borongan aktif.";
        c.Execute("UPDATE contracts SET status='ACTIVE' WHERE id=@id", new { id });
        return $"Kontrak {k.Client} diterima: {k.TripsPerMonth} trip/bulan selama {k.MonthsTotal} bulan, bayar {Rules.Rp(k.MonthlyPay)}/bulan.";
    }
}