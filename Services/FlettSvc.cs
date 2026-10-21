using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

public static class FleetSvc
{
    const string Sel = @"SELECT t.*, m.name model_name, m.tier, m.km_per_l, m.max_load, m.axles, m.sim, m.fuel_req, m.is_euro,
        COALESCE(b.weight_kg,0) body_weight FROM trucks t JOIN truck_catalog m ON m.id=t.catalog_id
        LEFT JOIN bodies b ON b.code=t.body_code";

    public static readonly string[] Comps = { "OIL", "BRAKE", "TIRE", "FILTER", "ENGINE" };
    public static int MaxTrucks(int tier) => tier == 1 ? 4 : tier == 2 ? 12 : 40;
    public static int Pct(Truck t, string k) => k switch { "OIL" => t.OilPct, "BRAKE" => t.BrakePct, "TIRE" => t.TirePct, "FILTER" => t.FilterPct, _ => t.EnginePct };
    static string Col(string k) => k.ToLower() + "_pct";

    public static List<Truck> Fleet(int pid)
    {
        using var c = Db.Open();
        return c.Query<Truck>(Sel + " WHERE t.player_id=@pid AND t.status<>'TOTALLOSS' ORDER BY t.id", new { pid }).ToList();
    }
    public static Truck? One(int id)
    {
        using var c = Db.Open();
        return c.QueryFirstOrDefault<Truck>(Sel + " WHERE t.id=@id", new { id });
    }
    public static List<TruckModel> Models(int tier)
    {
        using var c = Db.Open();
        return c.Query<TruckModel>("SELECT * FROM truck_catalog WHERE tier<=@tier ORDER BY id", new { tier }).ToList();
    }
    public static List<BodyType> Bodies()
    {
        using var c = Db.Open();
        return c.Query<BodyType>("SELECT * FROM bodies WHERE price>0").ToList();
    }

    public static string BuyNew(int pid, int modelId)
    {
        var p = PlayerSvc.Get(pid); using var c = Db.Open();
        var m = c.QueryFirstOrDefault<TruckModel>("SELECT * FROM truck_catalog WHERE id=@modelId", new { modelId });
        if (m == null) return "Model tidak ada.";
        if (m.Tier > p.Tier) return $"Butuh status {(m.Tier == 2 ? "CV" : "PT")}.";
        if (Fleet(pid).Count >= MaxTrucks(p.Tier)) return "Garasi/pool penuh.";
        if (p.Cash < m.Price) return "Uang kurang.";
        string status = m.FactoryBody == null ? "WAIT_BODY" : "IDLE";
        c.Execute("UPDATE players SET cash=cash-@price WHERE id=@pid", new { price = m.Price, pid });
        c.Execute(@"INSERT INTO trucks(player_id,catalog_id,body_code,status,fuel_pref,purchase_price,bought_day)
            VALUES(@pid,@modelId,@body,@status,@fuel,@price,@day)",
            new { pid, modelId, body = m.FactoryBody, status, fuel = m.FuelReq == "DEX" ? "DEX" : "BIO", price = m.Price, day = PlayerSvc.Day(p) });
        return m.FactoryBody == null ? $"{m.Name} dibeli (chassis+kabin). Bawa ke Karoseri untuk pasang bak!" : $"{m.Name} dibeli & siap jalan (butuh supir).";
    }

    public static string Karoseri(int pid, int truckId, string code)
    {
        var p = PlayerSvc.Get(pid); var t = One(truckId); using var c = Db.Open();
        var b = c.QueryFirstOrDefault<BodyType>("SELECT * FROM bodies WHERE code=@code AND price>0", new { code });
        if (t == null || t.PlayerId != pid || b == null) return "Truk/bak tidak valid.";
        if (t.Status != "WAIT_BODY") return "Truk ini tidak sedang menunggu bak.";
        if (b.WeightKg >= t.MaxLoad) return "Bak terlalu berat untuk chassis ini.";
        long cost = (long)(b.Price * (1 + t.MaxLoad / 20000.0));
        if (p.Cash < cost) return $"Uang kurang ({Rules.Rp(cost)}).";
        var ready = DateTime.UtcNow.AddMinutes(b.Days * Rules.DayMinutes);
        c.Execute("UPDATE players SET cash=cash-@cost WHERE id=@pid", new { cost, pid });
        c.Execute("UPDATE trucks SET status='KAROSERI',body_code=@code,body_ready_at=@ready WHERE id=@truckId", new { code, ready, truckId });
        return $"{b.Name} dipasang ke truk {truckId}: {Rules.Rp(cost)}, selesai {b.Days} hari game.";
    }

    public static void FinishBodies(int pid)
    {
        using var c = Db.Open();
        c.Execute("UPDATE trucks SET status='IDLE',body_ready_at=NULL WHERE player_id=@pid AND status='KAROSERI' AND body_ready_at<=@now", new { pid, now = DateTime.UtcNow });
    }

    // ---- Pasar truk bekas ----
    public static void RefreshUsed(int pid)
    {
        var R = Rules.Rng; using var c = Db.Open();
        c.Execute("DELETE FROM used_listings WHERE player_id=@pid", new { pid });
        var models = c.Query<TruckModel>("SELECT * FROM truck_catalog").ToList();
        for (int i = 0; i < 6; i++)
        {
            var m = models[R.Next(models.Count)];
            string body = m.FactoryBody ?? (m.Tier == 2 ? new[] { "BOXBESI", "WINGSET", "BAKKAYU" }[R.Next(3)]
                : m.Id == 10 ? "DUMP" : new[] { "FLATBED", "DUMP", "TANKI", "BAKKAYU" }[R.Next(4)]);
            long price = (long)(m.Price * (0.5 + R.NextDouble() * 0.12));
            c.Execute(@"INSERT INTO used_listings(player_id,catalog_id,body_code,price,age_days,oil_pct,brake_pct,tire_pct,filter_pct,engine_pct)
                VALUES(@pid,@cid,@body,@price,@age,@o,@b,@t,@f,@e)",
                new { pid, cid = m.Id, body, price, age = R.Next(200, 1500), o = R.Next(15, 66), b = R.Next(10, 61), t = R.Next(15, 66), f = R.Next(15, 66), e = R.Next(25, 71) });
        }
    }

