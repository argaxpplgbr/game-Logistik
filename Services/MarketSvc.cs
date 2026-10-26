using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

// Pasar kontrak bersama (Dokumen 02 bagian 4).
//  World Contract = peluang yang dilihat semua perusahaan  (tabel world_contracts)
//  Bid            = penawaran satu perusahaan              (tabel contract_bids)
//  Award          = pemenang + alasan                      (tabel contract_awards, PK = satu pemenang)
//  Execution      = baris di tabel contracts milik pemenang (kolom world_contract_id), dijalankan lewat TripSvc yang sama.
public static class MarketSvc
{
    public const string JobName = "MARKET_DAY";

    static readonly string[] ClientsT2 = { "PT Mayora Nusantara", "CV Tekstil Jaya Abadi", "Pabrik Roti Sari Rasa", "PT Furnitur Jepara Mandiri" };
    static readonly string[] ClientsT3 = { "PT Baja Nusantara Perkasa", "BUMN Pupuk Indonesia", "PT Tambang Borneo Raya", "Terminal Kontainer Tanjung Perak", "PT Siba Surya Logistik" };

    sealed record Ranked(ContractBid Bid, Player Company, BidScore Score);

    // ---------- Baca ----------

    public static WorldContract? Get(int id)
    {
        using var c = Db.Open();
        return c.QueryFirstOrDefault<WorldContract>("SELECT * FROM world_contracts WHERE id=@id", new { id });
    }

    public static List<WorldContract> Open(int day)
    {
        using var c = Db.Open();
        return c.Query<WorldContract>("SELECT * FROM world_contracts WHERE status='OPEN' AND expire_day>@day ORDER BY kind, id", new { day }).ToList();
    }

    public static List<ContractBid> Bids(int worldContractId)
    {
        using var c = Db.Open();
        return c.Query<ContractBid>("SELECT * FROM contract_bids WHERE world_contract_id=@worldContractId ORDER BY id", new { worldContractId }).ToList();
    }

    public static List<ContractBid> MyBids(int companyId, int limit = 10)
    {
        using var c = Db.Open();
        return c.Query<ContractBid>("SELECT * FROM contract_bids WHERE company_id=@companyId ORDER BY id DESC LIMIT @limit", new { companyId, limit }).ToList();
    }

    // ---------- Pembuatan peluang kontrak ----------

