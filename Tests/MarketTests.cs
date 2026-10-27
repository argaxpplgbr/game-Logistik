using Dapper;
using Microsoft.Data.Sqlite;
using Nusantara.Database;
using Nusantara.Services;
using Xunit;

namespace Nusantara.Tests;

// Skenario wajib Dokumen 02 untuk pasar bersama: satu pasar, satu pemenang, syarat yang sama untuk pemain dan AI.
[Collection("sqlite")]
public class MarketTests : IDisposable
{
    readonly string dir;
    readonly Func<long> oldStamp;

    public MarketTests()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        Db.Init();
        dir = Path.Combine(Path.GetTempPath(), "nusantara-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", dir);
        oldStamp = SimClock.Stamp;
        long fake = 1_000;
        SimClock.Stamp = () => fake;
        AiCompanySvc.Enabled = false;
    }

    public void Dispose()
    {
        AiCompanySvc.Enabled = true;
        SaveSlots.Shutdown();
        SimClock.Unload();
        SimClock.Stamp = oldStamp;
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", null);
        try { Directory.Delete(dir, true); } catch { /* sementara */ }
    }

    // ---------- alat bantu ----------

    // Dunia baru: satu pemain + tiga perusahaan AI.
    (int human, List<int> ai) NewWorld()
    {
        int human = SaveSlots.Create(1, "PT Pemain Uji");
        MarketSvc.RunDaily();
        return (human, CompanySvc.AiIds());
    }

    // Satu pickup siap jalan + satu supir yang ditugaskan, supaya perusahaan boleh menawar kontrak kecil.
    static void Equip(int id)
    {
        Assert.Contains("siap jalan", FleetSvc.BuyNew(id, 1));
        var cand = HrSvc.Candidates(id).First();
        Assert.Contains("diterima", HrSvc.Hire(id, cand.Id, 100));
        var truck = FleetSvc.Fleet(id).First();
        Assert.Contains("ditugaskan", FleetSvc.Assign(id, truck.Id, cand.Id));
    }

    static int InsertWorld(string kind = "SPOT", int minTier = 1, string scope = "DOMESTIC", int rep = 0,
                           long fee = 2_000_000, int kg = 500, int closeDay = 0, int expireDay = 10)
    {
        using var c = Db.Open();
        c.Execute(@"INSERT INTO world_contracts(kind,scope,min_tier,client,origin,dest,cargo_code,weight_kg,fee,rep_required,vehicle_class,
                quote_km,quote_rate,quote_mult,status,created_day,bid_close_day,expire_day)
            VALUES(@kind,@scope,@minTier,'Klien Uji','A','B','PAKET',@kg,@fee,@rep,'PICKUP',10,5000,1.0,'OPEN',0,@closeDay,@expireDay)",
            new { kind, scope, minTier, kg, fee, rep, closeDay, expireDay });
        return c.ExecuteScalar<int>("SELECT last_insert_rowid()");
    }

    static int Count(string sql, object? p = null)
    {
        using var c = Db.Open();
        return c.ExecuteScalar<int>(sql, p);
    }

    // ---------- skenario 1: satu pasar untuk semua ----------

    [Fact]
    public void Player_and_three_ai_companies_share_one_market()
    {
        var (human, ai) = NewWorld();

        Assert.Equal(MarketConfig.AiCompanyCount, ai.Count);
        Assert.All(ai, id => Assert.Equal("AI", CompanySvc.Kind(id)));
        Assert.Equal("PLAYER", CompanySvc.Kind(human));

        // Hari semua perusahaan sama, dan modal awal AI sama dengan pemain.
        int world = CompanySvc.WorldDay();
        foreach (var id in ai)
        {
            Assert.Equal(world, PlayerSvc.Day(PlayerSvc.Get(id)));
            Assert.Equal(EconomySvc.StartingCash, PlayerSvc.Get(id).Cash);
        }

        // Peluang kontrak dimiliki dunia, bukan satu perusahaan.
        var open = MarketSvc.Open(world);
        Assert.InRange(open.Count, 1, MarketConfig.TargetOpenSpot + MarketConfig.TargetOpenDedicated);
        Assert.Equal(0, Count("SELECT COUNT(*) FROM contracts WHERE world_contract_id IS NOT NULL"));
    }

    [Fact]
    public void Daily_market_run_is_idempotent_for_the_same_day()
    {
        NewWorld();
        int contracts = Count("SELECT COUNT(*) FROM world_contracts");
        int companies = Count("SELECT COUNT(*) FROM players");

        MarketSvc.RunDaily();
        MarketSvc.RunDaily();

        Assert.Equal(contracts, Count("SELECT COUNT(*) FROM world_contracts"));
        Assert.Equal(companies, Count("SELECT COUNT(*) FROM players"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM sim_job_log WHERE job=@j", new { j = MarketSvc.JobName }));
    }

    // ---------- skenario 2: satu kontrak, satu pemenang ----------

    [Fact]
    public void Two_companies_bidding_the_same_contract_produce_exactly_one_winner()
    {
        var (human, ai) = NewWorld();
        Equip(human); Equip(ai[0]);
        int wid = InsertWorld();

        Assert.Contains("tercatat", MarketSvc.PlaceBid(human, wid, 1_900_000, 0));
        Assert.Contains("tercatat", MarketSvc.PlaceBid(ai[0], wid, 1_900_000, 0));

        MarketSvc.Settle(0);
        MarketSvc.Settle(0);   // dijalankan ulang: tidak boleh membuat pemenang kedua

        Assert.Equal(1, Count("SELECT COUNT(*) FROM contract_awards WHERE world_contract_id=@wid", new { wid }));
        Assert.Equal("AWARDED", MarketSvc.Get(wid)!.Status);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM contract_bids WHERE world_contract_id=@wid AND status='WON'", new { wid }));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM contract_bids WHERE world_contract_id=@wid AND status='LOST'", new { wid }));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM contracts WHERE world_contract_id=@wid", new { wid }));

        // Seri sempurna -> reputasi sama -> perusahaan ber-ID lebih kecil (pemain) menang, dan alasannya tercatat.
        string reason;
        int execId, winner;
        using (var c = Db.Open())
        {
            reason = c.ExecuteScalar<string>("SELECT reason FROM contract_awards WHERE world_contract_id=@wid", new { wid })!;
            execId = c.ExecuteScalar<int>("SELECT execution_contract_id FROM contract_awards WHERE world_contract_id=@wid", new { wid });
            winner = c.ExecuteScalar<int>("SELECT company_id FROM contract_awards WHERE world_contract_id=@wid", new { wid });
        }
        Assert.Equal(human, winner);
        Assert.Contains("unggul", reason);

        // Kontrak pelaksanaan muncul di alur kontrak yang sudah ada, hanya milik pemenang.
        Assert.Contains(ContractSvc.Open(winner), k => k.Id == execId);
        Assert.DoesNotContain(ContractSvc.Open(ai[0]), k => k.Id == execId);

        // Perusahaan lain tidak bisa menawar kontrak yang sudah diberikan.
        Assert.Contains("tidak tersedia", MarketSvc.PlaceBid(ai[0], wid, 1_900_000, 0));

        // Basis data sendiri menolak pemenang kedua.
        using var c2 = Db.Open();
        Assert.Throws<SqliteException>(() => c2.Execute(
            "INSERT INTO contract_awards(world_contract_id,company_id,bid_id,score,reason,awarded_day) VALUES(@wid,@cid,999,1,'x',0)",
            new { wid, cid = ai[0] }));
    }

    [Fact]
    public void Award_is_recorded_as_event_and_news_but_bids_are_not_news()
    {
        var (human, ai) = NewWorld();
        Equip(human); Equip(ai[0]);
        int wid = InsertWorld();
        MarketSvc.PlaceBid(human, wid, 1_900_000, 0);
        MarketSvc.PlaceBid(ai[0], wid, 1_900_000, 0);
        MarketSvc.Settle(0);

        Assert.Equal(2, Count("SELECT COUNT(*) FROM event_ledger WHERE type=@t", new { t = EventTypes.BidPlaced }));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM event_ledger WHERE type=@t", new { t = EventTypes.ContractAwarded }));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM news_item n JOIN event_ledger e ON e.event_id=n.event_id WHERE e.type=@t", new { t = EventTypes.BidPlaced }));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM news_item n JOIN event_ledger e ON e.event_id=n.event_id WHERE e.type=@t", new { t = EventTypes.ContractAwarded }));
    }

    // ---------- penilaian transparan ----------

    [Fact]
    public void Lower_price_and_higher_reputation_each_raise_the_score_and_cheapest_does_not_always_win()
    {
        var (human, _) = NewWorld();
        Equip(human);
        var wc = MarketSvc.Get(InsertWorld())!;

        double cheapNoRep = BidScoring.Score(PlayerSvc.Get(human), wc, 1_400_000).Total;
        double dearNoRep = BidScoring.Score(PlayerSvc.Get(human), wc, 1_900_000).Total;
        Assert.True(cheapNoRep > dearNoRep, "Harga lebih rendah harus menaikkan skor.");

        using (var c = Db.Open()) c.Execute("UPDATE players SET rep_brand=400 WHERE id=@human", new { human });
        double dearHighRep = BidScoring.Score(PlayerSvc.Get(human), wc, 1_900_000).Total;
        Assert.True(dearHighRep > dearNoRep, "Reputasi lebih tinggi harus menaikkan skor.");
        Assert.True(dearHighRep > cheapNoRep, "Penawar mahal ber-reputasi tinggi bisa mengalahkan penawar murah tanpa reputasi.");
    }

    // ---------- skenario 3: dana dan kemampuan ----------

    [Fact]
    public void Company_without_truck_or_funds_cannot_bid_or_buy()
    {
        var (_, ai) = NewWorld();
        int id = ai[0];
        int wid = InsertWorld();

        Assert.Contains("Belum punya truk", MarketSvc.PlaceBid(id, wid, 1_900_000, 0));

        Equip(id);
        using (var c = Db.Open()) c.Execute("UPDATE players SET cash=1000 WHERE id=@id", new { id });
        Assert.Contains("Dana belum cukup", MarketSvc.PlaceBid(id, wid, 1_900_000, 0));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM contract_bids WHERE company_id=@id", new { id }));

        // Beli truk dengan dana tidak cukup: ditolak dan jumlah truk tidak bertambah.
        int before = FleetSvc.Fleet(id).Count;
        Assert.Contains("Uang kurang", FleetSvc.BuyNew(id, 1));
        Assert.Equal(before, FleetSvc.Fleet(id).Count);
    }

    // ---------- skenario 4: tier dan internasional ----------

    [Fact]
    public void Lower_tier_company_cannot_take_higher_tier_or_international_contract()
    {
        var (human, _) = NewWorld();
        Equip(human);

        Assert.Contains("tier", MarketSvc.PlaceBid(human, InsertWorld(minTier: 2), 1_900_000, 0));

        int intl = InsertWorld(scope: "INTERNATIONAL");
        Assert.Contains("internasional", MarketSvc.PlaceBid(human, intl, 1_900_000, 0));

        // Tier Nasional (3) pun belum boleh: layanan internasional hanya untuk tier Internasional.
        using (var c = Db.Open()) c.Execute("UPDATE players SET tier=3 WHERE id=@human", new { human });
        Assert.Contains("internasional", MarketSvc.PlaceBid(human, intl, 1_900_000, 0));

        // Tier Internasional boleh, tetapi tetap hanya lewat kontrak internasional ini.
        using (var c = Db.Open()) c.Execute("UPDATE players SET tier=@t WHERE id=@human", new { t = MarketConfig.InternationalTier, human });
        Assert.DoesNotContain("internasional", MarketSvc.PlaceBid(human, intl, 1_900_000, 0));
    }

    // ---------- skenario 7: aturan sama untuk pemain dan AI ----------

    [Fact]
    public void Player_and_ai_get_identical_verdicts_for_identical_situations()
    {
        var (human, ai) = NewWorld();
        Equip(human); Equip(ai[0]);

        int needsRep = InsertWorld(rep: 100);
        Assert.Equal(MarketSvc.PlaceBid(human, needsRep, 1_900_000, 0), MarketSvc.PlaceBid(ai[0], needsRep, 1_900_000, 0));

        int needsTier = InsertWorld(minTier: 2);
        Assert.Equal(MarketSvc.PlaceBid(human, needsTier, 1_900_000, 0), MarketSvc.PlaceBid(ai[0], needsTier, 1_900_000, 0));

        int tooCheap = InsertWorld();
        Assert.Equal(MarketSvc.PlaceBid(human, tooCheap, 100_000, 0), MarketSvc.PlaceBid(ai[0], tooCheap, 100_000, 0));
    }

    // ---------- syarat diperiksa ulang saat penetapan ----------

    [Fact]
    public void Settle_rejects_a_bid_whose_company_no_longer_qualifies_and_picks_the_next()
    {
        var (human, ai) = NewWorld();
        Equip(human); Equip(ai[0]);
        int wid = InsertWorld();
        MarketSvc.PlaceBid(human, wid, 1_900_000, 0);
        MarketSvc.PlaceBid(ai[0], wid, 1_900_000, 0);

        using (var c = Db.Open()) c.Execute("UPDATE players SET cash=0 WHERE id=@human", new { human });
        var msgs = MarketSvc.Settle(0);

        Assert.Contains(msgs, m => m.Contains("ditolak saat penetapan"));
        Assert.Equal("REJECTED", Bid(wid, human));
        Assert.Equal("WON", Bid(wid, ai[0]));
    }

    static string Bid(int wid, int company)
    {
        using var c = Db.Open();
        return c.ExecuteScalar<string>("SELECT status FROM contract_bids WHERE world_contract_id=@wid AND company_id=@company", new { wid, company })!;
    }

    // ---------- audit pasar ----------

    [Fact]
    public void Market_audit_is_clean_after_an_award_and_detects_a_missing_execution()
    {
        var (human, ai) = NewWorld();
        Equip(human);
        int wid = InsertWorld();
        MarketSvc.PlaceBid(human, wid, 1_900_000, 0);
        MarketSvc.Settle(0);

        Assert.False(IntegrityAudit.HasErrors(MarketAudit.Run()));
        Assert.False(IntegrityAudit.HasErrors(IntegrityAudit.Run()));

        using (var c = Db.Open()) c.Execute("DELETE FROM contracts WHERE world_contract_id=@wid", new { wid });
        Assert.Contains(MarketAudit.Run(), i => i.Code == "AWARD_WITHOUT_EXECUTION");
    }
}