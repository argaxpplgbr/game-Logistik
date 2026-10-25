using System.Text;
using System.Text.Json;
using Dapper;
using Nusantara.Database;

namespace Nusantara.Services;

// Metrik periode, disimpan di tabel digest supaya periode berikutnya bisa dibandingkan.
public class DigestMetrics
{
    public int TripsOk { get; set; }
    public int TripsFailed { get; set; }
    public long TripNet { get; set; }
    public long LedgerNet { get; set; }
}

class EvRow
{
    public long EventId { get; set; }
    public string Type { get; set; } = "";
    public string PayloadJson { get; set; } = "";
}

// Digest tiap 5 hari simulasi. Semua ANGKA datang dari buku kejadian dan buku kas.
// Bagian ANALISIS hanya aturan sederhana di atas angka itu. Digest tidak mengubah state game
// (hanya menulis ringkasannya sendiri) dan idempotent: satu periode hanya dibuat sekali.
public static class DigestSvc
{
    public const int RuleVersion = 1;
    public const int PeriodDays = 5;

    // Buat digest untuk periode yang sudah jatuh tempo dan belum ada. Mengembalikan pesan singkat.
    public static List<string> RunDue(int pid)
    {
        var log = new List<string>();
        var p = PlayerSvc.Get(pid);
        int day = PlayerSvc.Day(p);
        int latestDue = day / PeriodDays * PeriodDays;
        if (latestDue < PeriodDays) return log;

        int last;
        using (var c = Db.Open())
            last = c.ExecuteScalar<int?>("SELECT MAX(period_end) FROM digest") ?? 0;

        int next = last == 0 ? latestDue : last + PeriodDays;
        int guard = 0;
        while (next <= latestDue && guard++ < 6)
        {
            var msg = Build(pid, next);
            if (msg != "") log.Add(msg);
            next += PeriodDays;
        }
        return log;
    }

