using Dapper;
using Nusantara.Database;

namespace Nusantara.Services;

public sealed record AiFacts(int CompanyId, string Personality, long Cash, int Tier, int Reputation, int Trucks, int IdleTrucks, int ActiveContracts);
public sealed record AiGoalSet(string Short, string Long);

// Rencana jangka pendek/panjang AI. Tujuan berubah hanya kalau keadaan berubah DAN perubahan itu terkonfirmasi:
//  - ditinjau tiap ReviewIntervalDays, bukan tiap tick;
//  - tujuan baru harus diusulkan ConfirmCount kali berturut-turut;
//  - tujuan lama harus sudah dipegang minimal MinHold hari;
//  - kas kritis (RAISE_CASH) boleh mengganti jangka pendek seketika.
public static class AiPlanSvc
{
    public const int ReviewIntervalDays = 5;
    public const int ShortMinHold = 5;
    public const int LongMinHold = 20;
    public const int ConfirmCount = 2;

    sealed class PlanRow
    {
        public int CompanyId { get; set; } public string ShortGoal { get; set; } = ""; public string LongGoal { get; set; } = "";
        public int ShortSetDay { get; set; } public int LongSetDay { get; set; }
        public string? PendingShort { get; set; } public int PendingShortCount { get; set; }
        public string? PendingLong { get; set; } public int PendingLongCount { get; set; }
        public int LastReviewDay { get; set; }
    }

    // Tujuan yang diinginkan menurut keadaan sekarang (fungsi murni, mudah diuji).
    public static AiGoalSet Desire(AiFacts f)
    {
        var prof = AiProfiles.Get(f.Personality);

        string shortGoal =
            f.Cash < AiRules.LowCash ? AiGoals.RaiseCash :
            f.Trucks == 0 ? AiGoals.BuildFleet :
            f.ActiveContracts > 0 ? AiGoals.CompleteContracts :
            AiGoals.FillCapacity;

        // Ambang reputasi 50 dan 250 mengikuti syarat PlayerSvc.UpgradeTier.
        string longGoal;
        if (f.Tier >= 3) longGoal = f.Reputation < 300 ? AiGoals.BuildReputation : AiGoals.ExpandFleet;
        else switch (prof.Code)
        {
            case "HEMAT": longGoal = f.Reputation < 100 ? AiGoals.BuildReputation : AiGoals.UpgradeTier; break;
            case "PREMIUM": longGoal = f.Reputation < 250 ? AiGoals.BuildReputation : AiGoals.UpgradeTier; break;
            case "EKSPANSI":
            case "AGRESIF": longGoal = f.Trucks < prof.FleetTarget * f.Tier ? AiGoals.ExpandFleet : AiGoals.UpgradeTier; break;
            default: longGoal = f.Reputation < (f.Tier == 1 ? 50 : 250) ? AiGoals.BuildReputation : AiGoals.UpgradeTier; break;
        }
        return new AiGoalSet(shortGoal, longGoal);
    }

    // Satu langkah histeresis untuk satu tujuan.
    public static (string Goal, int SetDay, string? Pending, int Count) Step(
        string current, int setDay, string? pending, int pendingCount, string want, int day, int minHold, bool force)
    {
        if (want == current) return (current, setDay, null, 0);
        if (force) return (want, day, null, 0);
        int count = pending == want ? pendingCount + 1 : 1;
        if (count >= ConfirmCount && day - setDay >= minHold) return (want, day, null, 0);
        return (current, setDay, want, count);
    }

    public static AiGoalSet Review(AiFacts f, int day)
    {
        var want = Desire(f);
        using var c = Db.Open();
        var row = c.QueryFirstOrDefault<PlanRow>("SELECT * FROM ai_plan WHERE company_id=@id", new { id = f.CompanyId });
        if (row == null)
        {
            c.Execute(@"INSERT INTO ai_plan(company_id,short_goal,long_goal,short_set_day,long_set_day,last_review_day,reason)
                VALUES(@id,@s,@l,@day,@day,@day,@r)",
                new { id = f.CompanyId, s = want.Short, l = want.Long, day, r = $"Rencana awal: {want.Short} / {want.Long}" });
            return want;
        }

        bool urgent = f.Cash < AiRules.CriticalCash && want.Short == AiGoals.RaiseCash && row.ShortGoal != AiGoals.RaiseCash;
        if (!urgent && day - row.LastReviewDay < ReviewIntervalDays) return new AiGoalSet(row.ShortGoal, row.LongGoal);

        var s = Step(row.ShortGoal, row.ShortSetDay, row.PendingShort, row.PendingShortCount, want.Short, day, ShortMinHold, urgent);
        var l = Step(row.LongGoal, row.LongSetDay, row.PendingLong, row.PendingLongCount, want.Long, day, LongMinHold, false);

        c.Execute(@"UPDATE ai_plan SET short_goal=@sg,short_set_day=@ssd,pending_short=@ps,pending_short_count=@psc,
                long_goal=@lg,long_set_day=@lsd,pending_long=@pl,pending_long_count=@plc,last_review_day=@day,reason=@r WHERE company_id=@id",
            new { sg = s.Goal, ssd = s.SetDay, ps = s.Pending, psc = s.Count, lg = l.Goal, lsd = l.SetDay, pl = l.Pending, plc = l.Count, day,
                  r = $"Hari {day}: kas {Rules.Rp(f.Cash)}, {f.Trucks} truk, reputasi {f.Reputation}; jangka pendek {s.Goal}, jangka panjang {l.Goal}", id = f.CompanyId });
        return new AiGoalSet(s.Goal, l.Goal);
    }
}