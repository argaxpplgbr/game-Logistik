using System.Data;
using Dapper;
using MySqlConnector;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

public static class TripSvc
{
    static Plan Err(string m) => new Plan { Error = m };

    // Sisa jatah trip kontrak borongan bulan ini = target - selesai - yang masih jalan
    public static int SlotsLeft(IDbConnection c, Contract ct, IDbTransaction? tx = null)
    {
        int running = c.ExecuteScalar<int>("SELECT COUNT(*) FROM trips WHERE contract_id=@id AND status='RUNNING'", new { id = ct.Id }, tx);
        return Rules.DedicatedSlotsLeft(ct.TripsPerMonth, ct.TripsDoneMonth, running);
    }

    public static Plan Build(Player p, int truckId, int contractId, double ujMult, int overloadPct, string policy, string sec, string fuelMode)
    {
        var R = Rules.Rng; using var c = Db.Open();
        var t = FleetSvc.One(truckId);
        var ct = c.QueryFirstOrDefault<Contract>("SELECT * FROM contracts WHERE id=@contractId AND player_id=@id", new { contractId, id = p.Id });
        if (t == null || t.PlayerId != p.Id || ct == null) return Err("Truk/kontrak tidak valid.");
        if (overloadPct != 0 && overloadPct != 20 && overloadPct != 40) return Err("Overload harus 0, 20, atau 40%.");
        if (ujMult < 0.5 || ujMult > 2.0) return Err("Pengali UJ tidak valid.");
        if (t.Status != "IDLE") return Err($"Truk belum siap (status {t.Status}).");
        if (t.DriverId == null) return Err("Truk belum punya supir.");
        var d = c.QueryFirstOrDefault<Driver>("SELECT * FROM drivers WHERE id=@x", new { x = t.DriverId });
        if (d == null || d.Status != "IDLE") return Err("Supir tidak tersedia.");

        bool spot = ct.Kind == "SPOT";
        if (spot ? ct.Status != "OPEN" : ct.Status != "ACTIVE")
            return Err(spot ? "Kontrak sudah diambil / kedaluwarsa." : "Kontrak borongan belum diterima atau sudah selesai.");
        if (!spot)
        {
            if (!Rules.OverloadAllowed(ct.Kind) && overloadPct > 0)
                return Err("Kontrak borongan dibayar tetap per bulan dan tidak memberi bonus muatan lebih, jadi overload tidak diizinkan.");
            if (SlotsLeft(c, ct) <= 0)
                return Err($"Target {ct.TripsPerMonth} trip/bulan sudah terpenuhi (selesai {ct.TripsDoneMonth} + masih berjalan). Tunggu bulan berikutnya.");
        }

        if (p.Reputation < ct.RepRequired) return Err($"Reputasi kurang (butuh {ct.RepRequired}, punya {p.Reputation}).");
        var cg = c.QueryFirst<CargoType>("SELECT * FROM cargo_types WHERE code=@x", new { x = ct.CargoCode });
        if (cg.B3 && !p.B3Cert) return Err("Kargo B3 butuh sertifikasi B3.");
        if (!cg.Bodies.Split(',').Contains(t.BodyCode ?? "")) return Err($"Bak {t.BodyCode} tidak cocok untuk {cg.Name} (butuh: {cg.Bodies}).");
        if (Rules.SimRank(d.Sim) < Rules.SimRank(t.Sim)) return Err($"SIM supir {d.Sim} tidak cukup (butuh {t.Sim}).");
        if (ct.WeightKg > t.Payload * (1 + overloadPct / 100.0)) return Err($"Muatan {ct.WeightKg} kg melebihi kapasitas {t.Payload} kg (overload {overloadPct}%).");

        var ev = WorldSvc.Active(p.Id, PlayerSvc.Day(p));
        bool mudik = ev.Any(e => e.Code == "MUDIK") && t.Axles >= 3;
        var g = RoadGraph.I;
        var path = g.Path(g.Id(ct.Origin), g.Id(ct.Dest), p.Tier == 3, mudik ? 1.3 : 1.0);
        if (path == null) return Err("Rute tidak ditemukan.");

        double road = 0, ferry = 0, tollKm = 0, pantKm = 0; long ferryCost = 0;
        foreach (var e in path)
        {
            if (e.Kind == "FERRY") { ferry += e.Km; ferryCost += Rules.FerryTicket(e, t); continue; }
            road += e.Km * (mudik && e.Toll ? 1.3 : 1.0);
            if (e.Toll && !mudik) tollKm += e.Km;
            if (e.Pantura) pantKm += e.Km;
        }
        bool rawanAny = path.Any(e => e.Rawan > 0);
        if (sec == "AUTO") sec = path.Any(e => e.Rawan >= 2) ? "ORMAS" : "NONE";
        long nominal = ct.Fee;

        // BBM & kelangkaan solar
        string fuel = t.FuelPref; double fmult = 1; string fm = "NORMAL";
        var langka = ev.FirstOrDefault(e => e.Code == "SOLAR_LANGKA");
        if (langka != null && path.Any(e => e.Pantura) && fuel == "BIO")
        {
            fm = fuelMode == "ECERAN" ? "ECERAN" : "ANTRE";
            if (fm == "ECERAN") fmult = langka.Magnitude;
        }
        double liters = Rules.Liters(road, t, ct.WeightKg, d);
        long fuelCost = (long)(liters * Rules.FuelPrice(fuel, p.Depot, fmult));
        long toll = mudik ? 0 : Rules.TollCost(tollKm, t.Axles);
        long meal = (long)(road * 150);
        long reserve = path.Where(e => e.Rawan > 0).Sum(e => e.Rawan * 35_000L);
        long escort = policy == "KAWAL" && overloadPct > 0 ? (long)(nominal * 0.025) : 0;
        long security = sec == "ORMAS" && rawanAny ? (long)(nominal * 0.02) : 0;

        // waktu tempuh
        double minutes = Rules.TravelMinutes(road, ferry, d);
        var notes = new List<string>();
        if (fm == "ANTRE") { minutes += pantKm / 100.0 * 15 * 0.35; notes.Add("Antre SPBU Pantura (solar langka): waktu bertambah."); }
        if (sec == "RESTAREA" && rawanAny) minutes *= 1.2;
        if (mudik) { minutes *= 1.1; notes.Add("Dilarang masuk tol (mudik): lewat arteri."); }
        foreach (var e in path.Where(e => e.Kind == "FERRY"))
        {
            int q = R.Next(30, 121); minutes += q; notes.Add($"Antre dermaga {e.Name}: +{q} mnt.");
            if (R.NextDouble() < (ev.Any(x => x.Code == "CUACA_BURUK") ? 0.5 : 0.15))
            { int w = R.Next(180, 361); minutes += w; notes.Add($"Gelombang tinggi menunda kapal: +{w} mnt."); }
        }

        long baseCost = fuelCost + toll + ferryCost + meal + reserve;
        long ujStd = Rules.UjStd(baseCost);
        long ujGiven = (long)(ujStd * ujMult);

        // ---- Perkiraan risiko & hasil (rumus sama dengan yang dipakai saat trip selesai) ----
        double riskF = Rules.MoraleRisk(d.Morale);
        bool razia = ev.Any(x => x.Code == "RAZIA_ODOL");
        var land = path.Where(x => x.Kind != "FERRY").ToList();
        double pBrake = Rules.AtLeastOne(land.Where(x => x.Steep).Select(x => Rules.BrakeFailProb(t.BrakePct) * riskF));
        double pTheft = Rules.AtLeastOne(land.Where(x => x.Rawan > 0).Select(x => Rules.TheftProb(x.Rawan, sec, riskF)));
        double pTilang = overloadPct > 0 ? Rules.AtLeastOne(land.Where(x => x.Weigh).Select(x => Rules.WeighProb(razia, policy))) : 0.0;
        long punguliEst = sec == "ORMAS" ? 0 : (long)land.Where(x => x.Rawan > 0).Sum(x => 0.35 * x.Rawan * 100_000.0);

        long incomeEst = spot ? ct.Fee + (long)(ct.Fee * overloadPct / 100.0 * 0.8) : 0;
        long commissionEst = (long)(incomeEst * d.CommissionPct / 100.0);
        long spendEst = fuelCost + toll + ferryCost + meal;
        double hf = d.Morale < 30 ? 0.5 : d.Honesty / 10.0;
        long returnedEst = (long)(Math.Max(0, ujGiven - spendEst) * hf);
        long shortEst = Math.Max(0, spendEst - ujGiven);
        long netOk = incomeEst - commissionEst - ujGiven + returnedEst - shortEst - escort - security;
        long cargoValue = Math.Max(ct.Fee, 200_000) * 2;
        long netWorst = netOk - (incomeEst - commissionEst) - cargoValue;

        return new Plan
        {
            Truck = t, Driver = d, Contract = ct, Path = path, Notes = notes, RoadKm = road, FerryKm = ferry, Liters = liters,
            Steep = path.Count(e => e.Steep), OverloadPct = overloadPct, FuelCost = fuelCost, Toll = toll, Ferry = ferryCost, Meal = meal,
            Reserve = reserve, Escort = escort, Security = security, CostEst = spendEst, UjStd = ujStd,
            UjGiven = ujGiven, Minutes = minutes, Policy = policy, Sec = sec, FuelMode = fm, FuelType = fuel, UjMult = ujMult,
            IncomeEst = incomeEst, CommissionEst = commissionEst, MonthlyShare = spot ? 0 : ct.MonthlyPay / Math.Max(1, ct.TripsPerMonth),
            NetOk = netOk, NetWorst = netWorst, PBrake = pBrake, PTheft = pTheft, PTilang = pTilang, PunguliEst = punguliEst
        };
    }

