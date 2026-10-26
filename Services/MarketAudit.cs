using System.Data;
using Dapper;

namespace Nusantara.Services;

// Pemeriksa konsistensi pasar bersama (hanya membaca). Melengkapi IntegrityAudit.
public static class MarketAudit
{
    static bool Has(IDbConnection c, string table) =>
        c.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@table", new { table }) > 0;

    public static List<AuditIssue> Run(IDbConnection c)
    {
        var r = new List<AuditIssue>();
        if (!Has(c, "world_contracts")) return r;   // save lama sebelum migrasi pasar bersama

        void Check(string code, string label, string sql)
        {
            int n = c.ExecuteScalar<int>(sql);
            if (n > 0) r.Add(new AuditIssue(AuditSeverity.Error, code, $"{n} {label}"));
        }

        Check("AWARDED_WITHOUT_AWARD", "kontrak berstatus AWARDED tanpa catatan pemenang.",
            "SELECT COUNT(*) FROM world_contracts w WHERE w.status='AWARDED' AND NOT EXISTS (SELECT 1 FROM contract_awards a WHERE a.world_contract_id=w.id)");
        Check("OPEN_WITH_WINNER", "kontrak masih OPEN padahal sudah punya pemenang.",
            "SELECT COUNT(*) FROM world_contracts w WHERE w.status='OPEN' AND EXISTS (SELECT 1 FROM contract_awards a WHERE a.world_contract_id=w.id)");
        Check("AWARD_WITHOUT_EXECUTION", "pemenang tanpa kontrak pelaksanaan milik perusahaan pemenang.",
            @"SELECT COUNT(*) FROM contract_awards a WHERE NOT EXISTS
              (SELECT 1 FROM contracts k WHERE k.id=a.execution_contract_id AND k.player_id=a.company_id AND k.world_contract_id=a.world_contract_id)");
        Check("MULTIPLE_WINNERS", "kontrak punya lebih dari satu penawaran berstatus WON.",
            "SELECT COUNT(*) FROM (SELECT world_contract_id FROM contract_bids WHERE status='WON' GROUP BY world_contract_id HAVING COUNT(*)>1)");
        Check("ORPHAN_BID", "penawaran menunjuk kontrak atau perusahaan yang tidak ada.",
            @"SELECT COUNT(*) FROM contract_bids b WHERE NOT EXISTS (SELECT 1 FROM world_contracts w WHERE w.id=b.world_contract_id)
              OR NOT EXISTS (SELECT 1 FROM players p WHERE p.id=b.company_id)");
        return r;
    }

    public static List<AuditIssue> Run()
    {
        using var c = Nusantara.Database.Db.Open();
        return Run(c);
    }
}