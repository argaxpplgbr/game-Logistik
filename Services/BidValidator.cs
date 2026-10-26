using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

// Satu-satunya pemeriksa syarat penawaran. Dipakai SAMA untuk pemain dan perusahaan AI,
// baik saat menawar maupun saat pemenang ditetapkan.
public static class BidValidator
{
    static readonly string[] UsableStatus = { "IDLE", "ON_TRIP", "KAROSERI" };

    // Truk milik perusahaan yang bak-nya cocok dan muatnya cukup untuk kontrak ini.
    public static List<Truck> FitTrucks(int companyId, WorldContract wc)
    {
        using var c = Db.Open();
        var cg = c.QueryFirstOrDefault<CargoType>("SELECT * FROM cargo_types WHERE code=@x", new { x = wc.CargoCode });
        if (cg == null) return new List<Truck>();
        var bodies = cg.Bodies.Split(',');
        return FleetSvc.Fleet(companyId)
            .Where(t => UsableStatus.Contains(t.Status) && bodies.Contains(t.BodyCode ?? "") && t.Payload >= wc.WeightKg)
            .ToList();
    }

    // null = penawaran sah. Selain itu: alasan penolakan yang bisa dibaca pemain.
    public static string? Check(int companyId, WorldContract wc, long price, int day)
    {
        Player p;
        try { p = PlayerSvc.Get(companyId); }
        catch (InvalidOperationException) { return "Perusahaan tidak ditemukan."; }

        if (p.Bankrupt) return "Perusahaan sudah bangkrut.";
        if (wc.Status != "OPEN") return "Kontrak sudah tidak tersedia (sudah diberikan, kedaluwarsa, atau dibatalkan).";
        if (day >= wc.ExpireDay) return "Kontrak sudah kedaluwarsa.";

        if (wc.Scope == "INTERNATIONAL" && p.Tier < MarketConfig.InternationalTier)
            return "Layanan internasional hanya lewat kontrak internasional dan butuh tier Internasional.";
        if (p.Tier < wc.MinTier)
            return $"Butuh tier {MarketConfig.TierName(wc.MinTier)}; perusahaan ini tier {MarketConfig.TierName(p.Tier)}.";
        if (p.Reputation < wc.RepRequired)
            return $"Reputasi kurang (butuh {wc.RepRequired}, punya {p.Reputation}).";

        long lo = (long)(wc.Fee * MarketConfig.MinPriceFactor), hi = (long)(wc.Fee * MarketConfig.MaxPriceFactor);
        if (price < lo || price > hi) return $"Harga harus antara {Rules.Rp(lo)} dan {Rules.Rp(hi)}.";

        using var c = Db.Open();
        var cg = c.QueryFirstOrDefault<CargoType>("SELECT * FROM cargo_types WHERE code=@x", new { x = wc.CargoCode });
        if (cg == null) return "Jenis kargo tidak dikenal.";
        if (cg.B3 && !p.B3Cert) return "Kargo B3 butuh sertifikasi B3.";

        var fit = FitTrucks(companyId, wc);
        if (fit.Count == 0)
            return $"Belum punya truk dengan bak {cg.Bodies.Replace(",", "/")} yang muat {wc.WeightKg:N0} kg.";

        var staff = HrSvc.Staff(companyId);
        if (!fit.Any(t => staff.Any(d => Rules.SimRank(d.Sim) >= Rules.SimRank(t.Sim))))
            return "Belum ada supir ber-SIM cukup untuk truk yang cocok.";

        long uj = EconomySvc.AvgUj(wc.VehicleClass);
        if (p.Cash < uj) return $"Dana belum cukup untuk uang jalan trip pertama ({Rules.Rp(uj)}).";

        // Kapasitas: kontrak pelaksanaan yang belum selesai + penawaran lain yang masih menunggu
        // tidak boleh melebihi jumlah truk yang cocok.
        int committed = c.ExecuteScalar<int>(@"SELECT
              (SELECT COUNT(*) FROM contracts WHERE player_id=@id AND world_contract_id IS NOT NULL AND status IN ('OPEN','ACTIVE'))
            + (SELECT COUNT(*) FROM contract_bids WHERE company_id=@id AND status='PENDING' AND world_contract_id<>@wc)",
            new { id = companyId, wc = wc.Id });
        if (committed >= fit.Count)
            return $"Kapasitas penuh: {committed} komitmen untuk {fit.Count} truk yang cocok.";

        return null;
    }
}