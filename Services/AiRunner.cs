using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

// Pencatat jejak keputusan AI (tabel ai_decision_log). Kegagalan mencatat tidak boleh mengganggu game.
public static class AiLog
{
    public static void Write(int companyId, int day, string action, string status, string reason, string detail, string provider)
    {
        try
        {
            using var c = Db.Open();
            c.Execute(@"INSERT INTO ai_decision_log(company_id,day,action,status,reason,detail,provider) VALUES(@companyId,@day,@action,@status,@reason,@detail,@provider)",
                new { companyId, day, action, status, reason = Cut(reason, 400), detail = Cut(detail, 400), provider });
        }
        catch (Exception ex) { Console.WriteLine("Peringatan: gagal mencatat keputusan AI: " + ex.Message); }
    }

    static string Cut(string s, int n) => s.Length <= n ? s : s[..n];
}

// Menjalankan provider strategi dengan batas waktu dan validasi bentuk hasil.
// Provider yang gagal (error, timeout, hasil rusak) diganti strategi bawaan RuleBased, jadi simulasi tidak pernah berhenti.
// Provider eksternal (mis. layanan lain) cukup mengimplementasikan IStrategyProvider dan ditambahkan ke Providers.
public static class AiStrategyRunner
{
    public static readonly IStrategyProvider Fallback = new RuleBasedStrategy();
    public static List<IStrategyProvider> Providers = new() { Fallback };
    public static TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static (List<AiProposal> Proposals, string Provider, string? Failure) Propose(AiSnapshot s)
    {
        string? failure = null;
        foreach (var prov in Providers)
        {
            try { return (Run(prov, s), prov.Name, failure); }
            catch (Exception ex)
            {
                var inner = ex is AggregateException ae && ae.InnerException != null ? ae.InnerException : ex;
                failure = $"{prov.Name} gagal: {inner.Message}";
            }
        }

        // Semua provider gagal (atau daftar kosong): pakai strategi bawaan.
        try { return (Run(Fallback, s), Fallback.Name, failure); }
        catch (Exception ex) { return (new List<AiProposal>(), Fallback.Name, (failure ?? "") + $" | {Fallback.Name} gagal: {ex.Message}"); }
    }

    static List<AiProposal> Run(IStrategyProvider prov, AiSnapshot s)
    {
        var task = Task.Run(() => prov.Propose(s));
        if (!task.Wait(Timeout)) throw new TimeoutException($"melewati batas waktu {Timeout.TotalSeconds:0.##} detik");
        var result = task.Result ?? throw new InvalidDataException("hasil kosong (null)");
        var list = result.ToList();
        foreach (var p in list)
        {
            if (p == null) throw new InvalidDataException("ada usulan null");
            if (!AiActions.All.Contains(p.Type)) throw new InvalidDataException($"jenis tindakan tidak dikenal: {p.Type}");
            if (string.IsNullOrWhiteSpace(p.Reason)) throw new InvalidDataException("usulan tanpa alasan");
            if (p.Priority < -1000 || p.Priority > 1000) throw new InvalidDataException("prioritas di luar rentang");
        }
        return list;
    }
}

