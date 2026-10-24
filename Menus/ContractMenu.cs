using Nusantara.Models;
using Nusantara.Services;

namespace Nusantara.Menus;

public static class ContractMenu
{
    public static void Run(int pid)
    {
        var p = PlayerSvc.Get(pid);
        var all = ContractSvc.Open(pid);

        Ui.Title("Kontrak & Berangkat");
        if (all.Count == 0) { Ui.Empty("Belum ada kontrak tersedia."); Ui.Pause(); return; }

        int f = Ui.Choose("Filter papan kontrak:",
            new[] { "Semua", "Hanya yang layak dijalankan sekarang", "Spot charter saja", "Borongan saja" }, 1);
        Console.WriteLine("\n(Menghitung kelayakan tiap kontrak...)");
        var rows = all.Select(k => (k, fz: ContractPreview.Evaluate(p, k))).ToList();
        if (f == 2) rows = rows.Where(r => r.fz.Ok).ToList();
        else if (f == 3) rows = rows.Where(r => r.k.Kind == "SPOT").ToList();
        else if (f == 4) rows = rows.Where(r => r.k.Kind == "DEDICATED").ToList();

        if (rows.Count == 0) { Ui.Empty("Tidak ada kontrak yang cocok dengan filter."); Ui.Pause(); return; }
        ShowBoard(rows);

        Console.WriteLine();
        int id = Ui.Num("ID kontrak (0=batal): ");
        if (id <= 0) return;

        var row = rows.FirstOrDefault(x => x.k.Id == id);
        if (row.k == null) { Console.WriteLine("Kontrak tidak ada di daftar yang tampil."); Ui.Pause(); return; }
        var ct = row.k; var fz = row.fz;

        ShowPreview(ct, fz);

        if (ct.Kind == "DEDICATED" && ct.Status == "OPEN")
        {
            Ui.Section("Armada yang dibutuhkan kontrak ini");
            foreach (var w in ContractSvc.FleetCheck(pid, ct)) Console.WriteLine("  " + w);

            string q = fz.Ok ? "\nTerima kontrak borongan ini? (y/n): "
                             : "\nKontrak ini BELUM layak untuk armadamu. Tetap terima dan tanggung risiko potongan? (y/n): ";
            if (Ui.Yes(q))
                Console.WriteLine(ContractSvc.Accept(pid, id));
            Ui.Pause();
            return;
        }

        if (!fz.Ok)
        {
            Console.WriteLine("\nKontrak ini belum bisa dijalankan sekarang, lihat alasan di atas.");
            Ui.Pause();
            return;
        }

        Dispatch(pid, ct);
        Ui.Pause();
    }

    static void ShowBoard(List<(Contract k, Feasibility fz)> rows)
    {
        Ui.Section("Kontrak Eceran (Spot Charter)");
        var spot = rows.Where(r => r.k.Kind == "SPOT").ToList();
        if (spot.Count == 0) Ui.Empty("Tidak ada.");
        foreach (var (k, fz) in spot)
        {
            Console.WriteLine();
            Console.WriteLine($"[{k.Id}] {k.Origin} -> {k.Dest}   {Flag(fz)}");
            Console.WriteLine($"     Kargo   : {k.CargoCode}, {k.WeightKg:N0} kg");
            Console.WriteLine($"     Bayaran : {Rules.Rp(k.Fee)} sekali jalan");
            Console.WriteLine($"     Syarat  : reputasi >= {k.RepRequired}   (status {k.Status})");
        }

        Ui.Section("Kontrak Borongan (Dedicated Freight)");
        var ded = rows.Where(r => r.k.Kind == "DEDICATED").ToList();
        if (ded.Count == 0) Ui.Empty("Tidak ada.");
        foreach (var (k, fz) in ded)
        {
            Console.WriteLine();
            Console.WriteLine($"[{k.Id}] {k.Client}: {k.Origin} -> {k.Dest}   {Flag(fz)}");
            Console.WriteLine($"     Kargo   : {k.CargoCode}, {k.WeightKg:N0} kg" + (k.VehicleClass != "" ? $"   |  kelas {k.VehicleClass}" : ""));
            Console.WriteLine($"     Bayaran : {Rules.Rp(k.MonthlyPay)} per bulan");
            Console.WriteLine($"     Target  : {k.TripsPerMonth} trip/bulan selama {k.MonthsTotal} bulan ({k.TripsDoneMonth} trip bulan ini)");
            Console.WriteLine($"     Syarat  : reputasi >= {k.RepRequired}   (status {k.Status})");
        }
    }