    // Periode berakhir di hari periodEnd (tidak termasuk): mencakup hari periodEnd-5 .. periodEnd-1.
    // Mengembalikan "" kalau digest periode itu sudah ada.
    public static string Build(int pid, int periodEnd)
    {
        int start = periodEnd - PeriodDays, end = periodEnd - 1;
        var p = PlayerSvc.Get(pid);
        int today = PlayerSvc.Day(p);

        using var c = Db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM digest WHERE period_end=@periodEnd", new { periodEnd }) > 0) return "";

        // ---- Fakta dari buku kejadian ----
        var rows = c.Query<EvRow>(@"SELECT event_id, type, payload_json FROM event_ledger
            WHERE sim_day BETWEEN @start AND @end ORDER BY sequence", new { start, end }).ToList();

        int ok = 0, failed = 0, incidents = 0, bought = 0, hired = 0, dispatched = 0, seized = 0;
        long tripNet = 0;
        var worldTitles = new List<string>();
        foreach (var r in rows)
        {
            using var doc = JsonDocument.Parse(r.PayloadJson);
            var e = doc.RootElement;
            switch (r.Type)
            {
                case "TRIP_COMPLETED":
                    if (NewsComposer.Flag(e, "ok")) ok++; else failed++;
                    if (NewsComposer.Flag(e, "incident")) incidents++;
                    tripNet += NewsComposer.Num(e, "net");
                    break;
                case "TRIP_DISPATCHED": dispatched++; break;
                case "TRUCK_BOUGHT": bought++; break;
                case "DRIVER_HIRED": hired++; break;
                case "TRUCK_SEIZED": seized++; break;
                case "WORLD_EVENT": worldTitles.Add(NewsComposer.Str(e, "title")); break;
            }
        }

        // ---- Fakta dari buku kas ----
        long ledgerNet = c.ExecuteScalar<long>(@"SELECT CAST(COALESCE(SUM(amount),0) AS INTEGER) FROM ledger
            WHERE game_day BETWEEN @start AND @end AND category<>'SALDO_AWAL'", new { start, end });
        long endCash = c.ExecuteScalar<long?>("SELECT balance_after FROM ledger WHERE game_day<=@end ORDER BY id DESC LIMIT 1", new { end }) ?? p.Cash;

        var metrics = new DigestMetrics { TripsOk = ok, TripsFailed = failed, TripNet = tripNet, LedgerNet = ledgerNet };
        var prevJson = c.ExecuteScalar<string?>("SELECT metrics_json FROM digest ORDER BY period_end DESC LIMIT 1");
        DigestMetrics? prev = null;
        if (prevJson != null)
        {
            try { prev = JsonSerializer.Deserialize<DigestMetrics>(prevJson); } catch { prev = null; }
        }

        // ---- Susun teks ----
        var sb = new StringBuilder();
        sb.AppendLine($"ANALISIS 5 HARI: hari {start} sampai {end}");
        sb.AppendLine("[Fakta, dari buku kejadian dan buku kas]");
        sb.AppendLine($"  - Pengiriman: {dispatched} berangkat, {ok} selesai berhasil, {failed} gagal; laba/rugi trip {Accounting.Signed(tripNet)}.");
        sb.AppendLine($"  - Arus kas bersih periode {Accounting.Signed(ledgerNet)}; kas akhir periode {Rules.Rp(endCash)}.");
        sb.AppendLine($"  - Armada +{bought} truk, supir direkrut {hired}, insiden {incidents}, truk disita bank {seized}.");
        if (worldTitles.Count > 0)
            foreach (var w in worldTitles) sb.AppendLine($"  - Kejadian dunia: {w}");
        else
            sb.AppendLine("  - Kejadian dunia: tidak ada.");

        sb.AppendLine("[Analisis, kesimpulan berbasis aturan, bukan fakta baru]");
        if (prev == null)
            sb.AppendLine("  - Tren: belum ada periode pembanding.");
        else
        {
            string tripTrend = ok > prev.TripsOk ? $"naik dari {prev.TripsOk} ke {ok}" : ok < prev.TripsOk ? $"turun dari {prev.TripsOk} ke {ok}" : $"tetap {ok}";
            string cashTrend = ledgerNet > prev.LedgerNet ? "membaik" : ledgerNet < prev.LedgerNet ? "memburuk" : "sama";
            sb.AppendLine($"  - Tren: pengiriman berhasil {tripTrend}; arus kas bersih {cashTrend} dibanding periode lalu.");
        }

        var risks = new List<string>();
        if (failed > 0) risks.Add($"{failed} pengiriman gagal; periksa kondisi rem/ban dan pilihan keamanan rute.");
        if (seized > 0) risks.Add("bank sudah menyita truk; prioritaskan cicilan.");
        try
        {
            var fin = FinanceSvc.Preview(pid);
            if (p.Cash < fin.MinNeeded)
                risks.Add($"kas saat ini {Rules.Rp(p.Cash)} di bawah perkiraan tagihan tutup buku {Rules.Rp(fin.MinNeeded)}.");
        }
        catch { /* preview gagal: lewati aturan ini */ }
        if (risks.Count == 0) sb.AppendLine("  - Risiko: tidak ada yang menonjol menurut aturan analisis.");
        else foreach (var r in risks) sb.AppendLine($"  - Risiko: {r}");

        var boosted = WorldSvc.Active(pid, today).Where(x => x.Cargos != "" && x.Magnitude > 1.0).ToList();
        int openSpot = ContractSvc.Open(pid).Count(k => k.Kind == "SPOT" && k.Status == "OPEN");
        foreach (var b in boosted)
            sb.AppendLine($"  - Peluang: tarif kargo {b.Cargos.Replace(",", "/")} naik x{b.Magnitude:0.0#} sampai hari {b.EndDay}.");
        sb.AppendLine($"  - Peluang: {openSpot} kontrak spot terbuka di papan kontrak.");

        string output = sb.ToString().TrimEnd();
        string eventIds = JsonSerializer.Serialize(rows.Select(r => r.EventId).ToArray());

        // ---- Simpan (satu transaksi; UNIQUE period_end menjaga idempotensi) ----
        using var tx = c.BeginTransaction();
        c.Execute(@"INSERT INTO digest(period_start,period_end,created_day,rule_version,event_ids,metrics_json,output)
            VALUES(@start,@periodEnd,@today,@rv,@eventIds,@mj,@output)",
            new { start, periodEnd, today, rv = RuleVersion, eventIds, mj = JsonSerializer.Serialize(metrics), output }, tx);
        long did = c.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: tx);
        c.Execute(@"INSERT INTO news_item(event_id,category,headline,body,confidence,created_day,created_minute,digest_id,template_version)
            VALUES(NULL,'ANALISIS',@h,@output,'ANALISIS',@today,0,@did,@tv)",
            new { h = $"Analisis 5 hari: hari {start} sampai {end}", output, today, did, tv = NewsComposer.TemplateVersion }, tx);
        tx.Commit();

        return $"[Analisis] Ringkasan hari {start}-{end} tersedia di menu Berita (opsi 3).";
    }
}