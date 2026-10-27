using Nusantara.Models;

namespace Nusantara.Services;

// Strategi berbasis aturan (heuristik lokal). Ini strategi bawaan DAN cadangan kalau provider lain gagal.
// Hanya MENGUSULKAN tindakan; AiActionValidator yang memutuskan boleh atau tidak.
public sealed class RuleBasedStrategy : IStrategyProvider
{
    public string Name => "RuleBased";

    static readonly string[] Usable = { "IDLE", "ON_TRIP", "KAROSERI" };

    public IReadOnlyList<AiProposal> Propose(AiSnapshot s)
    {
        var prof = AiProfiles.Get(s.Personality);
        var list = new List<AiProposal>();
        bool saving = s.ShortGoal == AiGoals.RaiseCash;

        Maintenance(s, list);
        Dispatches(s, list);
        Staffing(s, list);
        Bodies(s, list);
        Bids(s, prof, list);
        if (!saving) Purchases(s, prof, list);
        return list;
    }

    // ---------- helper ----------

    static bool BodyFits(AiSnapshot s, AiTruck t, string cargo) =>
        t.Body != null && s.CargoBodies.TryGetValue(cargo, out var bodies) && bodies.Contains(t.Body);

    static bool Fits(AiSnapshot s, AiTruck t, WorldContract wc) =>
        Usable.Contains(t.Status) && BodyFits(s, t, wc.CargoCode) && t.Payload >= wc.WeightKg;

    // Syarat yang bisa dicek dari potret saja (validator tetap memeriksa ulang di data asli).
    static bool Eligible(AiSnapshot s, WorldContract wc) =>
        wc.MinTier <= s.Tier && s.Reputation >= wc.RepRequired && (wc.Scope != "INTERNATIONAL" || s.Tier >= MarketConfig.InternationalTier);

    static long EstCost(WorldContract wc) => (long)(EconomySvc.AvgUj(wc.VehicleClass) * AiRules.ProtocolCostFactor);

    // ---------- 1. perawatan ----------
    static void Maintenance(AiSnapshot s, List<AiProposal> list)
    {
        foreach (var t in s.Trucks.Where(x => x.Status == "IDLE" && x.MinCondition < 40))
            list.Add(new AiProposal(s.CompanyId, AiActions.Service, 90, $"Komponen truk #{t.Id} tinggal {t.MinCondition}%, servis sebelum rusak di jalan.",
                "Mengurangi risiko rem blong dan mogok", 0, A: t.Id));
    }

    // ---------- 2. berangkatkan kontrak yang sudah dimenangkan ----------
    static void Dispatches(AiSnapshot s, List<AiProposal> list)
    {
        var used = new HashSet<int>();
        foreach (var ct in s.MyContracts.Where(k => k.Status == "OPEN" || (k.Status == "ACTIVE" && k.TripsDoneMonth < k.TripsPerMonth))
                                         .OrderByDescending(k => k.Fee))
        {
            var truck = s.Trucks.FirstOrDefault(t => t.Status == "IDLE" && t.HasDriver && !used.Contains(t.Id)
                                                     && BodyFits(s, t, ct.CargoCode) && t.Payload >= ct.WeightKg);
            if (truck == null) continue;
            used.Add(truck.Id);
            list.Add(new AiProposal(s.CompanyId, AiActions.Dispatch, 100, $"Kontrak {ct.Origin} - {ct.Dest} menunggu, truk #{truck.Id} siap dan bersupir.",
                $"Pendapatan trip sekitar {Rules.Rp(ct.Fee)}", 0, A: ct.Id, B: truck.Id));
        }
    }