    static string Flag(Feasibility fz) =>
        fz.Ok ? (fz.Best != null ? $"[LAYAK, kontribusi ~{Rules.Rp(fz.Best.ContribEst)}]" : "[LAYAK]") : "[BELUM LAYAK]";
    static void ShowPreview(Contract k, Feasibility fz)
    {
        Ui.Section($"Preview kontrak [{k.Id}]");
        Console.WriteLine($"  Status kelayakan : {(fz.Ok ? "LAYAK dijalankan dengan kondisi sekarang" : "BELUM LAYAK")}");
        foreach (var r in fz.Reasons) Console.WriteLine("  - " + r);
        ShowQuote(k);

        if (fz.Best != null)
        {
            var b = fz.Best;
            Console.WriteLine($"  Truk terbaik     : [{b.Truck.Id}] {b.Truck.ModelName}, supir {b.Driver.Name} ({fz.FitTrucks} truk cocok)");
            if (k.Kind == "SPOT")
            {
                Console.WriteLine($"  Pendapatan kotor : {Rules.Rp(b.IncomeEst)}");
                Console.WriteLine($"  Komisi supir     : {Rules.Rp(b.CommissionEst)}");
                Console.WriteLine($"  UJ diberikan     : {Rules.Rp(b.UjGiven)}   (biaya wajib perkiraan {Rules.Rp(b.CostEst)})");
                Console.WriteLine($"  Kas bersih trip  : {Rules.Rp(b.NetOk)}   (margin {Accounting.Margin(b.NetOk, b.IncomeEst):P0})");
            }
            else
            {
                Console.WriteLine($"  Pendapatan       : setara {Rules.Rp(b.MonthlyShare)} per trip (cair bulanan)");
                Console.WriteLine($"  Biaya per trip   : UJ {Rules.Rp(b.UjGiven)}, biaya wajib perkiraan {Rules.Rp(b.CostEst)}");
                Console.WriteLine($"  Kas trip         : {Rules.Rp(b.NetOk)}   (belum ada pemasukan kas, bayaran cair saat tutup buku)");
            }
            ShowEconomy(b, k.Kind == "SPOT");
            string tc = EconomySvc.ClassForTruck(b.Truck.MaxLoad);
            if (k.VehicleClass != "" && tc != k.VehicleClass)
                Console.WriteLine($"  Catatan kelas    : truk kelas {tc}, kontrak dibayar sebagai kelas {k.VehicleClass} (tarif tidak naik walau truk lebih besar).");
            Console.WriteLine("  Asumsi           : UJ standar, tanpa overload, tanpa kejadian acak (pungli, mogok, kecelakaan).");
        }

        if (k.Kind == "DEDICATED")
        {
            Ui.Section("Tabel bayaran borongan");
            Console.WriteLine($"  Target {k.TripsPerMonth} trip/bulan, bayaran penuh {Rules.Rp(k.MonthlyPay)}/bulan, {k.MonthsTotal} bulan.");
            for (int d = k.TripsPerMonth - 1; d >= Math.Max(0, k.TripsPerMonth - 3); d--)
                Console.WriteLine("  Jika " + Accounting.DedicatedFormula(k.MonthlyPay, k.TripsPerMonth, d));
            Console.WriteLine("  Trip yang masih berjalan saat tutup buku tidak dihitung untuk target.");
        }
    }

        // Rincian kuotasi yang sudah dikunci pada kontrak
    static void ShowQuote(Contract k)
    {
        if (k.VehicleClass == "") { Console.WriteLine("  Kuotasi          : kontrak lama (sebelum v4), nilai sesuai kesepakatan awal."); return; }
        var band = EconomySvc.Band(k.VehicleClass);
        Console.WriteLine($"  Kuotasi dikunci  : kelas {band.Name}, jarak tertagih {k.QuoteKm:0} km, tarif {Rules.Rp(k.QuoteRate)}/km, faktor pasar x{k.QuoteMult:0.00}");
        Console.WriteLine($"  Minimum dispatch : {Rules.Rp(band.MinDispatch)}" + (band.Assumed ? "   (tarif kelas ini asumsi balancing, bukan kutipan pasar)" : ""));
    }