        public static string Dispatch(Player p, Plan pl)
    {
        if (pl.Error != null) return pl.Error;

        // Validasi ulang dari kondisi terbaru: kalau angka berubah sejak preview, batalkan tanpa memotong apa pun.
        var fresh = Build(PlayerSvc.Get(p.Id), pl.Truck.Id, pl.Contract.Id, pl.UjMult, pl.OverloadPct, pl.Policy, pl.Sec, pl.FuelMode);
        if (fresh.Error != null) return "Preview tidak berlaku lagi: " + fresh.Error;
        if (fresh.UjGiven != pl.UjGiven || fresh.Escort != pl.Escort || fresh.Security != pl.Security || fresh.IncomeEst != pl.IncomeEst)
            return "Biaya berubah sejak preview (harga, event, atau kondisi). Tidak ada yang dipotong, silakan buat preview baru.";

        long need = pl.UjGiven + pl.Escort + pl.Security;
        var now = DateTime.UtcNow; var arrive = now.AddMinutes(pl.Minutes / Rules.DevSpeed);
        long tripFee = pl.Contract.Kind == "SPOT" ? pl.Contract.Fee : 0;
        string notes = string.Join(" ", pl.Notes); if (notes.Length > 480) notes = notes[..480];
        int tripId = 0;

        try
        {
            using var c = Db.Open(); using var tx = c.BeginTransaction();
            // Semua langkah di bawah sukses bersama atau batal bersama (rollback otomatis kalau return sebelum Commit).
            if (c.Execute("UPDATE trucks SET status='ON_TRIP' WHERE id=@id AND player_id=@pid AND status='IDLE'", new { id = pl.Truck.Id, pid = p.Id }, tx) != 1)
                return "Truk tidak lagi siap jalan.";
            if (c.Execute("UPDATE drivers SET status='ON_TRIP' WHERE id=@id AND player_id=@pid AND status='IDLE'", new { id = pl.Driver.Id, pid = p.Id }, tx) != 1)
                return "Supir tidak lagi tersedia.";

            if (pl.Contract.Kind == "SPOT")
            {
                if (c.Execute("UPDATE contracts SET status='TAKEN' WHERE id=@id AND player_id=@pid AND status='OPEN'", new { id = pl.Contract.Id, pid = p.Id }, tx) != 1)
                    return "Kontrak sudah diambil / kedaluwarsa.";
            }
            else
            {
                var freshCt = c.QueryFirstOrDefault<Contract>("SELECT * FROM contracts WHERE id=@id AND player_id=@pid AND status='ACTIVE'", new { id = pl.Contract.Id, pid = p.Id }, tx);
                if (freshCt == null) return "Kontrak borongan tidak aktif.";
                if (SlotsLeft(c, freshCt, tx) <= 0) return "Target trip bulan ini sudah terpenuhi (selesai + masih berjalan).";
            }

            c.Execute(@"INSERT INTO trips(player_id,truck_id,driver_id,contract_id,path,km_road,km_ferry,steep,uj_given,cost_est,fuel_cost,fee,overload_pct,odol_policy,security,fuel_mode,fuel_type,uj_mult,started_at,arrive_at,status,notes,ins_type,cargo_ins)
                VALUES(@pid,@tid,@did,@cid,@path,@kr,@kf,@st,@uj,@ce,@fc,@fee,@ol,@pol,@sec,@fm,@ft,@um,@now,@arr,'RUNNING',@notes,@ins,@ci)",
                new { pid = p.Id, tid = pl.Truck.Id, did = pl.Driver.Id, cid = pl.Contract.Id, path = string.Join(",", pl.Path.Select(e => e.Id)),
                      kr = pl.RoadKm, kf = pl.FerryKm, st = pl.Steep, uj = pl.UjGiven, ce = pl.CostEst, fc = pl.FuelCost, fee = tripFee,
                      ol = pl.OverloadPct, pol = pl.Policy, sec = pl.Sec, fm = pl.FuelMode, ft = pl.FuelType, um = pl.UjMult, now, arr = arrive,
                      notes, ins = pl.Truck.Insurance, ci = pl.Truck.CargoIns }, tx);
            tripId = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()", transaction: tx);

            // UJ dan biaya awal keluar SEKALI di sini; hasil trip hanya mengembalikan sisa / menagih kekurangan.
            string rute = $"{pl.Contract.Origin}->{pl.Contract.Dest}";
            if (!Db.Spend(c, tx, p.Id, pl.UjGiven, "UANG_JALAN", $"UJ trip #{tripId} {rute}", tripId)
                || !Db.Spend(c, tx, p.Id, pl.Escort, "PENGAWALAN", $"Pengawalan trip #{tripId}", tripId)
                || !Db.Spend(c, tx, p.Id, pl.Security, "KEAMANAN", $"Uang keamanan trip #{tripId}", tripId))
                return $"Kas kurang. Butuh {Rules.Rp(need)} (UJ + biaya lain). Tidak ada yang berubah.";
            tx.Commit();
        }
        catch (MySqlException ex)
        {
            return "Gagal menyimpan perjalanan, tidak ada yang berubah (uang tidak dipotong): " + ex.Message;
        }
        return $"{pl.Truck.ModelName} berangkat ke {pl.Contract.Dest} (supir {pl.Driver.Name}), trip #{tripId}. UJ {Rules.Rp(pl.UjGiven)}. Tiba ~{pl.Minutes / Rules.DevSpeed:0.#} menit real-time. Laporan: menu Perjalanan > ID {tripId}.";
    }

