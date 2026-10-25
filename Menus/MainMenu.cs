using Nusantara.Models;
using Nusantara.Services;

namespace Nusantara.Menus;

public static class MainMenu
{
    public static void ShowEvents(List<string> msgs)
    {
        if (msgs.Count == 0) return;
        Ui.Section("KEJADIAN TERBARU");
        foreach (var m in msgs) Console.WriteLine("  > " + m);
    }

    public static void ShowDashboard(int pid, Player p)
    {
        int day = PlayerSvc.Day(p);
        int trucks = FleetSvc.Fleet(pid).Count;
        var fin = FinanceSvc.Preview(pid);

        Ui.Title("Nusantara Logistics Manager");
        Console.WriteLine($"  Perusahaan : {p.Name} ({p.Legal})");
        
        Console.WriteLine($"  Kas        : {Rules.Rp(p.Cash)}");
        Console.WriteLine($"  Rating     : {p.Stars:0.0} bintang (reputasi {p.Reputation})");
        Console.WriteLine($"  Armada     : {trucks}/{FleetSvc.MaxTrucks(p.Tier)} truk");
        Console.WriteLine($"  Tutup buku : hari {fin.NextDay} ({fin.DaysLeft} hari lagi), tagihan sekitar {Rules.Rp(fin.MinNeeded)}");
        long reserve = FinanceSvc.OperatingReserve(p, fin);
        Console.WriteLine($"  Cadangan operasi disarankan : {Rules.Rp(reserve)}" + (p.Cash < reserve ? "   (kas di bawah cadangan)" : ""));
        if (fin.NoIncome.EndCash < 0)
            Console.WriteLine("  !! PERINGATAN: tanpa pemasukan baru, kas TIDAK cukup untuk tutup buku (menu 12).");

        var events = WorldSvc.Active(pid, day);
        if (events.Count > 0)
        {
            Ui.Section("BERITA AKTIF");
            foreach (var e in events) Console.WriteLine($"  [BERITA] {e.Title}");
        }
        var latest = NewsSvc.Latest(3);
        if(latest.Count > 0) { Ui.Section("BERITA TERBARU"); 
        foreach (var n in latest)
        Console.WriteLine($" [{n.Category}]{n.Headline}");}
        var hint = TutorialSvc.Hint(pid);
        if (hint != null)
        {
            Ui.Section("PANDUAN");
            Console.WriteLine("  " + hint);
        }

        Ui.Options(new[]
        {
            "Armada",
            "Dealer",
            "Servis",
            "Supir",
            "Kontrak & Berangkat",
            "Perjalanan",
            "Berita & Sosmed",
            "Bank & Asuransi",
            "Pool / Tier / Manajer",
            "Toko",
            "Buku Kas & Laporan",
            "Keuangan & Tutup Buku",
            "Panduan Pemula"
        }, "Kembali ke slot simpan");
        Console.WriteLine("  [P] Jeda/Lanjut waktu   [S] Simpan + cadangan   [V] Kecepatan waktu");
    }
}