    // Keausan & overhead: angka ekonomi, BUKAN uang yang langsung keluar dari kas
    static void ShowEconomy(Plan b, bool spot)
    {
        Console.WriteLine($"  Cadangan keausan : {Rules.Rp(b.WearEst)}   (estimasi servis, BUKAN pengeluaran kas sebelum servis dibayar)");
        Console.WriteLine($"  Kontribusi trip  : {Rules.Rp(b.ContribEst)}" + (spot ? "" : "   (jika target trip bulanan tercapai)"));
        Console.WriteLine($"  Bagian overhead  : {Rules.Rp(b.OverheadEst)} per trip   (biaya tetap bulanan dibagi {EconomySvc.PlannedTripsPerTruckMonth} trip per truk)");
        Console.WriteLine($"  Hasil setelah overhead : {Rules.Rp(b.ContribEst - b.OverheadEst)}");
    }

    // Tampilkan dampak tiap pilihan SEBELUM pemain memilih
    static bool ShowImpact(Player p, int tid, Contract ct)
    {
        Ui.Section("Dampak pilihan Uang Jalan (tanpa overload)");
        foreach (var (m, label) in new[] { (0.9, "Pas-pasan 0.9x"), (1.0, "Standar 1.0x"), (1.15, "Longgar 1.15x"), (1.3, "Sangat longgar 1.3x") })
        {
            var x = TripSvc.Build(p, tid, ct.Id, m, 0, "DENDA", "AUTO", "ANTRE");
            if (x.Error != null) { Console.WriteLine("\n" + x.Error); return false; }
            string warn = x.UjGiven < x.CostEst ? "  <- UJ di bawah biaya wajib, supir nombok" : "";
            Console.WriteLine($"  {label,-18} UJ {Rules.Rp(x.UjGiven),16}   bersih jika lancar {Rules.Rp(x.NetOk),16}{warn}");
        }

        if (ct.Kind == "SPOT")
        {
            Ui.Section("Dampak pilihan overload (UJ standar)");
            foreach (var ol in new[] { 0, 20, 40 })
            {
                var x = TripSvc.Build(p, tid, ct.Id, 1.0, ol, "DENDA", "AUTO", "ANTRE");
                if (x.Error != null) { Console.WriteLine($"  Overload {ol,2}%: tidak bisa ({x.Error})"); continue; }
                Console.WriteLine($"  Overload {ol,2}%  pendapatan {Rules.Rp(x.IncomeEst),16}   bersih {Rules.Rp(x.NetOk),16}   kena timbangan {x.PTilang:P0}");
            }
        }
        return true;
    }