// Memvalidasi setiap usulan terhadap data ASLI (bukan potret). null = boleh dijalankan.
public static class AiActionValidator
{
    public static string? Check(AiProposal pr, int companyId, int day)
    {
        if (pr.CompanyId != companyId) return "Usulan ini bukan untuk perusahaan yang sedang giliran.";

        Player p;
        try { p = PlayerSvc.Get(companyId); }
        catch (InvalidOperationException) { return "Perusahaan tidak ditemukan."; }
        if (p.Bankrupt) return "Perusahaan sudah bangkrut.";
        var prof = AiProfiles.Get(CompanyPersonality(companyId));
        using var c = Db.Open();

        switch (pr.Type)
        {
            case AiActions.Bid:
            {
                var wc = MarketSvc.Get(pr.A);
                if (wc == null) return "Kontrak tidak ada.";
                return BidValidator.Check(companyId, wc, pr.Price, day);
            }
            case AiActions.BuyTruck:
            {
                var m = c.QueryFirstOrDefault<TruckModel>("SELECT * FROM truck_catalog WHERE id=@id", new { id = pr.A });
                if (m == null) return "Model truk tidak ada.";
                if (m.Tier > p.Tier) return $"Model butuh tier {MarketConfig.TierName(m.Tier)}; perusahaan ini tier {MarketConfig.TierName(p.Tier)}.";
                if (FleetSvc.Fleet(companyId).Count >= FleetSvc.MaxTrucks(p.Tier)) return "Garasi penuh.";
                if (p.Cash < m.Price) return $"Dana tidak cukup ({Rules.Rp(p.Cash)} < {Rules.Rp(m.Price)}).";
                if (p.Cash - m.Price < prof.CashReserve) return $"Pembelian akan melanggar cadangan kas {Rules.Rp(prof.CashReserve)}.";
                return null;
            }
            case AiActions.InstallBody:
            {
                var t = FleetSvc.One(pr.A);
                if (t == null || t.PlayerId != companyId) return "Truk bukan milik perusahaan ini.";
                if (t.Status != "WAIT_BODY") return "Truk tidak sedang menunggu bak.";
                var b = c.QueryFirstOrDefault<BodyType>("SELECT * FROM bodies WHERE code=@code AND price>0", new { code = pr.Code });
                if (b == null) return "Jenis bak tidak ada.";
                if (b.WeightKg >= t.MaxLoad) return "Bak terlalu berat untuk chassis ini.";
                long cost = (long)(b.Price * (1 + t.MaxLoad / 20000.0));
                return p.Cash < cost ? $"Dana tidak cukup untuk karoseri ({Rules.Rp(cost)})." : null;
            }
            case AiActions.Hire:
            {
                var d = c.QueryFirstOrDefault<Driver>("SELECT * FROM drivers WHERE id=@id AND player_id=@pid AND status='CANDIDATE'", new { id = pr.A, pid = companyId });
                if (d == null) return "Kandidat tidak ada.";
                var offer = Rules.ValidateOffer(pr.B);
                if (offer != null) return offer;
                int limit = Rules.MaxDrivers(FleetSvc.Fleet(companyId).Count);
                return HrSvc.Staff(companyId).Count >= limit ? $"Jumlah supir sudah mencapai batas ({limit})." : null;
            }
            case AiActions.RefreshCandidates:
                return p.Cash < 250_000 ? "Dana tidak cukup untuk pasang lowongan." : null;
            case AiActions.Assign:
            {
                var t = FleetSvc.One(pr.A);
                var d = c.QueryFirstOrDefault<Driver>("SELECT * FROM drivers WHERE id=@id AND player_id=@pid AND status='IDLE'", new { id = pr.B, pid = companyId });
                if (t == null || t.PlayerId != companyId) return "Truk bukan milik perusahaan ini.";
                if (d == null) return "Supir tidak tersedia.";
                if (t.Status == "ON_TRIP") return "Truk sedang jalan.";
                return Rules.SimRank(d.Sim) < Rules.SimRank(t.Sim) ? $"SIM {d.Sim} tidak cukup (butuh {t.Sim})." : null;
            }
            case AiActions.Dispatch:
            {
                var ct = c.QueryFirstOrDefault<Contract>("SELECT * FROM contracts WHERE id=@id AND player_id=@pid AND status IN ('OPEN','ACTIVE')", new { id = pr.A, pid = companyId });
                if (ct == null) return "Kontrak tidak ada atau bukan milik perusahaan ini.";
                var t = FleetSvc.One(pr.B);
                if (t == null || t.PlayerId != companyId) return "Truk bukan milik perusahaan ini.";
                if (t.Status != "IDLE") return $"Truk belum siap (status {t.Status}).";
                return t.DriverId == null ? "Truk belum punya supir." : null;
            }
            case AiActions.Service:
            {
                var t = FleetSvc.One(pr.A);
                if (t == null || t.PlayerId != companyId) return "Truk bukan milik perusahaan ini.";
                return t.Status != "IDLE" ? "Truk harus IDLE untuk servis." : null;
            }
            case AiActions.UpgradeTier:
            case AiActions.RentPool:
                if (p.Tier >= 3) return "Sudah di tier tertinggi.";
                return pr.Type == AiActions.UpgradeTier && p.Cash < AiRules.TierUpgradeCost(p.Tier) ? "Dana tidak cukup untuk naik tier." : null;
            default:
                return "Jenis tindakan tidak dikenal.";
        }
    }

