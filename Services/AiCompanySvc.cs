using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

public sealed record AiRunSummary(int Done, int Rejected, int Failed, string Provider);

// Giliran harian perusahaan AI: bereskan operasi -> tinjau rencana -> minta usulan -> validasi -> jalankan -> catat.
public static class AiCompanySvc
{
    // Tes bisa mematikan AI otomatis. Kegagalan AI tidak pernah menghentikan game (lihat RunAll).
    public static bool Enabled = true;

    public static void RunAll(int day)
    {
        if (!Enabled) return;
        foreach (var id in CompanySvc.AiIds())
        {
            try { RunDay(id, day); }
            catch (Exception ex) { AiLog.Write(id, day, "RUN_DAY", "FAILED", "Giliran AI gagal; simulasi tetap berjalan.", ex.Message, ""); }
        }
    }

    public static AiRunSummary RunDay(int id, int day)
    {
        Tick(id);
        var p = PlayerSvc.Get(id);
        if (p.Bankrupt) return new AiRunSummary(0, 0, 0, "-");

        var goals = AiPlanSvc.Review(FactsFor(id, p), day);
        var snap = BuildSnapshot(id, p, day, goals);
        var (proposals, provider, failure) = AiStrategyRunner.Propose(snap);
        if (failure != null)
            AiLog.Write(id, day, "PROVIDER", "FAILED", "Provider strategi gagal, memakai strategi bawaan.", failure, provider);

        int done = 0, rejected = 0, failed = 0;
        foreach (var pr in proposals.OrderByDescending(x => x.Priority).Take(AiRules.MaxActionsPerDay))
        {
            var err = AiActionValidator.Check(pr, id, day);
            if (err != null)
            {
                AiLog.Write(id, day, pr.Type, "REJECTED", pr.Reason, err, provider);
                rejected++;
                continue;
            }
            var (ok, detail) = AiExecutor.Execute(pr, id, day);
            AiLog.Write(id, day, pr.Type, ok ? "DONE" : "FAILED", pr.Reason, detail, provider);
            if (ok) done++; else failed++;
        }
        return new AiRunSummary(done, rejected, failed, provider);
    }

    // Operasi rutin yang sama dengan WorldSvc.Tick milik pemain, tanpa pembuatan kontrak pribadi dan digest.
    public static void Tick(int id)
    {
        MirrorWorldEvents(id);
        FleetSvc.FinishBodies(id);
        TripSvc.CompleteTrips(id);

        var p = PlayerSvc.Get(id);
        if (p.Bankrupt) return;
        int day = PlayerSvc.Day(p);
        if (day > p.LastDay)
        {
            using var c = Db.Open();
            c.Execute("UPDATE players SET last_day=@day WHERE id=@id", new { day, id });
        }
        p = PlayerSvc.Get(id); int guard = 0;
        while (day >= p.NextMonthDay && guard++ < 12)
        {
            FinanceSvc.CloseMonth(id);
            p = PlayerSvc.Get(id);
            if (p.Bankrupt) break;
        }
    }

    // Kejadian dunia (solar langka, mudik, razia, dll.) berlaku sama untuk semua perusahaan: salin dari perusahaan pemain.
    static void MirrorWorldEvents(int id)
    {
        var human = CompanySvc.HumanId();
        if (human == null || human == id) return;
        int day = PlayerSvc.Day(PlayerSvc.Get(id));
        using var c = Db.Open();
        c.Execute(@"INSERT INTO world_events(player_id,code,title,start_day,end_day,magnitude,cargos)
            SELECT @id, h.code, h.title, h.start_day, h.end_day, h.magnitude, h.cargos FROM world_events h
            WHERE h.player_id=@human AND h.end_day>=@day
              AND NOT EXISTS (SELECT 1 FROM world_events x WHERE x.player_id=@id AND x.code=h.code AND x.start_day=h.start_day)",
            new { id, human = human.Value, day });
    }

    static AiFacts FactsFor(int id, Player p)
    {
        var fleet = FleetSvc.Fleet(id);
        return new AiFacts(id, CompanyPersonality(id), p.Cash, p.Tier, p.Reputation, fleet.Count,
                           fleet.Count(t => t.Status == "IDLE"), ContractSvc.Open(id).Count);
    }

    static string CompanyPersonality(int id)
    {
        using var c = Db.Open();
        return c.ExecuteScalar<string>("SELECT personality FROM players WHERE id=@id", new { id }) ?? "SEIMBANG";
    }

    static AiSnapshot BuildSnapshot(int id, Player p, int day, AiGoalSet goals)
    {
        var fleet = FleetSvc.Fleet(id);
        var trucks = fleet.Select(t => new AiTruck(t.Id, t.Status, t.BodyCode, t.Payload, t.MaxLoad, t.Sim, t.DriverId,
            Math.Min(Math.Min(t.OilPct, t.BrakePct), Math.Min(Math.Min(t.TirePct, t.FilterPct), t.EnginePct)))).ToList();
        var staff = HrSvc.Staff(id).Select(d => new AiDriver(d.Id, d.Sim, d.Status, d.Salary, d.Honesty + d.Eco + d.Speed)).ToList();
        var cands = HrSvc.Candidates(id).Select(d => new AiDriver(d.Id, d.Sim, d.Status, d.SalaryExp, d.Honesty + d.Eco + d.Speed)).ToList();

        var biddedOn = MarketSvc.MyBids(id, 1000).Select(b => b.WorldContractId).ToHashSet();
        var market = MarketSvc.Open(day).Where(w => !biddedOn.Contains(w.Id)).ToList();

        Dictionary<string, string[]> cargoBodies;
        List<int> otherIds;
        using (var c = Db.Open())
        {
            cargoBodies = c.Query<CargoType>("SELECT * FROM cargo_types").ToDictionary(x => x.Code, x => x.Bodies.Split(','));
            otherIds = c.Query<int>("SELECT id FROM players WHERE id<>@id", new { id }).ToList();
        }
        var rivals = otherIds.Select(o => { var r = PlayerSvc.Get(o); return new AiRival(r.Name, r.Tier, r.Reputation, FleetSvc.Fleet(o).Count); }).ToList();

        return new AiSnapshot(id, p.Name, CompanyPersonality(id), day, p.Cash, p.Tier, p.Reputation, p.PoolType,
            goals.Short, goals.Long, trucks, staff, cands, market, ContractSvc.Open(id), cargoBodies,
            FleetSvc.Models(p.Tier), FleetSvc.Bodies(), rivals);
    }
}