    public static int Refresh(int day)
    {
        using var c = Db.Open();
        c.Execute("UPDATE world_contracts SET status='EXPIRED' WHERE status='OPEN' AND expire_day<=@day", new { day });
        c.Execute(@"UPDATE contract_bids SET status='LOST', note='Kontrak kedaluwarsa tanpa pemenang'
            WHERE status='PENDING' AND world_contract_id IN (SELECT id FROM world_contracts WHERE status='EXPIRED')");

        int maxTier = Math.Clamp(c.ExecuteScalar<int?>("SELECT MAX(tier) FROM players WHERE bankrupt=0") ?? 1, 1, 3);
        int spot = c.ExecuteScalar<int>("SELECT COUNT(*) FROM world_contracts WHERE status='OPEN' AND kind='SPOT'");
        int ded = c.ExecuteScalar<int>("SELECT COUNT(*) FROM world_contracts WHERE status='OPEN' AND kind='DEDICATED'");

        int created = 0;
        for (int i = spot; i < MarketConfig.TargetOpenSpot; i++) if (TryCreate(c, "SPOT", day, maxTier)) created++;
        if (maxTier >= 2)
            for (int i = ded; i < MarketConfig.TargetOpenDedicated; i++) if (TryCreate(c, "DEDICATED", day, maxTier)) created++;
        // Kontrak INTERNATIONAL belum dibuat: MarketConfig.InternationalGenerationEnabled = false (belum ada simpul peta luar negeri).
        return created;
    }

    // Meniru logika ContractSvc.Make, tetapi untuk dunia (bukan satu pemain). Tier minimum dihitung dari kargo dan rute.
    static bool TryCreate(IDbConnection c, string kind, int day, int maxTier)
    {
        var g = RoadGraph.I; var R = Rules.Rng; var nodes = g.Nodes.Values.ToList();
        if (nodes.Count < 2) return false;
        var cargos = c.Query<CargoType>("SELECT * FROM cargo_types WHERE min_tier<=@maxTier", new { maxTier }).ToList();
        if (cargos.Count == 0) return false;

        for (int attempt = 0; attempt < 60; attempt++)
        {
            var cg = cargos[R.Next(cargos.Count)];
            var a = nodes[R.Next(nodes.Count)]; var b = nodes[R.Next(nodes.Count)];
            if (a.Id == b.Id) continue;
            var path = g.Path(a.Id, b.Id, true, 1.0);
            if (path == null) continue;

            double road = path.Where(e => e.Kind == "ROAD").Sum(e => (double)e.Km);
            double sea = path.Where(e => e.Kind == "FERRY").Sum(e => (double)e.Km);
            int routeTier = (a.Island != b.Island || sea > 0 || road > 1000) ? 3 : road > 150 ? 2 : 1;
            int minTier = Math.Max(cg.MinTier, routeTier);
            if (minTier > maxTier) continue;
            if (kind == "DEDICATED" && minTier < 2) continue;
            double accept = minTier == 1 ? 1.0 : minTier == 2 ? 0.7 : 0.5;     // tier tinggi lebih jarang
            if (R.NextDouble() > accept) continue;

            int kg = R.Next(cg.MinKg, cg.MaxKg + 1) / 10 * 10;
            string vclass = EconomySvc.ClassForWeight(kg);
            long ferryFeeSum = path.Where(e => e.Kind == "FERRY").Sum(e => e.FerryFee);
            var quote = EconomySvc.MakeQuote(vclass, EconomySvc.BilledKm(road, sea), 1.0, R.NextDouble(), ferryFeeSum, cg.B3);
            long fee = quote.Fee;

            string client; int rep, months = 0, trips = 0, window, life;
            if (kind == "SPOT")
            {
                client = "Spot Charter";
                rep = R.NextDouble() < 0.7 ? 0 : R.Next(1, 4) * 40;
                window = MarketConfig.SpotBidWindowDays; life = MarketConfig.SpotLifetimeDays;
            }
            else
            {
                fee = (long)(fee * EconomySvc.DedicatedDiscount);
                trips = R.Next(2, 7); months = R.Next(1, 13);
                rep = minTier == 2 ? R.Next(60, 201) : R.Next(150, 451);
                var list = minTier == 2 ? ClientsT2 : ClientsT3;
                client = list[R.Next(list.Length)];
                window = MarketConfig.DedicatedBidWindowDays; life = MarketConfig.DedicatedLifetimeDays;
            }

            c.Execute(@"INSERT INTO world_contracts(kind,scope,min_tier,client,origin,dest,cargo_code,weight_kg,fee,rep_required,vehicle_class,
                    quote_km,quote_rate,quote_mult,months_total,trips_per_month,status,created_day,bid_close_day,expire_day)
                VALUES(@kind,'DOMESTIC',@minTier,@client,@o,@d,@cg,@kg,@fee,@rep,@vc,@qkm,@qrate,@qmult,@months,@trips,'OPEN',@day,@close,@exp)",
                new { kind, minTier, client, o = a.Name, d = b.Name, cg = cg.Code, kg, fee, rep, vc = quote.VehicleClass,
                      qkm = quote.BilledKm, qrate = quote.Rate, qmult = quote.Market, months, trips, day, close = day + window, exp = day + life });
            return true;
        }
        return false;
    }

    // ---------- Penawaran ----------

