using Nusantara.Services;

namespace Nusantara.Menus;

public static class LedgerMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            Ui.Title("Buku Kas & Laporan");
            Ui.Options(new[]
            {
                "Riwayat terbaru (25 transaksi)",
                "Filter per kategori",
                "Ringkasan masuk/keluar per kategori",
                "Cek rekonsiliasi (jumlah ledger = kas?)"
            });

            switch (Ui.Ask("Pilih: "))
            {
                case "1": Show(LedgerSvc.Recent(pid, null, 25)); break;
                case "2": FilterByCategory(pid); break;
                case "3": Summary(pid); break;
                case "4": Reconcile(pid); break;
                case "0": return;
            }
        }
    }

    static void Show(List<LedgerEntry> list)
    {
        Ui.Section("Transaksi (terbaru di atas)");
        if (list.Count == 0) { Ui.Empty("Tidak ada catatan."); return; }
        foreach (var e in list)
        {
            string trip = e.TripId != null ? $" [trip #{e.TripId}]" : "";
            Console.WriteLine($"  H{e.GameDay,-4} {LedgerSvc.Label(e.Category),-26} {Accounting.Signed(e.Amount),20}  saldo {Rules.Rp(e.BalanceAfter),18}");
            if (e.Note != "") Console.WriteLine($"         {e.Note}{trip}");
        }
    }

    static void FilterByCategory(int pid)
    {
        var cats = LedgerSvc.Categories(pid);
        if (cats.Count == 0) { Ui.Empty("Belum ada catatan."); return; }
        int pick = Ui.Choose("Kategori:", cats.Select(LedgerSvc.Label).ToArray(), 1);
        Show(LedgerSvc.Recent(pid, cats[pick - 1], 25));
    }

    static void Summary(int pid)
    {
        var p = PlayerSvc.Get(pid); int day = PlayerSvc.Day(p);
        int scope = Ui.Choose("Periode:", new[] { "30 hari game terakhir", "Semua waktu" }, 1);
        var sums = LedgerSvc.Summary(pid, scope == 1 ? Math.Max(0, day - 30) : 0);

        Ui.Section("Ringkasan (tanpa saldo awal)");
        if (sums.Count == 0) { Ui.Empty("Tidak ada transaksi pada periode ini."); return; }
        foreach (var s in sums) Console.WriteLine($"  {LedgerSvc.Label(s.Category),-28} {Accounting.Signed(s.Total),20}");
        Ui.Line();
        Console.WriteLine($"  {"PERUBAHAN BERSIH",-28} {Accounting.Signed(sums.Sum(x => x.Total)),20}");
    }

    static void Reconcile(int pid)
    {
        var (sum, cash) = LedgerSvc.Reconcile(pid);
        Ui.Section("Rekonsiliasi");
        Console.WriteLine($"  Jumlah semua ledger : {Rules.Rp(sum)}");
        Console.WriteLine($"  Kas perusahaan      : {Rules.Rp(cash)}");
        Console.WriteLine(Accounting.Reconciles(sum, cash)
            ? "  HASIL: COCOK. Setiap perubahan kas bisa ditelusuri."
            : $"  HASIL: SELISIH {Accounting.Signed(cash - sum)}. Ada perubahan kas di luar buku kas, laporkan ke developer.");
        Ui.Pause();
    }
}