    public static List<Trip> Running(int pid)
    {
        using var c = Db.Open();
        return c.Query<Trip>("SELECT * FROM trips WHERE player_id=@pid AND status='RUNNING'", new { pid }).ToList();
    }
    public static List<Trip> History(int pid)
    {
        using var c = Db.Open();
        return c.Query<Trip>("SELECT * FROM trips WHERE player_id=@pid AND status='DONE' ORDER BY id DESC LIMIT 5", new { pid }).ToList();
    }

    public static List<string> CompleteTrips(int pid)
    {
        var log = new List<string>(); using var c = Db.Open();
        var due = c.Query<Trip>("SELECT * FROM trips WHERE player_id=@pid AND status='RUNNING' AND arrive_at<=@now", new { pid, now = DateTime.UtcNow }).ToList();
        foreach (var t in due)
        {
            try { log.AddRange(Resolve(t)); }
            catch (MySqlException ex) { log.Add($"Gagal memproses trip #{t.Id} ({ex.Message}). Akan dicoba lagi."); }
        }
        return log;
    }

    static string Cut(string s, int n) => s.Length > n ? s[..n] : s;

    static List<string> Resolve(Trip t)
    {
        var R = Rules.Rng; var log = new List<string>(); using var c = Db.Open();
        var p = PlayerSvc.Get(t.PlayerId); int day = PlayerSvc.Day(p);
        var truck = FleetSvc.One(t.TruckId)!;
        var drv = c.QueryFirst<Driver>("SELECT * FROM drivers WHERE id=@id", new { id = t.DriverId });
        var ct = c.QueryFirst<Contract>("SELECT * FROM contracts WHERE id=@id", new { id = t.ContractId });
        var ev = WorldSvc.Active(p.Id, day); var g = RoadGraph.I;
        var edges = t.Path.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => g.EdgeById[int.Parse(s)]).ToList();

