using Nusantara.Models;

namespace Nusantara.Services;

public class Feasibility
{
    public bool Ok; public List<string> Reasons = new(); public Plan? Best; public int FitTrucks;
}

// Kelayakan memakai TripSvc.Build yang SAMA dengan dispatch, jadi angka preview = angka berangkat.
public static class ContractPreview
{
    public static Feasibility Evaluate(Player p, Contract k)
    {
        var f = new Feasibility();
        if (p.Reputation < k.RepRequired)
        {
            f.Reasons.Add($"Reputasi kurang (butuh {k.RepRequired}, punya {p.Reputation}).");
            return f;
        }

        // Borongan yang belum diterima: cek kemampuan armada (belum bisa dihitung per trip)
        if (k.Kind == "DEDICATED" && k.Status == "OPEN")
        {
            var warns = ContractSvc.FleetCheck(p.Id, k).Where(x => x.StartsWith("PERINGATAN")).ToList();
            f.Reasons.AddRange(warns);
            f.Ok = warns.Count == 0;
            return f;
        }

        var idle = FleetSvc.Fleet(p.Id).Where(t => t.Status == "IDLE").ToList();
        if (idle.Count == 0) { f.Reasons.Add("Tidak ada truk berstatus IDLE."); return f; }

        var errs = new List<string>();
        foreach (var t in idle)
        {
            if (t.DriverId == null) { errs.Add($"Truk [{t.Id}] belum punya supir."); continue; }
            // Asumsi preview: UJ standar, tanpa overload, keamanan otomatis, tanpa kejadian acak
            var pl = TripSvc.Build(p, t.Id, k.Id, 1.0, 0, "DENDA", "AUTO", "ANTRE");
            if (pl.Error != null) { errs.Add($"Truk [{t.Id}]: {pl.Error}"); continue; }
            f.FitTrucks++;
            if (f.Best == null || pl.ContribEst > f.Best.ContribEst) f.Best = pl;
        }
        f.Ok = f.Best != null;
        if (!f.Ok) f.Reasons.AddRange(errs.Distinct());
        return f;
    }
}