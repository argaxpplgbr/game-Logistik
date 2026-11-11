
using Nusantara.Models;
using Nusantara.Services;

namespace Nusantara.Menus;

public static class MainMenu
{
    public static void ShowEvents(List<string> msgs)
    {
        if (msgs.Count == 0) return;

        Ui.Section("KEJADIAN TERBARU");

        foreach (var m in msgs)
            Console.WriteLine("  > " + m);
    }

    public static void ShowDashboard(int pid, Player p)
    {
        int day = PlayerSvc.Day(p);
        int trucks = FleetSvc.Fleet(pid).Count;
        var fin = FinanceSvc.Preview(pid);
        var assessment = EconomicBalanceSvc.Assess(p, fin);

        Ui.Title("Nusantara Logistics Manager");

        Console.WriteLine($"  Perusahaan : {p.Name} ({p.Legal})");
        Console.WriteLine($"  Kas        : {Rules.Rp(p.Cash)}");
        Console.WriteLine($"  Rating     : {p.Stars:0.0} bintang (reputasi {p.Reputation})");
        Console.WriteLine($"  Armada     : {trucks}/{FleetSvc.MaxTrucks(p.Tier)} truk");

        Console.WriteLine(
            $"  Tutup buku : hari {fin.NextDay} ({fin.DaysLeft} hari lagi), " +
            $"tagihan sekitar {Rules.Rp(fin.MinNeeded)}");

        Console.WriteLine(
            $"  Cadangan operasi disarankan : {Rules.Rp(assessment.OperatingReserve)}");

        Console.WriteLine($"  Risiko kas  : {assessment.Risk}");

        Console.WriteLine(
            $"  Kemampuan biaya tetap : " +
            (double.IsInfinity(assessment.RunwayMonths)
                ? "Tidak terbatas"
                : $"{assessment.RunwayMonths:0.0} bulan"));

        Console.WriteLine(
            $"  Simulasi kas tutup buku : {Rules.Rp(fin.Current.EndCash)}");

        Console.WriteLine("  Saran keuangan: " + assessment.Advice);

        if (fin.NoIncome.EndCash < 0)
        {
            Console.WriteLine(
                "  !! PERINGATAN: tanpa pemasukan borongan baru, " +
                "kas berisiko tidak cukup untuk tutup buku.");
        }

        var events = WorldSvc.Active(pid, day);

        if (events.Count > 0)
        {
            Ui.Section("BERITA AKTIF");

            foreach (var e in events)
                Console.WriteLine($"  [BERITA] {e.Title}");
        }

        var latest = NewsSvc.Latest(3);

        if (latest.Count > 0)
        {
            Ui.Section("BERITA TERBARU");

            foreach (var n in latest)
                Console.WriteLine($"  [{n.Category}] {n.Headline}");
        }

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
            "Panduan Pemula",
            "Pasar Bersama (Kontrak & Pesaing)",
            "Komunikasi Antar Perusahaan",
            "Pusat Intelijen Kompetitor"
        }, "Kembali ke slot simpan");

        Console.WriteLine(
            "  [P] Jeda/Lanjut waktu   [S] Simpan + cadangan   [V] Kecepatan waktu");
    }
}