        long value = Rules.TruckValue(truck, day);
        long nominal = Math.Max(ct.Fee, 200_000), cargoValue = nominal * 2;
        long income = t.Fee + (long)(t.Fee * t.OverloadPct / 100.0 * 0.8);
        long costs = 0, pungli = 0, damage = 0, cargoLoss = 0;
        bool total = false, stolen = false, incident = false, fuelSold = false, quit = false;
        int repD = 0, moraleD = 0, crash = 0;
        double risk = Rules.MoraleRisk(drv.Morale);
        bool razia = ev.Any(e => e.Code == "RAZIA_ODOL");

        foreach (var e in edges)
        {
            if (total) break;
            if (e.Kind == "FERRY") continue;
            // Rem blong di tanjakan/turunan
            if (e.Steep)
            {
                double pb = Rules.BrakeFailProb(truck.BrakePct);
                if (R.NextDouble() < pb * risk)
                {
                    incident = true; crash++; repD -= 8;
                    bool severe = R.NextDouble() < 0.4;
                    log.Add($"REM BLONG di {e.Name}! {(severe ? "Kecelakaan parah." : "Berhasil dihentikan, kerusakan sedang.")}");
                    if (severe && R.NextDouble() < 0.35) { total = true; cargoLoss += cargoValue; log.Add("TRUK TOTAL LOSS!"); }
                    else { damage += (long)(value * (severe ? 0.25 : 0.08)); cargoLoss += (long)(cargoValue * (severe ? 0.5 : 0.15)); }
                }
            }
            if (total) break;
            // Jalur rawan: bajing loncat & pungli
            if (e.Rawan > 0)
            {
                if (!stolen && R.NextDouble() < Rules.TheftProb(e.Rawan, t.Security, risk))
                { stolen = true; income = 0; cargoLoss += cargoValue; repD -= 6; log.Add($"Kargo dicuri bajing loncat di {e.Name}!"); }
                if (t.Security != "ORMAS" && R.NextDouble() < 0.35) pungli += e.Rawan * 100_000L;
            }
            // Jembatan timbangan / razia ODOL
            if (e.Weigh && t.OverloadPct > 0)
            {
                double pc = Rules.WeighProb(razia, t.OdolPolicy);
                if (R.NextDouble() < pc)
                {
                    if (t.OdolPolicy == "BONGKAR")
                    { income -= (long)(t.Fee * t.OverloadPct / 100.0 * 0.8); log.Add($"Timbangan {e.Name}: muatan lebih dibongkar, bonus overload hangus."); }
                    else
                    { long fine = (long)(nominal * 0.10); costs += fine; repD -= 1; log.Add($"Ditilang ODOL di {e.Name}: denda {Rules.Rp(fine)}."); }
                }
            }
        }