    static string CompanyPersonality(int id)
    {
        using var c = Db.Open();
        return c.ExecuteScalar<string>("SELECT personality FROM players WHERE id=@id", new { id }) ?? "SEIMBANG";
    }
}

// Menjalankan usulan yang sudah lolos validasi, lewat service game yang SAMA dengan yang dipakai pemain.
// Keberhasilan dipastikan dari perubahan keadaan nyata, bukan dari teks balasan.
public static class AiExecutor
{
    public static (bool Ok, string Detail) Execute(AiProposal pr, int id, int day)
    {
        switch (pr.Type)
        {
            case AiActions.Bid:
            {
                var msg = MarketSvc.PlaceBid(id, pr.A, pr.Price, day);
                return (msg.Contains("tercatat"), msg);
            }
            case AiActions.BuyTruck:
            {
                int before = FleetSvc.Fleet(id).Count;
                var msg = FleetSvc.BuyNew(id, pr.A);
                return (FleetSvc.Fleet(id).Count > before, msg);
            }
            case AiActions.InstallBody:
            {
                var msg = FleetSvc.Karoseri(id, pr.A, pr.Code);
                return (FleetSvc.One(pr.A)?.Status == "KAROSERI", msg);
            }
            case AiActions.Hire:
            {
                int before = HrSvc.Staff(id).Count;
                var msg = HrSvc.Hire(id, pr.A, pr.B);
                return (HrSvc.Staff(id).Count > before, msg);
            }
            case AiActions.RefreshCandidates:
            {
                var msg = HrSvc.Refresh(id, true);
                return (msg.StartsWith("Lowongan"), msg == "" ? "Lowongan tidak dipasang." : msg);
            }
            case AiActions.Assign:
            {
                var msg = FleetSvc.Assign(id, pr.A, pr.B);
                return (FleetSvc.One(pr.A)?.DriverId == pr.B, msg);
            }
            case AiActions.Dispatch:
            {
                var p = PlayerSvc.Get(id);
                var plan = TripSvc.Build(p, pr.B, pr.A, 1.0, 0, "DENDA", "AUTO", "NORMAL");
                if (plan.Error != null) return (false, plan.Error);
                if (plan.NetOk < 0) return (false, $"Trip tidak menguntungkan (laba bersih sekitar {Accounting.Signed(plan.NetOk)}), tidak diberangkatkan.");
                int before = TripSvc.Running(id).Count;
                var msg = TripSvc.Dispatch(p, plan);
                return (TripSvc.Running(id).Count > before, msg);
            }
            case AiActions.Service:
            {
                var msg = FleetSvc.Service(id, pr.A, "ALL");
                return (msg.StartsWith("Servis"), msg);
            }
            case AiActions.UpgradeTier:
            {
                int before = PlayerSvc.Get(id).Tier;
                var msg = PlayerSvc.UpgradeTier(id);
                return (PlayerSvc.Get(id).Tier > before, msg);
            }
            case AiActions.RentPool:
            {
                var msg = PlayerSvc.RentPool(id);
                return (PlayerSvc.Get(id).PoolType != "NONE", msg);
            }
            default:
                return (false, "Jenis tindakan tidak dikenal.");
        }
    }
}