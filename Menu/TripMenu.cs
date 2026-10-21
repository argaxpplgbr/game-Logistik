using Nusantara.Services;

namespace Nusantara.Menus;

public static class TripMenu
{
    public static void Run(int pid)
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
        Ui.Pause();
    }
}