        if (!total)
        {
            if ((truck.EnginePct < 15 || truck.OilPct < 10) && R.NextDouble() < 0.30)
            { long r = (long)(Rules.PartPrice("ENGINE", truck.Tier) * 0.3); costs += r; moraleD -= 8; log.Add($"Truk mogok di jalan, perbaikan {Rules.Rp(r)}."); }
            if (truck.TirePct < 15 && R.NextDouble() < 0.25)
            { long r = (long)(Rules.PartPrice("TIRE", truck.Tier) * 0.3); costs += r; moraleD -= 3; log.Add($"Ban pecah, ganti darurat {Rules.Rp(r)}."); }
            if (truck.IsEuro && t.FuelType == "BIO") log.Add("Truk Eropa diisi Bio Solar: mesin & filter rusak jauh lebih cepat!");
            double pBase = 0.008 * (drv.Speed / 5.0) * risk * (1 + t.KmRoad / 2000.0) * (t.Security == "RESTAREA" ? 0.6 : 1.0);
            if (R.NextDouble() < pBase)
            { incident = true; crash++; repD -= 3; damage += (long)(value * 0.05); cargoLoss += (long)(cargoValue * 0.1); log.Add("Senggolan/tabrakan, kerusakan ringan."); }
            if (drv.Morale < 30)
            {
                if (R.NextDouble() < 0.35) { fuelSold = true; log.Add("Moral rendah: supir menjual BBM di jalan!"); }
                if (!stolen && R.NextDouble() < 0.02 + (10 - drv.Honesty) * 0.004)
                { stolen = true; quit = true; income = 0; cargoLoss += cargoValue; repD -= 10; log.Add("Supir KABUR membawa kargo!"); }
            }
        }
        if (total || stolen) income = 0;
        income = Math.Max(0, income);
        bool ok = !total && !stolen;

