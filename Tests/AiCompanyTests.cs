using Dapper;
using Nusantara.Database;
using Nusantara.Models;
using Nusantara.Services;
using Xunit;

namespace Nusantara.Tests;

// Skenario wajib Dokumen 02 untuk perusahaan AI: keputusan berbasis data, validasi, fallback, rencana stabil.
[Collection("sqlite")]
public class AiCompanyTests : IDisposable
{
    readonly string dir;
    readonly Func<long> oldStamp;

    public AiCompanyTests()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        Db.Init();
        dir = Path.Combine(Path.GetTempPath(), "nusantara-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", dir);
        oldStamp = SimClock.Stamp;
        long fake = 1_000;
        SimClock.Stamp = () => fake;
        AiCompanySvc.Enabled = false;   // AI baru bertindak saat tes memanggil RunDay / mengaktifkannya
    }

    public void Dispose()
    {
        AiCompanySvc.Enabled = true;
        AiStrategyRunner.Providers = new List<IStrategyProvider> { AiStrategyRunner.Fallback };
        AiStrategyRunner.Timeout = TimeSpan.FromSeconds(2);
        SaveSlots.Shutdown();
        SimClock.Unload();
        SimClock.Stamp = oldStamp;
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", null);
        try { Directory.Delete(dir, true); } catch { /* sementara */ }
    }

    // ---------- alat bantu ----------

    (int human, List<int> ai) NewWorld()
    {
        int human = SaveSlots.Create(1, "PT Pemain Uji");
        MarketSvc.RunDaily();
        return (human, CompanySvc.AiIds());
    }

    static int Count(string sql, object? p = null)
    {
        using var c = Db.Open();
        return c.ExecuteScalar<int>(sql, p);
    }

    static int Logged(int id, string action, string status) =>
        Count("SELECT COUNT(*) FROM ai_decision_log WHERE company_id=@id AND action=@action AND status=@status", new { id, action, status });

    // Satu pickup siap jalan + satu supir yang ditugaskan (lewat service yang sama dengan pemain).
    static void Equip(int id)
    {
        Assert.Contains("siap jalan", FleetSvc.BuyNew(id, 1));
        var cand = HrSvc.Candidates(id).First();
        Assert.Contains("diterima", HrSvc.Hire(id, cand.Id, 100));
        var truck = FleetSvc.Fleet(id).First();
        Assert.Contains("ditugaskan", FleetSvc.Assign(id, truck.Id, cand.Id));
    }

    static int InsertWorld(string origin = "A", string dest = "B", int minTier = 1, long fee = 2_000_000, int kg = 400)
    {
        using var c = Db.Open();
        c.Execute(@"INSERT INTO world_contracts(kind,scope,min_tier,client,origin,dest,cargo_code,weight_kg,fee,rep_required,vehicle_class,
                quote_km,quote_rate,quote_mult,status,created_day,bid_close_day,expire_day)
            VALUES('SPOT','DOMESTIC',@minTier,'Klien Uji',@origin,@dest,'PAKET',@kg,@fee,0,'PICKUP',10,5000,1.0,'OPEN',0,0,10)",
            new { origin, dest, minTier, kg, fee });
        return c.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    // Pasangan simpul sepulau dengan rute jalan terpendek, supaya trip uji murah dan pasti menguntungkan.
    static (string a, string b) ShortRoute()
    {
        var g = RoadGraph.I; (string a, string b) best = ("", ""); double bestKm = double.MaxValue;
        foreach (var x in g.Nodes.Values)
            foreach (var y in g.Nodes.Values)
            {
                if (x.Id >= y.Id || x.Island != y.Island) continue;
                var path = g.Path(x.Id, y.Id, false, 1.0);
                if (path == null) continue;
                double km = path.Where(e => e.Kind == "ROAD").Sum(e => (double)e.Km);
                if (km > 0 && km < bestKm) { bestKm = km; best = (x.Name, y.Name); }
            }
        Assert.NotEqual("", best.a);
        return best;
    }

    static AiSnapshot Snap(long cash = 1_500_000_000, int rep = 0, string personality = "SEIMBANG", string shortGoal = AiGoals.FillCapacity,
                           IEnumerable<AiRival>? rivals = null, IEnumerable<WorldContract>? market = null)
        => new(1, "PT Uji", personality, 0, cash, 1, rep, "NONE", shortGoal, AiGoals.UpgradeTier,
               new List<AiTruck> { new(1, "IDLE", "BLINDVAN", 500, 750, "A", 5, 100) }, new List<AiDriver>(), new List<AiDriver>(),
               (market ?? Array.Empty<WorldContract>()).ToList(), new List<Contract>(),
               new Dictionary<string, string[]> { ["PAKET"] = new[] { "PICKUP", "BLINDVAN" } },
               new List<TruckModel>(), new List<BodyType>(), (rivals ?? Array.Empty<AiRival>()).ToList());

    static WorldContract Wc(long fee = 2_000_000, int kg = 400, int rep = 0) => new()
    {
        Id = 1, Kind = "SPOT", MinTier = 1, CargoCode = "PAKET", WeightKg = kg, Fee = fee, RepRequired = rep,
        VehicleClass = "PICKUP", Status = "OPEN", ExpireDay = 10, Origin = "A", Dest = "B", Scope = "DOMESTIC"
    };

    static long? Price(AiSnapshot s) => new RuleBasedStrategy().Propose(s).FirstOrDefault(p => p.Type == AiActions.Bid)?.Price;

    // ---------- keputusan AI dari kondisi bisnis (skenario 1, 3) ----------

    [Fact]
    public void Ai_buys_a_truck_hires_a_driver_assigns_it_and_bids_and_logs_every_decision()
    {
        var (_, ai) = NewWorld();
        int id = ai[0];
        InsertWorld();

        for (int i = 0; i < 3; i++) AiCompanySvc.RunDay(id, 0);

        Assert.Equal(1, FleetSvc.Fleet(id).Count);
        Assert.Equal(1, HrSvc.Staff(id).Count);
        Assert.NotNull(FleetSvc.Fleet(id).First().DriverId);
        Assert.True(Logged(id, AiActions.BuyTruck, "DONE") >= 1);
        Assert.True(Logged(id, AiActions.Hire, "DONE") >= 1);
        Assert.True(Logged(id, AiActions.Assign, "DONE") >= 1);
        Assert.True(Logged(id, AiActions.Bid, "DONE") >= 1);
        Assert.InRange(Count("SELECT COUNT(*) FROM contract_bids WHERE company_id=@id", new { id }), 1, 1);   // satu truk = satu komitmen
        Assert.Equal(0, Count("SELECT COUNT(*) FROM ai_decision_log WHERE company_id=@id AND reason=''", new { id }));
    }

    [Fact]
    public void Ai_does_not_buy_a_truck_when_it_cannot_afford_it()
    {
        var (_, ai) = NewWorld();
        int id = ai[0];
        using (var c = Db.Open()) c.Execute("UPDATE players SET cash=1000 WHERE id=@id", new { id });

        AiCompanySvc.RunDay(id, 0);

        Assert.Empty(FleetSvc.Fleet(id));
        Assert.Equal(0, Logged(id, AiActions.BuyTruck, "DONE"));
    }

    [Fact]
    public void Invalid_proposals_from_a_misbehaving_provider_are_rejected_by_validation()
    {
        var (human, ai) = NewWorld();
        int id = ai[0];
        int tier2 = InsertWorld(minTier: 2);
        using (var c = Db.Open()) c.Execute("UPDATE players SET cash=1000 WHERE id=@id", new { id });
        AiStrategyRunner.Providers = new List<IStrategyProvider> { new RogueProvider(id, human, tier2) };

        var summary = AiCompanySvc.RunDay(id, 0);

        Assert.Equal(0, summary.Done);
        Assert.Equal(4, summary.Rejected);
        Assert.Empty(FleetSvc.Fleet(id));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM contract_bids WHERE company_id=@id", new { id }));
        Assert.Equal(4, Count("SELECT COUNT(*) FROM ai_decision_log WHERE company_id=@id AND status='REJECTED' AND provider='Rogue'", new { id }));
    }

    // ---------- skenario 5: provider gagal, game tetap jalan ----------

    [Fact] public void Throwing_provider_falls_back_to_rule_based() => FallbackScenario(new ThrowingProvider(), "Throwing");
    [Fact] public void Null_result_falls_back_to_rule_based() => FallbackScenario(new NullProvider(), "Null");

    [Fact]
    public void Slow_provider_times_out_and_falls_back_to_rule_based()
    {
        AiStrategyRunner.Timeout = TimeSpan.FromMilliseconds(200);
        FallbackScenario(new SlowProvider(), "Slow");
    }

    void FallbackScenario(IStrategyProvider bad, string name)
    {
        var (_, ai) = NewWorld();
        int id = ai[0];
        AiStrategyRunner.Providers = new List<IStrategyProvider> { bad };

        var summary = AiCompanySvc.RunDay(id, 0);

        Assert.Equal("RuleBased", summary.Provider);
        Assert.Equal(1, FleetSvc.Fleet(id).Count);   // AI tetap bertindak lewat strategi bawaan
        Assert.Equal(1, Count("SELECT COUNT(*) FROM ai_decision_log WHERE company_id=@id AND action='PROVIDER' AND status='FAILED' AND detail LIKE @n", new { id, n = name + "%" }));
    }

    [Fact]
    public void Run_all_keeps_every_ai_company_active_even_when_the_provider_is_broken()
    {
        var (_, ai) = NewWorld();
        AiCompanySvc.Enabled = true;
        AiStrategyRunner.Providers = new List<IStrategyProvider> { new ThrowingProvider() };
        AiStrategyRunner.Timeout = TimeSpan.FromMilliseconds(200);

        AiCompanySvc.RunAll(0);   // tidak boleh melempar

        Assert.All(ai, id => Assert.Equal(1, FleetSvc.Fleet(id).Count));
    }

    // ---------- skenario 6: keputusan menyesuaikan kondisi, alasan masuk akal ----------

    [Fact]
    public void Bid_price_adapts_to_rivals_cash_needs_and_personality()
    {
        var calm = Price(Snap(market: new[] { Wc() }));
        var pressured = Price(Snap(market: new[] { Wc() }, rivals: new[] { new AiRival("A", 1, 300, 3), new AiRival("B", 1, 300, 3), new AiRival("C", 1, 300, 3) }));
        var needsCash = Price(Snap(shortGoal: AiGoals.RaiseCash, market: new[] { Wc() }));
        var aggressive = Price(Snap(personality: "AGRESIF", market: new[] { Wc() }));
        var frugal = Price(Snap(personality: "HEMAT", market: new[] { Wc() }));

        Assert.NotNull(calm);
        Assert.True(pressured < calm, "Pesaing yang lebih kuat harus menekan harga tawaran.");
        Assert.True(needsCash < calm, "Butuh kas harus menekan harga supaya lebih mudah menang.");
        Assert.True(aggressive < frugal, "Kepribadian agresif menawar lebih murah daripada hemat.");
    }

    [Fact]
    public void Ai_skips_unprofitable_ineligible_and_off_brand_contracts()
    {
        Assert.Null(Price(Snap(market: new[] { Wc(fee: 100_000) })));                          // di bawah margin
        Assert.Null(Price(Snap(market: new[] { Wc(rep: 100) })));                              // reputasi belum cukup
        Assert.Null(Price(Snap(personality: "PREMIUM", market: new[] { Wc() })));              // premium hanya kontrak bernilai/bersyarat
        Assert.NotNull(Price(Snap(personality: "PREMIUM", market: new[] { Wc(fee: 4_000_000) })));

        var proposal = new RuleBasedStrategy().Propose(Snap(market: new[] { Wc() })).First(p => p.Type == AiActions.Bid);
        Assert.False(string.IsNullOrWhiteSpace(proposal.Reason));
        Assert.False(string.IsNullOrWhiteSpace(proposal.ExpectedImpact));
    }

    // ---------- skenario 6: rencana tidak berubah acak ----------

    [Fact]
    public void Goals_do_not_flip_on_noise_but_change_after_confirmation_and_in_emergencies()
    {
        // Usulan bolak-balik tidak pernah dikonfirmasi: tujuan tetap.
        string goal = AiGoals.FillCapacity; int setDay = 0; string? pending = null; int count = 0;
        for (int day = 5; day <= 55; day += 5)
        {
            string want = (day / 5) % 2 == 0 ? AiGoals.CompleteContracts : AiGoals.RaiseCash;
            (goal, setDay, pending, count) = AiPlanSvc.Step(goal, setDay, pending, count, want, day, AiPlanSvc.ShortMinHold, false);
        }
        Assert.Equal(AiGoals.FillCapacity, goal);

        // Usulan yang sama dua kali berturut-turut (dan masa tahan terpenuhi): tujuan berganti.
        (goal, setDay, pending, count) = AiPlanSvc.Step(goal, setDay, pending, count, AiGoals.CompleteContracts, 60, AiPlanSvc.ShortMinHold, false);
        Assert.Equal(AiGoals.FillCapacity, goal);
        (goal, setDay, pending, count) = AiPlanSvc.Step(goal, setDay, pending, count, AiGoals.CompleteContracts, 65, AiPlanSvc.ShortMinHold, false);
        Assert.Equal(AiGoals.CompleteContracts, goal);

        // Darurat kas: berganti seketika.
        var urgent = AiPlanSvc.Step(goal, 65, null, 0, AiGoals.RaiseCash, 66, AiPlanSvc.ShortMinHold, true);
        Assert.Equal(AiGoals.RaiseCash, urgent.Goal);
    }

    [Fact]
    public void Plan_is_stored_once_and_not_reviewed_again_within_the_interval()
    {
        var (_, ai) = NewWorld();
        var rich = new AiFacts(ai[0], "SEIMBANG", 1_500_000_000, 1, 0, 0, 0, 0);
        var first = AiPlanSvc.Review(rich, 0);

        // Keadaan memburuk tapi belum waktunya tinjau ulang dan belum darurat: tujuan tetap.
        var poor = rich with { Cash = AiRules.LowCash - 1 };
        var again = AiPlanSvc.Review(poor, 2);

        Assert.Equal(first, again);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM ai_plan WHERE company_id=@id", new { id = ai[0] }));

        // Kas kritis: langsung berganti ke RAISE_CASH walau belum waktunya tinjau.
        var broke = rich with { Cash = AiRules.CriticalCash - 1 };
        Assert.Equal(AiGoals.RaiseCash, AiPlanSvc.Review(broke, 3).Short);
    }

    // ---------- skenario 7: aturan sama untuk AI (jalan sampai trip) ----------

    [Fact]
    public void Ai_dispatches_an_awarded_contract_through_the_same_trip_rules_as_the_player()
    {
        var (_, ai) = NewWorld();
        int id = ai[0];
        var (a, b) = ShortRoute();

        // Armada disiapkan lewat service biasa dan pasar acak dikosongkan, supaya hanya kontrak uji ini yang ada.
        Equip(id);
        using (var c = Db.Open()) c.Execute("DELETE FROM world_contracts");
        int wid = InsertWorld(origin: a, dest: b);
        Assert.Contains("tercatat", MarketSvc.PlaceBid(id, wid, 1_900_000, 0));
        MarketSvc.Settle(0);
        Assert.Equal("AWARDED", MarketSvc.Get(wid)!.Status);

        AiCompanySvc.RunDay(id, 0);

        Assert.Equal(1, TripSvc.Running(id).Count);
        Assert.Equal(1, Logged(id, AiActions.Dispatch, "DONE"));
        Assert.Equal("ON_TRIP", FleetSvc.Fleet(id).First().Status);
    }

    // ---------- seluruh siklus harian ----------

    [Fact]
    public void Daily_run_activates_all_ai_companies_and_audits_stay_clean()
    {
        AiCompanySvc.Enabled = true;
        var (_, ai) = NewWorld();

        Assert.Equal(MarketConfig.AiCompanyCount, ai.Count);
        Assert.All(ai, id => Assert.True(FleetSvc.Fleet(id).Count >= 1, "Setiap AI membeli truk pertamanya."));
        Assert.True(Count("SELECT COUNT(*) FROM ai_decision_log") > 0);
        Assert.False(IntegrityAudit.HasErrors(IntegrityAudit.Run()));
        Assert.False(IntegrityAudit.HasErrors(MarketAudit.Run()));
    }

    // ---------- provider tiruan ----------

    sealed class ThrowingProvider : IStrategyProvider
    {
        public string Name => "Throwing";
        public IReadOnlyList<AiProposal> Propose(AiSnapshot s) => throw new InvalidOperationException("provider rusak (simulasi)");
    }

    sealed class NullProvider : IStrategyProvider
    {
        public string Name => "Null";
        public IReadOnlyList<AiProposal> Propose(AiSnapshot s) => null!;
    }

    sealed class SlowProvider : IStrategyProvider
    {
        public string Name => "Slow";
        public IReadOnlyList<AiProposal> Propose(AiSnapshot s) { Thread.Sleep(3000); return new List<AiProposal>(); }
    }

    // Mengusulkan hal-hal yang semuanya melanggar aturan: dana kurang, tier kurang, kontrak tier tinggi, perusahaan lain.
    sealed class RogueProvider : IStrategyProvider
    {
        readonly int id, other, tier2Contract;
        public RogueProvider(int id, int other, int tier2Contract) { this.id = id; this.other = other; this.tier2Contract = tier2Contract; }
        public string Name => "Rogue";
        public IReadOnlyList<AiProposal> Propose(AiSnapshot s) => new List<AiProposal>
        {
            new(id, AiActions.BuyTruck, 90, "Beli truk murah padahal dana habis", "-", 190_000_000, A: 1),
            new(id, AiActions.BuyTruck, 80, "Beli truk tier 3 padahal tier 1", "-", 2_400_000_000, A: 12),
            new(id, AiActions.Bid, 70, "Tawar kontrak Regional padahal Lokal", "-", 0, A: tier2Contract, Price: 1_900_000),
            new(other, AiActions.BuyTruck, 60, "Usulan untuk perusahaan lain", "-", 190_000_000, A: 1)
        };
    }
}