    public static string PlaceBid(int companyId, int worldContractId, long price, int day)
    {
        var wc = Get(worldContractId);
        if (wc == null) return "Kontrak tidak ditemukan.";
        var err = BidValidator.Check(companyId, wc, price, day);
        if (err != null) return "Penawaran ditolak: " + err;

        var p = PlayerSvc.Get(companyId);
        using var c = Db.Open(); using var tx = c.BeginTransaction();
        int n = c.Execute(@"INSERT OR IGNORE INTO contract_bids(world_contract_id,company_id,price,placed_day)
            VALUES(@w,@cid,@price,@day)", new { w = worldContractId, cid = companyId, price, day }, tx);
        if (n == 0) return "Perusahaan ini sudah menawar kontrak tersebut.";
        Events.Append(c, tx, EventTypes.BidPlaced,
            new { company = p.Name, worldContractId, price, origin = wc.Origin, dest = wc.Dest }, companyId, null, false);
        tx.Commit();
        return $"Penawaran {Rules.Rp(price)} untuk {wc.Origin} - {wc.Dest} tercatat. Pemenang ditetapkan hari ke-{wc.BidCloseDay}.";
    }

    // ---------- Penetapan pemenang ----------

    // Mengembalikan pesan yang relevan untuk pemain (hasil penawaran pemain). Hasil AI vs AI ada di event dan log.
    public static List<string> Settle(int day)
    {
        var log = new List<string>();
        List<int> ids;
        using (var c = Db.Open())
            ids = c.Query<int>(@"SELECT id FROM world_contracts WHERE status='OPEN' AND bid_close_day<=@day AND expire_day>@day
                AND EXISTS (SELECT 1 FROM contract_bids b WHERE b.world_contract_id=world_contracts.id AND b.status='PENDING')
                ORDER BY id", new { day }).ToList();

        foreach (var id in ids)
        {
            try { log.AddRange(SettleOne(id, day)); }
            catch (SqliteException ex) { log.Add($"Gagal menetapkan pemenang kontrak #{id}: {ex.Message}"); }
        }
        return log;
    }

    static List<string> SettleOne(int id, int day)
    {
        var msgs = new List<string>();
        var wc = Get(id);
        if (wc == null || wc.Status != "OPEN") return msgs;

        List<ContractBid> pending;
        using (var c0 = Db.Open())
            pending = c0.Query<ContractBid>("SELECT * FROM contract_bids WHERE world_contract_id=@id AND status='PENDING' ORDER BY id", new { id }).ToList();
        if (pending.Count == 0) return msgs;

        int? humanId = CompanySvc.HumanId();
        var rejected = new List<(ContractBid Bid, string Why)>();
        var ranked = new List<Ranked>();
        foreach (var b in pending)
        {
            // Syarat diperiksa ulang saat penetapan: dana, kapasitas, atau tier bisa berubah sejak penawaran.
            var why = BidValidator.Check(b.CompanyId, wc, b.Price, day);
            if (why != null) { rejected.Add((b, why)); continue; }
            var comp = PlayerSvc.Get(b.CompanyId);
            ranked.Add(new Ranked(b, comp, BidScoring.Score(comp, wc, b.Price)));
        }

        // Aturan pemilihan: skor tertinggi; seri -> reputasi lebih tinggi; seri lagi -> ID perusahaan lebih kecil.
        var order = ranked.OrderByDescending(r => r.Score.Total).ThenByDescending(r => r.Company.Reputation).ThenBy(r => r.Company.Id).ToList();

        using var c = Db.Open(); using var tx = c.BeginTransaction();
        foreach (var (bid, why) in rejected)
        {
            c.Execute("UPDATE contract_bids SET status='REJECTED', note=@why WHERE id=@id AND status='PENDING'", new { why, id = bid.Id }, tx);
            if (humanId != null && bid.CompanyId == humanId.Value)
                msgs.Add($"Penawaran kamu untuk {wc.Origin} - {wc.Dest} ditolak saat penetapan: {why}");
        }
        if (order.Count == 0) { tx.Commit(); return msgs; }

        var win = order[0];
        string reason = order.Count == 1
            ? $"Satu-satunya penawar yang memenuhi syarat ({win.Score.Detail})."
            : $"Skor {win.Score.Total:0.0}, unggul {win.Score.Total - order[1].Score.Total:0.0} poin dari {order[1].Company.Name} ({win.Score.Detail}).";

        long price = win.Bid.Price;
        bool ded = wc.Kind == "DEDICATED";
        c.Execute(@"INSERT INTO contracts(player_id,kind,client,origin,dest,cargo_code,weight_kg,fee,rep_required,status,created_day,expire_day,
                months_total,trips_per_month,monthly_pay,vehicle_class,quote_km,quote_rate,quote_mult,world_contract_id)
            VALUES(@pid,@kind,@client,@o,@d,@cg,@kg,@fee,@rep,@status,@day,@exp,@months,@trips,@mp,@vc,@qkm,@qrate,@qmult,@wid)",
            new { pid = win.Company.Id, kind = wc.Kind, client = wc.Client, o = wc.Origin, d = wc.Dest, cg = wc.CargoCode, kg = wc.WeightKg,
                  fee = price, rep = wc.RepRequired, status = ded ? "ACTIVE" : "OPEN", day,
                  exp = day + (ded ? 30 : MarketConfig.SpotLifetimeDays), months = wc.MonthsTotal, trips = wc.TripsPerMonth,
                  mp = ded ? price * wc.TripsPerMonth : 0L, vc = wc.VehicleClass, qkm = wc.QuoteKm, qrate = wc.QuoteRate,
                  qmult = wc.QuoteMult, wid = wc.Id }, tx);
        int execId = c.ExecuteScalar<int>("SELECT last_insert_rowid()", transaction: tx);

        c.Execute(@"INSERT INTO contract_awards(world_contract_id,company_id,bid_id,score,reason,awarded_day,execution_contract_id)
            VALUES(@wid,@cid,@bid,@s,@r,@day,@ex)",
            new { wid = wc.Id, cid = win.Company.Id, bid = win.Bid.Id, s = win.Score.Total, r = reason, day, ex = execId }, tx);
        if (c.Execute("UPDATE world_contracts SET status='AWARDED' WHERE id=@id AND status='OPEN'", new { id = wc.Id }, tx) != 1)
            return msgs;   // kontrak sudah diberikan di tempat lain: batalkan semua (rollback)

        foreach (var r in order)
        {
            bool won = r.Bid.Id == win.Bid.Id;
            c.Execute("UPDATE contract_bids SET status=@st, score=@sc, note=@n WHERE id=@id",
                new { st = won ? "WON" : "LOST", sc = r.Score.Total, n = won ? reason : $"Kalah: skor {r.Score.Total:0.0} vs pemenang {win.Score.Total:0.0}.", id = r.Bid.Id }, tx);
            if (!won && humanId != null && r.Company.Id == humanId.Value)
                msgs.Add($"Penawaran kamu untuk {wc.Origin} - {wc.Dest} kalah dari {win.Company.Name} (skor {r.Score.Total:0.0} vs {win.Score.Total:0.0}).");
        }
        if (humanId != null && win.Company.Id == humanId.Value)
            msgs.Add($"Kamu MENANG kontrak {wc.Client}: {wc.Origin} - {wc.Dest}, harga {Rules.Rp(price)}. {reason}");

        bool involvesHuman = humanId != null && pending.Any(b => b.CompanyId == humanId.Value);
        bool newsworthy = involvesHuman || ded || wc.Fee >= 5_000_000;
        Events.Append(c, tx, EventTypes.ContractAwarded,
            new { company = win.Company.Name, companyId = win.Company.Id, client = wc.Client, origin = wc.Origin, dest = wc.Dest, price,
                  score = win.Score.Total, reason, bidders = order.Count, worldContractId = wc.Id, executionContractId = execId },
            win.Company.Id, null, newsworthy);
        tx.Commit();
        return msgs;
    }

    // ---------- Siklus harian ----------

    // Dipanggil dari GameLoop. Aman dipanggil berulang: tiap hari dunia hanya diproses satu kali (DayJobGuard).
    public static List<string> RunDaily()
    {
        var log = new List<string>();
        if (CompanySvc.HumanId() == null) return log;
        CompanySvc.EnsureAiCompanies(MarketConfig.AiCompanyCount);

        int today = CompanySvc.WorldDay();
        int last;
        using (var c = Db.Open())
            last = c.ExecuteScalar<int?>("SELECT MAX(period) FROM sim_job_log WHERE job=@j", new { j = JobName }) ?? -1;

        for (int d = Math.Max(last + 1, today - MarketConfig.MaxCatchUpDays + 1); d <= today; d++)
        {
            bool claimed;
            using (var c = Db.Open())
            using (var tx = c.BeginTransaction())
            {
                claimed = DayJobGuard.TryClaim(c, tx, JobName, d, today);
                tx.Commit();
            }
            if (!claimed) continue;

            log.AddRange(Settle(d));    // 1) tetapkan pemenang dari penawaran yang sudah ditutup
            Refresh(d);                 // 2) kedaluwarsakan yang lama, buat peluang baru
        }
        return log;
    }
}