    // ---------- 3. supir ----------
    static void Staffing(AiSnapshot s, List<AiProposal> list)
    {
        var assigned = s.Trucks.Where(t => t.DriverId != null).Select(t => t.DriverId!.Value).ToHashSet();
        var freeDrivers = s.Staff.Where(d => d.Status == "IDLE" && !assigned.Contains(d.Id)).ToList();
        var pool = s.Candidates.ToList();
        bool needMoreCandidates = false;

        foreach (var t in s.Trucks.Where(x => !x.HasDriver && (x.Status == "IDLE" || x.Status == "KAROSERI")))
        {
            int need = Rules.SimRank(t.Sim);
            var free = freeDrivers.FirstOrDefault(d => Rules.SimRank(d.Sim) >= need);
            if (free != null)
            {
                freeDrivers.Remove(free);
                list.Add(new AiProposal(s.CompanyId, AiActions.Assign, 79, $"Truk #{t.Id} belum punya supir; {free.Id} menganggur dan ber-SIM cukup.",
                    "Truk bisa jalan", 0, A: t.Id, B: free.Id));
                continue;
            }
            var cand = pool.Where(d => Rules.SimRank(d.Sim) >= need).OrderByDescending(d => d.Quality * 1_000_000.0 / Math.Max(1, d.Salary)).FirstOrDefault();
            if (cand != null)
            {
                pool.Remove(cand);
                list.Add(new AiProposal(s.CompanyId, AiActions.Hire, 80, $"Truk #{t.Id} butuh supir ber-SIM minimal {t.Sim}; kandidat terbaik per rupiah gaji.",
                    $"Gaji pokok {Rules.Rp(cand.Salary)}/bulan", 0, A: cand.Id, B: 100));
                list.Add(new AiProposal(s.CompanyId, AiActions.Assign, 79, $"Tugaskan supir baru ke truk #{t.Id}.", "Truk bisa jalan", 0, A: t.Id, B: cand.Id));
            }
            else needMoreCandidates = true;
        }

        if (needMoreCandidates && s.Cash >= 1_000_000)
            list.Add(new AiProposal(s.CompanyId, AiActions.RefreshCandidates, 60, "Tidak ada kandidat supir yang cocok; pasang lowongan baru.",
                "Enam kandidat baru", 250_000));
    }

    // ---------- 4. karoseri (truk tier 2+ datang tanpa bak) ----------
    static void Bodies(AiSnapshot s, List<AiProposal> list)
    {
        var cargos = s.Market.Select(w => w.CargoCode).Concat(s.MyContracts.Select(k => k.CargoCode)).ToList();
        foreach (var t in s.Trucks.Where(x => x.Status == "WAIT_BODY"))
        {
            var best = s.Bodies.Where(b => b.WeightKg < t.MaxLoad)
                .Select(b => (body: b, score: cargos.Count(cg => s.CargoBodies.TryGetValue(cg, out var bs) && bs.Contains(b.Code))))
                .OrderByDescending(x => x.score).ThenBy(x => x.body.Price).FirstOrDefault();
            if (best.body == null) continue;
            long cost = (long)(best.body.Price * (1 + t.MaxLoad / 20000.0));
            list.Add(new AiProposal(s.CompanyId, AiActions.InstallBody, 85, $"Truk #{t.Id} belum punya bak; {best.body.Name} cocok untuk {best.score} kargo yang sedang beredar.",
                "Truk bisa dipakai setelah bak selesai", cost, A: t.Id, Code: best.body.Code));
        }
    }

    // ---------- 5. penawaran ----------

    // Harga tawaran, atau null kalau tidak menguntungkan. Publik supaya mudah diuji.
    public static long? BidPrice(AiSnapshot s, AiProfile prof, WorldContract wc, out string reason)
    {
        int pressure = Math.Min(3, s.Rivals.Count(r => r.Reputation > s.Reputation + 20));
        bool saving = s.ShortGoal == AiGoals.RaiseCash;
        double factor = prof.PriceFactor - 0.02 * pressure - (saving ? 0.03 : 0.0);

        long cost = EstCost(wc);
        long floor = (long)(cost * (1 + prof.MinMargin));
        long price = Math.Max(floor, (long)(wc.Fee * factor)) / 1000 * 1000;
        long lo = (long)(wc.Fee * MarketConfig.MinPriceFactor), hi = (long)(wc.Fee * MarketConfig.MaxPriceFactor);
        price = Math.Clamp(price, lo, hi);

        if (price < floor) { reason = $"Harga {Rules.Rp(price)} di bawah batas margin {Rules.Rp(floor)}."; return null; }
        reason = $"Biaya trip sekitar {Rules.Rp(cost)}, margin minimal {prof.MinMargin:P0} ({prof.Code}); " +
                 $"{pressure} pesaing lebih kuat" + (saving ? "; sedang butuh kas, harga ditekan." : ".");
        return price;
    }

