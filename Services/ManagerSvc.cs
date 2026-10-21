using Nusantara.Models;

namespace Nusantara.Services;

// Late game: Manajer Operasional mengurus servis & pemberangkatan sesuai rule pemain
public static class ManagerSvc
{
    public static List<string> Run(int pid)
    {
        var log = new List<string>(); var p = PlayerSvc.Get(pid);

        foreach (var t in FleetSvc.Fleet(pid).Where(x => x.Status == "IDLE"))
            foreach (var k in FleetSvc.Comps.Where(k => FleetSvc.Pct(t, k) < p.MgrServiceTh))
                log.Add("[Manajer] " + FleetSvc.Service(pid, t.Id, k));

        p = PlayerSvc.Get(pid);
        var contracts = ContractSvc.Open(pid);
        foreach (var t in FleetSvc.Fleet(pid).Where(x => x.Status == "IDLE" && x.DriverId != null))
        {
            Plan? best = null; double bestProfit = double.MinValue;
            foreach (var ct in contracts.Where(k => k.Status == "OPEN" || (k.Status == "ACTIVE" && k.TripsDoneMonth < k.TripsPerMonth)))
            {
                var pl = TripSvc.Build(p, t.Id, ct.Id, p.MgrUjMult, 0, "DENDA", "AUTO", "ANTRE");
                if (pl.Error != null) continue;
                long fee = ct.Kind == "SPOT" ? ct.Fee : ct.MonthlyPay / Math.Max(1, ct.TripsPerMonth);
                double profit = fee - pl.UjGiven - pl.Escort - pl.Security;
                if (ct.Kind == "SPOT" && (fee <= 0 || profit / fee < p.MgrMinMargin)) continue;
                if (ct.Kind == "DEDICATED") profit += 1_000_000_000;   // kewajiban borongan diprioritaskan
                if (profit > bestProfit) { best = pl; bestProfit = profit; }
            }
            if (best != null)
            {
                log.Add("[Manajer] " + TripSvc.Dispatch(p, best));
                contracts = ContractSvc.Open(pid); p = PlayerSvc.Get(pid);
            }
        }
        return log;
    }
}