        // Asuransi: pakai polis yang dikunci saat berangkat (bukan kondisi truk sekarang)
        long claim = Rules.InsuranceClaim(total, value, damage, cargoLoss, t.InsType, t.CargoIns);
        double newPen = Math.Min(2.0, p.PremiumPenalty + (claim > 0 ? 0.25 : 0));
        if (claim > 0) log.Add($"Klaim asuransi cair {Rules.Rp(claim)} (premi bulan depan naik).");

        // Penyelesaian uang jalan (UJ sudah dipotong saat berangkat)
        long spend = t.CostEst + pungli;
        long shortfall = Math.Max(0, spend - t.UjGiven), leftover = Math.Max(0, t.UjGiven - spend);
        double hf = drv.Morale < 30 ? 0.5 : drv.Honesty / 10.0; if (fuelSold) hf *= 0.7;
        long returned = (long)(leftover * hf);
        if (pungli > 0) log.Add($"Pungli di jalan: {Rules.Rp(pungli)}.");
        if (shortfall > 0) { moraleD -= 10; log.Add($"UJ kurang {Rules.Rp(shortfall)}, supir nombok dulu (moral turun), diganti perusahaan."); }
        moraleD += t.UjMult + 1e-9 < drv.UjExp ? -6 : 2;
        if (ok) moraleD += 1;

        long commission = ok ? (long)(income * drv.CommissionPct / 100.0) : 0;
        if (ok && !incident) repD += 2;
        long damagePaid = total ? 0 : damage;

        string head = ok ? $"Truk {truck.ModelName} tiba di {ct.Dest}. Pendapatan {Rules.Rp(income)}, komisi supir {Rules.Rp(commission)}."
                         : $"Pengiriman ke {ct.Dest} GAGAL.";
        log.Insert(0, head);

        // ---- Tulis hasil: semua atau tidak sama sekali. Setiap perubahan kas = satu baris ledger bertaut ke trip ----
        using var tx = c.BeginTransaction();
        // Klaim trip lebih dulu supaya tidak pernah diselesaikan dua kali
        if (c.Execute("UPDATE trips SET status='DONE' WHERE id=@id AND status='RUNNING'", new { id = t.Id }, tx) == 0)
            return new List<string>();

