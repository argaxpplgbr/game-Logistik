using Nusantara.Models;
using Nusantara.Services;

namespace Nusantara.Menus;

public static class ContractMenu
{
    public static void Run(int pid)
    {
        var p = PlayerSvc.Get(pid);
        var list = ContractSvc.Open(pid);

        Ui.Title("Kontrak & Berangkat");
        ShowContracts(list);
        if (list.Count == 0) { Ui.Empty("Belum ada kontrak tersedia."); Ui.Pause(); return; }

        Console.WriteLine();
        int id = Ui.Num("ID kontrak (0=batal): ");
        if (id <= 0) return;

        var ct = list.FirstOrDefault(x => x.Id == id);
        if (ct == null) { Console.WriteLine("Kontrak tidak ada."); Ui.Pause(); return; }

        if (ct.Kind == "DEDICATED" && ct.Status == "OPEN")
        {
            if (Ui.Yes("Terima kontrak borongan ini? (y/n): "))
                Console.WriteLine(ContractSvc.Accept(pid, id));
            Ui.Pause();
            return;
        }

        Dispatch(pid, p, ct);
        Ui.Pause();
    }

    static void ShowContracts(List<Contract> list)
    {
        Ui.Section("Kontrak Eceran (Spot Charter)");
        var spot = list.Where(k => k.Kind == "SPOT").ToList();
        if (spot.Count == 0) Ui.Empty("Tidak ada.");
        foreach (var k in spot)
        {
            Console.WriteLine();
            Console.WriteLine($"[{k.Id}] {k.Origin} -> {k.Dest}");
            Console.WriteLine($"     Kargo   : {k.CargoCode}, {k.WeightKg:N0} kg");
            Console.WriteLine($"     Bayaran : {Rules.Rp(k.Fee)} sekali jalan");
            Console.WriteLine($"     Syarat  : reputasi >= {k.RepRequired}   (status {k.Status})");
        }

        Ui.Section("Kontrak Borongan (Dedicated Freight)");
        var ded = list.Where(k => k.Kind == "DEDICATED").ToList();
        if (ded.Count == 0) Ui.Empty("Tidak ada.");
        foreach (var k in ded)
        {
            Console.WriteLine();
            Console.WriteLine($"[{k.Id}] {k.Client}: {k.Origin} -> {k.Dest}");
            Console.WriteLine($"     Kargo   : {k.CargoCode}, {k.WeightKg:N0} kg per trip");
            Console.WriteLine($"     Bayaran : {Rules.Rp(k.MonthlyPay)} per bulan");
            Console.WriteLine($"     Target  : {k.TripsPerMonth} trip/bulan selama {k.MonthsTotal} bulan ({k.TripsDoneMonth} trip bulan ini)");
            Console.WriteLine($"     Syarat  : reputasi >= {k.RepRequired}   (status {k.Status})");
        }
    }

    static void Dispatch(int pid, Player p, Contract ct)
    {
        Ui.Section("Truk yang siap jalan");
        var idle = FleetSvc.Fleet(pid).Where(x => x.Status == "IDLE").ToList();
        if (idle.Count == 0) { Ui.Empty("Tidak ada truk IDLE."); return; }
        foreach (var t in idle)
            Console.WriteLine($"  Truk [{t.Id}] {t.ModelName} / bak {t.BodyCode}  |  muat {t.Payload:N0} kg  |  supir {(t.DriverId == null ? "-" : t.DriverId.ToString())}");

        Console.WriteLine();
        int tid = Ui.Num("ID truk: ");

        int u = Ui.Choose("Uang Jalan (UJ):",
            new[] { "Pas-pasan (0.9x)", "Standar (1.0x)", "Longgar (1.15x)", "Sangat longgar (1.3x)" }, 2);
        double mult = u switch { 1 => 0.9, 3 => 1.15, 4 => 1.3, _ => 1.0 };

        int ol = Ui.Num("\nOverload % (0 / 20 / 40) [0]: ", 0);
        ol = ol >= 40 ? 40 : ol >= 20 ? 20 : 0;

        string pol = "DENDA";
        if (ol > 0)
        {
            int k = Ui.Choose("Jika kena jembatan timbangan:", new[] { "Bayar denda", "Jasa pengawalan", "Bongkar muatan" });
            pol = k switch { 2 => "KAWAL", 3 => "BONGKAR", _ => "DENDA" };
        }

        int s = Ui.Choose("Keamanan jalur:", new[] { "Tanpa pengamanan", "Uang keamanan Ormas", "Wajib istirahat di Rest Area" });
        string sec = s switch { 2 => "ORMAS", 3 => "RESTAREA", _ => "NONE" };

        int f = Ui.Choose("Jika solar langka:", new[] { "Antre di SPBU", "Beli eceran (mahal)" });
        string fm = f == 2 ? "ECERAN" : "ANTRE";

        var pl = TripSvc.Build(p, tid, ct.Id, mult, ol, pol, sec, fm);
        if (pl.Error != null) { Console.WriteLine("\n" + pl.Error); return; }

        ShowPlan(pl);
        if (Ui.Yes("\nBerangkat? (y/n): "))
            Console.WriteLine("\n" + TripSvc.Dispatch(p, pl));
    }

    static void ShowPlan(Plan pl)
    {
        Ui.Section("Rencana perjalanan");
        Console.WriteLine("  Rute        : " + string.Join(" > ", pl.Path.Select(e => e.Name)));
        Console.WriteLine($"  Jarak       : darat {pl.RoadKm:0} km, laut {pl.FerryKm:0} km");
        Console.WriteLine($"  Estimasi    : {pl.Minutes / Rules.DevSpeed:0.#} menit real-time");

        Ui.Section("Biaya");
        Console.WriteLine($"  BBM         : {pl.Liters:0} L ({pl.FuelType}) = {Rules.Rp(pl.FuelCost)}");
        Console.WriteLine($"  Tol         : {Rules.Rp(pl.Toll)}");
        Console.WriteLine($"  Tiket kapal : {Rules.Rp(pl.Ferry)}");
        Console.WriteLine($"  Makan/parkir: {Rules.Rp(pl.Meal)}");
        Console.WriteLine($"  Pengawalan  : {Rules.Rp(pl.Escort)}");
        Console.WriteLine($"  Keamanan    : {Rules.Rp(pl.Security)}");

        Ui.Section("Uang Jalan");
        Console.WriteLine($"  UJ standar   : {Rules.Rp(pl.UjStd)}");
        Console.WriteLine($"  UJ diberikan : {Rules.Rp(pl.UjGiven)}");
        Console.WriteLine($"  Supir minta UJ >= {pl.Driver.UjExp:0.00}x");
        if (pl.UjMult + 1e-9 < pl.Driver.UjExp)
            Console.WriteLine("  PERINGATAN: UJ lebih rendah dari permintaan supir, moralnya akan turun.");

        if (pl.Notes.Count > 0)
        {
            Ui.Section("Catatan");
            foreach (var n in pl.Notes) Console.WriteLine("  - " + n);
        }
    }
}