    static void Dispatch(int pid, Contract ct)
    {
        var p = PlayerSvc.Get(pid);
        Ui.Section("Truk yang siap jalan");
        var idle = FleetSvc.Fleet(pid).Where(x => x.Status == "IDLE").ToList();
        if (idle.Count == 0) { Ui.Empty("Tidak ada truk IDLE."); return; }
        foreach (var t in idle)
            Console.WriteLine($"  Truk [{t.Id}] {t.ModelName} / bak {t.BodyCode}  |  muat {t.Payload:N0} kg  |  supir {(t.DriverId == null ? "-" : t.DriverId.ToString())}");

        Console.WriteLine();
        int tid = Ui.Num("ID truk: ");
        if (!idle.Any(x => x.Id == tid)) { Console.WriteLine("\nTruk itu tidak ada di daftar yang siap jalan."); return; }
        if (!ShowImpact(p, tid, ct)) return;

        int u = Ui.Choose("Uang Jalan (UJ):",
            new[] { "Pas-pasan (0.9x)", "Standar (1.0x)", "Longgar (1.15x)", "Sangat longgar (1.3x)" }, 2);
        double mult = u switch { 1 => 0.9, 3 => 1.15, 4 => 1.3, _ => 1.0 };

        // Overload hanya ada di kontrak spot (borongan dibayar tetap per bulan, tidak ada bonus muatan lebih)
        int ol = 0; string pol = "DENDA";
        if (ct.Kind == "SPOT")
        {
            int o = Ui.Choose("Overload (bonus bayaran, tapi bisa kena jembatan timbangan):",
                new[] { "Tanpa overload", "Overload 20%", "Overload 40%" }, 1);
            ol = o == 2 ? 20 : o == 3 ? 40 : 0;

            if (ol > 0)
            {
                int k = Ui.Choose("Jika kena jembatan timbangan:", new[] { "Bayar denda", "Jasa pengawalan", "Bongkar muatan" });
                pol = k switch { 2 => "KAWAL", 3 => "BONGKAR", _ => "DENDA" };
            }
        }
        else Console.WriteLine("\n(Kontrak borongan: overload tidak tersedia karena bayaran sudah tetap per bulan.)");

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
        Console.WriteLine($"  BBM            : {pl.Liters:0} L ({pl.FuelType}) = {Rules.Rp(pl.FuelCost)}");
        Console.WriteLine($"  Tol            : {Rules.Rp(pl.Toll)}");
        Console.WriteLine($"  Tiket kapal    : {Rules.Rp(pl.Ferry)}");
        Console.WriteLine($"  Makan/parkir   : {Rules.Rp(pl.Meal)}");
        Console.WriteLine($"  Cadangan rawan : {Rules.Rp(pl.Reserve)}   (masuk hitungan UJ standar)");
        Console.WriteLine($"  Pengawalan     : {Rules.Rp(pl.Escort)}");
        Console.WriteLine($"  Keamanan       : {Rules.Rp(pl.Security)}");

        Ui.Section("Uang Jalan");
        Console.WriteLine($"  UJ standar   : {Rules.Rp(pl.UjStd)}   = (BBM + tol + kapal + makan + cadangan rawan) x 1,1");
        Console.WriteLine($"  UJ diberikan : {Rules.Rp(pl.UjGiven)}   (dibayar saat berangkat, sisa dikembalikan sesuai kejujuran supir)");
        Console.WriteLine($"  Supir minta UJ >= {pl.Driver.UjExp:0.00}x");
        if (pl.UjMult + 1e-9 < pl.Driver.UjExp)
            Console.WriteLine("  PERINGATAN: UJ lebih rendah dari permintaan supir, moralnya akan turun.");
        long nombok = Accounting.UjShortfall(pl.UjGiven, pl.CostEst);
        if (nombok > 0)
            Console.WriteLine($"  PERINGATAN: UJ lebih kecil dari biaya wajib {Rules.Rp(pl.CostEst)}. Supir nombok {Rules.Rp(nombok)} dan moral turun.");

        Ui.Section("Perkiraan hasil");
        if (pl.Contract.Kind == "SPOT")
        {
            Console.WriteLine($"  Pendapatan kotor : {Rules.Rp(pl.IncomeEst)}" + (pl.OverloadPct > 0 ? $"  (termasuk bonus overload {pl.OverloadPct}%)" : ""));
            Console.WriteLine($"  Komisi supir     : {Rules.Rp(pl.CommissionEst)}");
        }
        else
            Console.WriteLine($"  Pendapatan       : dibayar bulanan lewat tutup buku (setara {Rules.Rp(pl.MonthlyShare)} per trip), tidak cair saat tiba");
        Console.WriteLine($"  Kas bersih jika lancar : {Rules.Rp(pl.NetOk)}   (belum termasuk pungli/mogok/kerusakan)");
        ShowEconomy(pl, pl.Contract.Kind == "SPOT");
        Console.WriteLine($"  Bersih terburuk    : {Rules.Rp(pl.NetWorst)}   (kargo hilang/dicuri, sebelum klaim asuransi)");

        Ui.Section("Peluang risiko (perkiraan)");
        Console.WriteLine($"  Rem blong di tanjakan : {pl.PBrake:P0}   (kanvas rem {pl.Truck.BrakePct}%, {pl.Steep} tanjakan/turunan)");
        Console.WriteLine($"  Kargo dicuri          : {pl.PTheft:P0}   (kargo hilang bernilai sekitar {Rules.Rp(Math.Max(pl.Contract.Fee, 200_000) * 2)})");
        if (pl.OverloadPct > 0) Console.WriteLine($"  Kena timbangan ODOL   : {pl.PTilang:P0}   (denda sekitar {Rules.Rp((long)(Math.Max(pl.Contract.Fee, 200_000) * 0.10))} per kena)");
        Console.WriteLine($"  Pungli                : perkiraan {Rules.Rp(pl.PunguliEst)}");
        Console.WriteLine($"  Moral supir {pl.Driver.Morale}/100 memengaruhi semua peluang di atas.");

        Ui.Section("Asuransi (dikunci saat berangkat)");
        Console.WriteLine($"  {pl.Truck.Insurance}{(pl.Truck.CargoIns ? " + Kargo" : "")}   (tidak bisa diganti selama truk jalan)");

        if (pl.Notes.Count > 0)
        {
            Ui.Section("Catatan");
            foreach (var n in pl.Notes) Console.WriteLine("  - " + n);
        }
    }
}