        string rute = $"{ct.Origin}->{ct.Dest}";
        Db.Post(c, tx, p.Id, income, "PENDAPATAN_TRIP", $"Trip #{t.Id} {rute}", t.Id);
        Db.Post(c, tx, p.Id, -commission, "KOMISI_SUPIR", $"Trip #{t.Id}", t.Id);
        Db.Post(c, tx, p.Id, returned, "UJ_KEMBALI", $"Sisa UJ trip #{t.Id}", t.Id);
        Db.Post(c, tx, p.Id, -shortfall, "UJ_KEKURANGAN", $"Nombok supir trip #{t.Id}", t.Id);
        Db.Post(c, tx, p.Id, -costs, "BIAYA_INSIDEN", $"Denda/mogok trip #{t.Id}", t.Id);
        Db.Post(c, tx, p.Id, -damagePaid, "KERUSAKAN", $"Kerusakan trip #{t.Id}", t.Id);
        Db.Post(c, tx, p.Id, -cargoLoss, "KARGO_HILANG", $"Kargo trip #{t.Id}", t.Id);
        Db.Post(c, tx, p.Id, claim, "KLAIM_ASURANSI", $"Klaim trip #{t.Id}", t.Id);

        long tripNet = c.ExecuteScalar<long>("SELECT COALESCE(SUM(amount),0) FROM ledger WHERE trip_id=@id AND player_id=@pid", new { id = t.Id, pid = p.Id }, tx);
        log.Add($"Laba/rugi trip #{t.Id} (termasuk UJ & biaya berangkat): {Accounting.Signed(tripNet)}.");
        if (ct.Kind != "SPOT")
            log.Add($"Pendapatan borongan (setara {Rules.Rp(ct.MonthlyPay / Math.Max(1, ct.TripsPerMonth))} per trip) cair saat tutup buku, bukan sekarang.");

        c.Execute("UPDATE players SET rep_delivery=GREATEST(0,LEAST(400,rep_delivery+@rep)),deliveries_ok=deliveries_ok+@ok,premium_penalty=@pen WHERE id=@id",
            new { rep = repD, ok = ok ? 1 : 0, pen = newPen, id = p.Id }, tx);
        if (total) c.Execute("UPDATE trucks SET status='TOTALLOSS',driver_id=NULL WHERE id=@id", new { id = truck.Id }, tx);
        else
        {
            var w = Rules.Wear(truck, t.KmRoad, t.Steep, drv.Morale, t.FuelType);
            // status='ON_TRIP' supaya truk yang sudah disita bank tidak "hidup lagi"
            c.Execute("UPDATE trucks SET status='IDLE',oil_pct=@o,brake_pct=@b,tire_pct=@ti,filter_pct=@f,engine_pct=@e" + (quit ? ",driver_id=NULL" : "") + " WHERE id=@id AND status='ON_TRIP'",
                new { o = w.oil, b = w.brake, ti = w.tire, f = w.filter, e = w.engine, id = truck.Id }, tx);
        }
        c.Execute("UPDATE drivers SET morale=@m,crash_count=crash_count+@cr,status=@s WHERE id=@id",
            new { m = Rules.Clamp(drv.Morale + moraleD, 0, 100), cr = crash, s = quit ? "QUIT" : "IDLE", id = drv.Id }, tx);
        if (ct.Kind == "SPOT") c.Execute("UPDATE contracts SET status='DONE' WHERE id=@id", new { id = ct.Id }, tx);
        else if (ok) c.Execute("UPDATE contracts SET trips_done_month=trips_done_month+1 WHERE id=@id", new { id = ct.Id }, tx);
        c.Execute("UPDATE trips SET result=@r WHERE id=@id", new { r = Cut(string.Join(" | ", log), 590), id = t.Id }, tx);
        tx.Commit();
        return log;
    }
}