    public static List<UsedListing> Used(int pid)
    {
        using var c = Db.Open();
        return c.Query<UsedListing>(@"SELECT u.*, m.name model_name, m.tier, m.fuel_req FROM used_listings u
            JOIN truck_catalog m ON m.id=u.catalog_id WHERE u.player_id=@pid", new { pid }).ToList();
    }

    public static string BuyUsed(int pid, int id)
    {
        var p = PlayerSvc.Get(pid); using var c = Db.Open();
        var l = Used(pid).FirstOrDefault(x => x.Id == id);
        if (l == null) return "Listing tidak ada.";
        if (l.Tier > p.Tier) return "Status usaha belum cukup untuk truk ini.";
        if (Fleet(pid).Count >= MaxTrucks(p.Tier)) return "Garasi/pool penuh.";
        if (p.Cash < l.Price) return "Uang kurang.";
        c.Execute("UPDATE players SET cash=cash-@price WHERE id=@pid", new { price = l.Price, pid });
        c.Execute(@"INSERT INTO trucks(player_id,catalog_id,body_code,status,oil_pct,brake_pct,tire_pct,filter_pct,engine_pct,fuel_pref,purchase_price,bought_day,is_used)
            VALUES(@pid,@cid,@body,'IDLE',@o,@b,@t,@f,@e,@fuel,@price,@day,1)",
            new { pid, cid = l.CatalogId, body = l.BodyCode, o = l.OilPct, b = l.BrakePct, t = l.TirePct, f = l.FilterPct, e = l.EnginePct, fuel = l.FuelReq == "DEX" ? "DEX" : "BIO", price = l.Price, day = PlayerSvc.Day(p) });
        c.Execute("DELETE FROM used_listings WHERE id=@id", new { id });
        return $"{l.ModelName} bekas dibeli. Komponen sudah aus, cek & servis dulu!";
    }

    // ---- Maintenance ----
    public static string Service(int pid, int truckId, string comp)
    {
        var p = PlayerSvc.Get(pid); var t = One(truckId);
        if (t == null || t.PlayerId != pid) return "Truk tidak ditemukan.";
        if (t.Status != "IDLE") return "Truk harus berstatus IDLE untuk servis.";
        var list = comp == "ALL" ? Comps.Where(k => Pct(t, k) < 60).ToArray() : new[] { comp };
        if (list.Length == 0) return "Semua komponen masih di atas 60%.";
        bool mech = p.PoolType != "NONE" && p.Mechanics > 0;
        double lab = mech ? 0.7 : 2.0;               // mekanik tetap hemat, mekanik panggilan mahal
        if (p.Warehouse) lab *= 0.85;
        if (p.AdDiscount) lab *= 0.85;
        long cost = (long)list.Sum(k => Rules.PartPrice(k, t.Tier) * lab);
        if (p.Cash < cost) return $"Uang kurang ({Rules.Rp(cost)}).";
        using var c = Db.Open();
        c.Execute($"UPDATE trucks SET {string.Join(",", list.Select(k => Col(k) + "=100"))} WHERE id=@id", new { id = t.Id });
        c.Execute("UPDATE players SET cash=cash-@cost,ad_discount=0 WHERE id=@pid", new { cost, pid });
        return $"Servis {string.Join("+", list)} {t.ModelName}: {Rules.Rp(cost)} ({(mech ? "mekanik tetap" : "mekanik panggilan")}).";
    }

    public static string Assign(int pid, int truckId, int driverId)
    {
        using var c = Db.Open(); var t = One(truckId);
        var d = c.QueryFirstOrDefault<Driver>("SELECT * FROM drivers WHERE id=@driverId AND player_id=@pid AND status='IDLE'", new { driverId, pid });
        if (t == null || t.PlayerId != pid || d == null) return "Data tidak valid / supir sedang jalan.";
        if (t.Status == "ON_TRIP") return "Truk sedang jalan.";
        if (Rules.SimRank(d.Sim) < Rules.SimRank(t.Sim)) return $"SIM {d.Sim} tidak cukup (butuh {t.Sim}).";
        c.Execute("UPDATE trucks SET driver_id=NULL WHERE driver_id=@driverId", new { driverId });
        c.Execute("UPDATE trucks SET driver_id=@driverId WHERE id=@truckId", new { driverId, truckId });
        return $"{d.Name} ditugaskan ke truk {truckId}.";
    }

    public static string SetFuel(int pid, int truckId, string fuel)
    {
        using var c = Db.Open();
        int n = c.Execute("UPDATE trucks SET fuel_pref=@fuel WHERE id=@truckId AND player_id=@pid", new { fuel, truckId, pid });
        return n > 0 ? $"BBM truk {truckId}: {fuel}. (Truk Eropa wajib Dexlite!)" : "Truk tidak ditemukan.";
    }

    public static string SetInsurance(int pid, int truckId, string type, bool cargo)
    {
        using var c = Db.Open();
        int n = c.Execute("UPDATE trucks SET insurance=@type,cargo_ins=@cargo WHERE id=@truckId AND player_id=@pid", new { type, cargo, truckId, pid });
        return n > 0 ? $"Asuransi truk {truckId}: {type}{(cargo ? " + Cargo" : "")}." : "Truk tidak ditemukan.";
    }
}