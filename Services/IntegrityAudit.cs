using System.Data;
using Dapper;
using Nusantara.Database;

namespace Nusantara.Services;

public enum AuditSeverity { Info, Warning, Error }
public sealed record AuditIssue(AuditSeverity Severity, string Code, string Message);

// Pemeriksa konsistensi data (hanya MEMBACA). Skema belum memakai foreign key, jadi relasi dicek di sini.
// Aman dijalankan pada save lama: tabel yang belum ada (mis. event_ledger di skema v1) dilewati.
public static class IntegrityAudit
{
    sealed class PlayerCashRow { public long Id { get; set; } public long Cash { get; set; } public long Bankrupt { get; set; } }
    sealed class TypeCountRow { public string Type { get; set; } = ""; public long N { get; set; } }

    static readonly Dictionary<string, string> LegacyEventTypes = new()
    {
        ["COMPANY_FOUNDER"] = EventTypes.CompanyFounded
    };

    static bool Has(IDbConnection c, string table) =>
        c.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@table", new { table }) > 0;

    public static List<AuditIssue> Run()
    {
        using var c = Db.Open();
        return Run(c);
    }

    public static List<AuditIssue> Run(IDbConnection c)
    {
        var r = new List<AuditIssue>();
        void Add(AuditSeverity s, string code, string msg) => r.Add(new AuditIssue(s, code, msg));

        // ---- Uang ----
        foreach (var p in c.Query<PlayerCashRow>("SELECT id, cash, bankrupt FROM players"))
        {
            if (p.Cash < 0 && p.Bankrupt == 0)
                Add(AuditSeverity.Warning, "NEGATIVE_CASH", $"Perusahaan #{p.Id} kas minus ({Rules.Rp(p.Cash)}) tapi belum bangkrut.");

            long? last = c.ExecuteScalar<long?>("SELECT balance_after FROM ledger WHERE player_id=@id ORDER BY id DESC LIMIT 1", new { id = p.Id });
            if (last.HasValue && last.Value != p.Cash)
                Add(AuditSeverity.Error, "CASH_LEDGER_MISMATCH",
                    $"Kas perusahaan #{p.Id} ({Rules.Rp(p.Cash)}) tidak sama dengan saldo akhir buku kas ({Rules.Rp(last.Value)}).");
        }

        int badChain = c.ExecuteScalar<int>(@"SELECT COUNT(*) FROM (
            SELECT amount, balance_after, LAG(balance_after) OVER (PARTITION BY player_id ORDER BY id) AS prev FROM ledger)
            WHERE prev IS NOT NULL AND balance_after <> prev + amount");
        if (badChain > 0)
            Add(AuditSeverity.Error, "LEDGER_CHAIN_BROKEN", $"{badChain} baris buku kas tidak nyambung dengan saldo sebelumnya.");

        foreach (var t in c.Query<long>("SELECT trip_id FROM ledger WHERE category='PENDAPATAN_TRIP' AND trip_id IS NOT NULL GROUP BY trip_id HAVING COUNT(*)>1"))
            Add(AuditSeverity.Error, "DUPLICATE_TRIP_PAYMENT", $"Trip #{t} dibayar lebih dari satu kali.");

        // ---- Relasi (pengganti foreign key) ----
        void Orphans(string code, string label, string sql)
        {
            int n = c.ExecuteScalar<int>(sql);
            if (n > 0) Add(AuditSeverity.Error, code, $"{n} {label}");
        }
        Orphans("ORPHAN_LEDGER_TRIP", "baris buku kas menunjuk trip yang tidak ada.",
            "SELECT COUNT(*) FROM ledger l WHERE l.trip_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM trips t WHERE t.id=l.trip_id)");
        Orphans("ORPHAN_TRIP_REF", "trip menunjuk truk, supir, atau kontrak yang tidak ada.",
            @"SELECT COUNT(*) FROM trips t WHERE NOT EXISTS (SELECT 1 FROM trucks x WHERE x.id=t.truck_id)
              OR NOT EXISTS (SELECT 1 FROM drivers d WHERE d.id=t.driver_id) OR NOT EXISTS (SELECT 1 FROM contracts k WHERE k.id=t.contract_id)");
        Orphans("ORPHAN_TRUCK_DRIVER", "truk menunjuk supir yang tidak ada.",
            "SELECT COUNT(*) FROM trucks t WHERE t.driver_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM drivers d WHERE d.id=t.driver_id)");
        Orphans("ORPHAN_OWNER", "truk/supir/kontrak/trip milik perusahaan yang tidak ada.",
            @"SELECT (SELECT COUNT(*) FROM trucks WHERE player_id NOT IN (SELECT id FROM players))
                   + (SELECT COUNT(*) FROM drivers WHERE player_id NOT IN (SELECT id FROM players))
                   + (SELECT COUNT(*) FROM contracts WHERE player_id NOT IN (SELECT id FROM players))
                   + (SELECT COUNT(*) FROM trips WHERE player_id NOT IN (SELECT id FROM players))");

        foreach (var x in c.Query<long>("SELECT truck_id FROM trips WHERE status='RUNNING' GROUP BY truck_id HAVING COUNT(*)>1"))
            Add(AuditSeverity.Error, "TRUCK_ON_TWO_TRIPS", $"Truk #{x} sedang di dua perjalanan sekaligus.");
        foreach (var x in c.Query<long>("SELECT driver_id FROM trips WHERE status='RUNNING' GROUP BY driver_id HAVING COUNT(*)>1"))
            Add(AuditSeverity.Error, "DRIVER_ON_TWO_TRIPS", $"Supir #{x} sedang di dua perjalanan sekaligus.");

        // ---- Event, berita, digest (tabel baru di skema v2) ----
        if (Has(c, "event_ledger"))
        {
            foreach (var row in c.Query<TypeCountRow>("SELECT type, COUNT(*) AS n FROM event_ledger GROUP BY type"))
            {
                if (EventTypes.All.Contains(row.Type)) continue;
                if (LegacyEventTypes.TryGetValue(row.Type, out var modern))
                    Add(AuditSeverity.Info, "LEGACY_EVENT_TYPE", $"{row.N} event bertipe lama {row.Type} (kini {modern}); tidak diubah karena event ledger tidak boleh diubah.");
                else
                    Add(AuditSeverity.Warning, "UNKNOWN_EVENT_TYPE", $"{row.N} event bertipe tidak dikenal: {row.Type}.");
            }

            long? maxSeq = c.ExecuteScalar<long?>("SELECT MAX(sequence) FROM event_ledger");
            long state = c.ExecuteScalar<long>("SELECT last_event_sequence FROM game_state WHERE id=1");
            if ((maxSeq ?? 0) != state)
                Add(AuditSeverity.Error, "EVENT_SEQUENCE_MISMATCH", $"Urutan event terakhir ({maxSeq ?? 0}) tidak sama dengan penghitung game_state ({state}).");

            foreach (var e in c.Query<long>("SELECT event_id FROM news_item WHERE event_id IS NOT NULL GROUP BY event_id HAVING COUNT(*)>1"))
                Add(AuditSeverity.Error, "DUPLICATE_NEWS", $"Event #{e} punya lebih dari satu berita.");
            Orphans("ORPHAN_NEWS", "berita menunjuk event yang tidak ada.",
                "SELECT COUNT(*) FROM news_item n WHERE n.event_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM event_ledger e WHERE e.event_id=n.event_id)");
        }

        if (Has(c, "digest"))
        {
            int bad = c.ExecuteScalar<int>($"SELECT COUNT(*) FROM digest WHERE period_end - period_start <> {DigestSvc.PeriodDays}");
            if (bad > 0) Add(AuditSeverity.Error, "DIGEST_RANGE_INVALID", $"{bad} digest punya rentang bukan {DigestSvc.PeriodDays} hari.");
        }

        return r;
    }

    public static bool HasErrors(IEnumerable<AuditIssue> issues) => issues.Any(i => i.Severity == AuditSeverity.Error);
}