    static void Bids(AiSnapshot s, AiProfile prof, List<AiProposal> list)
    {
        var offers = new List<(WorldContract wc, long price, string reason)>();
        foreach (var wc in s.Market)
        {
            if (!Eligible(s, wc)) continue;
            if (prof.PremiumOnly && wc.RepRequired == 0 && wc.Fee < 3_000_000) continue;
            if (!s.Trucks.Any(t => Fits(s, t, wc))) continue;
            var price = BidPrice(s, prof, wc, out var why);
            if (price != null) offers.Add((wc, price.Value, why));
        }
        foreach (var o in offers.OrderByDescending(o => o.price - EstCost(o.wc)).Take(prof.MaxBidsPerDay))
            list.Add(new AiProposal(s.CompanyId, AiActions.Bid, 50, $"{o.wc.Origin} - {o.wc.Dest}: {o.reason}",
                $"Laba kotor sekitar {Rules.Rp(o.price - EstCost(o.wc))} kalau menang", 0, A: o.wc.Id, Price: o.price));
    }

    // ---------- 6. pembelian, perluasan, tier ----------
    static void Purchases(AiSnapshot s, AiProfile prof, List<AiProposal> list)
    {
        bool setupPending = s.Trucks.Any(t => !t.HasDriver || t.Status == "WAIT_BODY" || t.Status == "KAROSERI");
        int maxTrucks = FleetSvc.MaxTrucks(s.Tier);

        if (!setupPending && s.Trucks.Count < maxTrucks)
        {
            var unserved = s.Market.Where(w => Eligible(s, w) && !s.Trucks.Any(t => Fits(s, t, w))
                                               && BidPrice(s, prof, w, out _) != null).ToList();
            bool expanding = s.LongGoal == AiGoals.ExpandFleet && s.Trucks.Count < prof.FleetTarget * s.Tier;
            bool need = s.Trucks.Count == 0 || unserved.Count > 0 || expanding;

            if (need)
            {
                var affordable = s.Models.Where(m => m.Price <= s.Cash - prof.CashReserve).OrderBy(m => m.Price).ToList();
                TruckModel? pick = null;
                if (s.Trucks.Count == 0) pick = affordable.FirstOrDefault(m => m.FactoryBody != null) ?? affordable.FirstOrDefault();
                else if (unserved.Count > 0)
                {
                    var kgs = unserved.Select(w => w.WeightKg).OrderBy(x => x).ToList();
                    int median = kgs[kgs.Count / 2];
                    pick = affordable.FirstOrDefault(m => m.MaxLoad - 700 >= median) ?? affordable.LastOrDefault();
                }
                else pick = affordable.FirstOrDefault();

                if (pick != null)
                    list.Add(new AiProposal(s.CompanyId, AiActions.BuyTruck, 70,
                        s.Trucks.Count == 0 ? "Belum punya truk sama sekali." : unserved.Count > 0 ? $"{unserved.Count} kontrak menguntungkan belum bisa dilayani armada sekarang." : "Tujuan jangka panjang memperluas armada.",
                        $"Kapasitas bertambah; sisa kas {Rules.Rp(s.Cash - pick.Price)}", pick.Price, A: pick.Id));
            }
        }

        if (s.LongGoal == AiGoals.UpgradeTier && s.Tier < 3 && !setupPending)
        {
            long cost = AiRules.TierUpgradeCost(s.Tier);
            if (s.Cash >= cost + prof.CashReserve)
            {
                if (s.Tier == 2 && s.PoolType == "NONE")
                    list.Add(new AiProposal(s.CompanyId, AiActions.RentPool, 71, "Naik tier ke PT butuh pool; sewa dulu.", "Syarat tier terpenuhi", 0));
                else
                    list.Add(new AiProposal(s.CompanyId, AiActions.UpgradeTier, 72, "Kas cukup dan tujuan jangka panjang naik tier.", "Truk, rute, dan kontrak lebih besar terbuka", cost));
            }
        }
    }
}