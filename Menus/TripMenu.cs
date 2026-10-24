using Nusantara.Services;

namespace Nusantara.Menus;

public static class TripMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            Ui.Title("Perjalanan");

            Ui.Section("Sedang berjalan");
            var running = TripSvc.Running(pid);
            if (running.Count == 0) Ui.Empty("Tidak ada truk yang sedang jalan.");
            foreach (var t in running)
            {
                Console.WriteLine();
                Console.WriteLine($"Trip #{t.Id} (truk {t.TruckId})");
                Console.WriteLine($"     Tiba   : {(t.ArriveAt - DateTime.UtcNow).TotalMinutes:0.0} menit lagi");
                Console.WriteLine($"     UJ     : {Rules.Rp(t.UjGiven)}");
                if (t.Notes != "") Console.WriteLine($"     Catatan: {t.Notes}");
            }

            Ui.Section("5 hasil terakhir");
            var history = TripSvc.History(pid);
            if (history.Count == 0) Ui.Empty("Belum ada riwayat.");
            foreach (var t in history)
            {
                Console.WriteLine();
                Console.WriteLine($"Trip #{t.Id}");
                foreach (var part in t.Result.Split(" | "))
                    Console.WriteLine("     " + part);
            }

            Console.WriteLine();
            Console.WriteLine("Perjalanan berjalan di background. Hasilnya muncul otomatis saat menu utama dimuat.");
            int id = Ui.Num("ID trip untuk laporan laba/rugi (0=kembali): ", 0);
            if (id <= 0) return;
            Report(pid, id);
        }
    }

    static void Report(int pid, int tripId)
    {
        var info = LedgerSvc.TripInfo(pid, tripId);
        if (info == null) { Console.WriteLine("Trip tidak ditemukan."); Ui.Pause(); return; }

        Ui.Section($"Laporan trip #{tripId}: {info.Origin} -> {info.Dest} ({info.Kind}, {info.Status})");
        var lines = LedgerSvc.TripReport(pid, tripId);
        if (lines.Count == 0) Ui.Empty("Belum ada catatan kas untuk trip ini (trip lama dari sebelum buku kas dibuat).");
        foreach (var l in lines)
            Console.WriteLine($"  Hari {l.GameDay,-4} {LedgerSvc.Label(l.Category),-26} {Accounting.Signed(l.Amount),20}");
        if (lines.Count > 0)
        {
            Ui.Line();
            Console.WriteLine($"  {"LABA/RUGI TRIP",-31} {Accounting.Signed(lines.Sum(x => x.Amount)),20}");
        }

                if (lines.Count > 0)
        {
            long cashNet = lines.Sum(x => x.Amount);
            long share = info.Kind == "DEDICATED" ? info.MonthlyPay / Math.Max(1, info.TripsPerMonth) : 0;
            Ui.Section("Ringkasan ekonomi (kas vs estimasi)");
            Console.WriteLine($"  Kas trip (buku kas)            : {Accounting.Signed(cashNet)}");
            if (share > 0) Console.WriteLine($"  Bagian bayaran borongan        : {Accounting.Signed(share)}   (cair bulanan, jika target tercapai)");
            if (info.WearEst > 0 || info.OverheadEst > 0)
            {
                Console.WriteLine($"  Estimasi keausan               : {Accounting.Signed(-info.WearEst)}   (cadangan servis, BUKAN transaksi kas)");
                long contrib = cashNet + share - info.WearEst;
                Console.WriteLine($"  Kontribusi ekonomi trip        : {Accounting.Signed(contrib)}");
                Console.WriteLine($"  Bagian overhead                : {Accounting.Signed(-info.OverheadEst)}   (alokasi biaya tetap bulanan)");
                Console.WriteLine($"  Hasil setelah overhead         : {Accounting.Signed(contrib - info.OverheadEst)}");
            }
            else Console.WriteLine("  Estimasi keausan/overhead      : belum ada (trip dari sebelum v4).");
        }
        
        if (info.Status != "DONE")
            Console.WriteLine("  Catatan: trip masih berjalan, pendapatan & pengembalian UJ baru tercatat saat tiba.");
        if (info.Kind == "DEDICATED")
            Console.WriteLine($"  Catatan: borongan dibayar bulanan lewat tutup buku (setara {Rules.Rp(info.MonthlyPay / Math.Max(1, info.TripsPerMonth))} per trip).");

        var reward = TutorialSvc.OnReportViewed(pid, tripId);
        if (reward != null) { Console.WriteLine(); Console.WriteLine(reward); }
        Ui.Pause();
    }
}