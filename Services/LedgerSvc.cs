using Dapper;
using Nusantara.Database;

namespace Nusantara.Services;

public class LedgerEntry
{
    public int Id { get; set; } public int GameDay { get; set; } public DateTime CreatedAt { get; set; }
    public string Category { get; set; } = ""; public string Note { get; set; } = "";
    public int? TripId { get; set; } public long Amount { get; set; } public long BalanceAfter { get; set; }
}
public class LedgerSum { public string Category { get; set; } = ""; public long Total { get; set; } }
public class TripInfo
{
    public string Status { get; set; } = ""; public string Origin { get; set; } = ""; public string Dest { get; set; } = "";
    public string Kind { get; set; } = ""; public long MonthlyPay { get; set; } public int TripsPerMonth { get; set; }
    public long WearEst { get; set; } public long OverheadEst { get; set; }
}

public static class LedgerSvc
{
    static readonly Dictionary<string, string> Labels = new()
    {
        ["SALDO_AWAL"] = "Saldo awal", ["UANG_JALAN"] = "Uang jalan (UJ)", ["PENGAWALAN"] = "Jasa pengawalan",
        ["KEAMANAN"] = "Uang keamanan", ["PENDAPATAN_TRIP"] = "Pendapatan trip", ["KOMISI_SUPIR"] = "Komisi supir",
        ["UJ_KEMBALI"] = "Sisa UJ dikembalikan", ["UJ_KEKURANGAN"] = "Kekurangan UJ", ["BIAYA_INSIDEN"] = "Denda/mogok di jalan",
        ["KERUSAKAN"] = "Kerusakan truk", ["KARGO_HILANG"] = "Kargo hilang/rusak", ["KLAIM_ASURANSI"] = "Klaim asuransi",
        ["PEMBAYARAN_BORONGAN"] = "Bayaran kontrak borongan", ["GAJI_STAF"] = "Gaji, BPJS & staf", ["SEWA_POOL"] = "Sewa pool",
        ["PREMI_ASURANSI"] = "Premi asuransi", ["CICILAN_BANK"] = "Cicilan bank", ["PINJAMAN_CAIR"] = "Pinjaman cair",
        ["PELUNASAN_PINJAMAN"] = "Pelunasan pinjaman", ["FASILITAS"] = "Fasilitas/tier", ["MARKETING"] = "Kampanye marketing",
        ["TOPUP_SIMULASI"] = "Top-up (simulasi)", ["HADIAH_TUTORIAL"] = "Hadiah tutorial",
        ["BuyNew"] = "Beli truk baru", ["BuyUsed"] = "Beli truk bekas", ["Karoseri"] = "Karoseri (pasang bak)",
        ["Service"] = "Servis truk", ["Refresh"] = "Pasang lowongan supir", ["Interview"] = "Interview kandidat"
    };
    public static string Label(string code) => Labels.TryGetValue(code, out var s) ? s : code;

    // Catat saldo awal sekali per perusahaan supaya jumlah ledger selalu sama dengan kas.
    public static void EnsureOpening(int pid)
    {
        using var c = Db.Open(); using var tx = c.BeginTransaction();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM ledger WHERE player_id=@pid", new { pid }, tx) > 0) return;
        Db.Log(c, tx, pid, c.ExecuteScalar<long>("SELECT cash FROM players WHERE id=@pid", new { pid }, tx), "SALDO_AWAL", "Saldo saat buku kas dimulai");
        tx.Commit();
    }

    public static List<LedgerEntry> Recent(int pid, string? category, int limit)
    {
        using var c = Db.Open();
        string sql = "SELECT id, game_day, created_at, category, note, trip_id, amount, balance_after FROM ledger WHERE player_id=@pid"
            + (category != null ? " AND category=@category" : "") + " ORDER BY id DESC LIMIT @limit";
        return c.Query<LedgerEntry>(sql, new { pid, category, limit }).ToList();
    }

    public static List<string> Categories(int pid)
    {
        using var c = Db.Open();
        return c.Query<string>("SELECT DISTINCT category FROM ledger WHERE player_id=@pid ORDER BY category", new { pid }).ToList();
    }

    public static List<LedgerSum> Summary(int pid, int fromDay)
    {
        using var c = Db.Open();
        return c.Query<LedgerSum>(@"SELECT category, CAST(SUM(amount) AS INTEGER) total FROM ledger
            WHERE player_id=@pid AND game_day>=@fromDay AND category<>'SALDO_AWAL' GROUP BY category ORDER BY total", new { pid, fromDay }).ToList();
    }

    public static List<LedgerEntry> TripReport(int pid, int tripId)
    {
        using var c = Db.Open();
        return c.Query<LedgerEntry>(@"SELECT id, game_day, created_at, category, note, trip_id, amount, balance_after
            FROM ledger WHERE player_id=@pid AND trip_id=@tripId ORDER BY id", new { pid, tripId }).ToList();
    }

    public static TripInfo? TripInfo(int pid, int tripId)
    {
        using var c = Db.Open();
            return c.QueryFirstOrDefault<TripInfo>(@"SELECT t.status, k.origin, k.dest, k.kind, k.monthly_pay, k.trips_per_month, t.wear_est, t.overhead_est
            FROM trips t JOIN contracts k ON k.id=t.contract_id WHERE t.id=@tripId AND t.player_id=@pid", new { pid, tripId });
    }

    // (jumlah seluruh ledger, kas sebenarnya)
    public static (long sum, long cash) Reconcile(int pid)
    {
        using var c = Db.Open();
        long sum = c.ExecuteScalar<long>("SELECT CAST(COALESCE(SUM(amount),0) AS INTEGER) FROM ledger WHERE player_id=@pid", new { pid });
        long cash = c.ExecuteScalar<long>("SELECT cash FROM players WHERE id=@pid", new { pid });
        return (sum, cash);
    }
}