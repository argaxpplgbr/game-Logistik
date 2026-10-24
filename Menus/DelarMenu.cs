using Nusantara.Services;

namespace Nusantara.Menus;

public static class DealerMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            Ui.Title("Dealer");
            Ui.Options(new[] { "Truk baru (chassis + kabin)", "Pasar truk bekas", "Karoseri (pasang bak)" });

            switch (Ui.Ask("Pilih: "))
            {
                case "1": BuyNew(pid); break;
                case "2": BuyUsed(pid); break;
                case "3": Karoseri(pid); break;
                case "0": return;
            }
        }
    }

    static void BuyNew(int pid)
    {
        var p = PlayerSvc.Get(pid);
        Ui.Section("Truk baru");
        foreach (var m in FleetSvc.Models(p.Tier))
        {
            Console.WriteLine();
            Console.WriteLine($"[{m.Id}] {m.Name}");
            Console.WriteLine($"     Harga   : {Rules.Rp(m.Price)}");
            Console.WriteLine($"     Muatan  : {m.MaxLoad:N0} kg  |  {m.KmPerL} km/L  |  SIM {m.Sim}");
            Console.WriteLine($"     BBM     : {(m.FuelReq == "DEX" ? "WAJIB Dexlite" : "Solar boleh")}");
            Console.WriteLine($"     Bak     : {(m.FactoryBody == null ? "butuh karoseri" : "bak standar (siap jalan)")}");
        }
        Console.WriteLine();
        int id = Ui.Num("Beli ID (0=batal): ");
        if (id <= 0) return;
        var model = FleetSvc.Models(p.Tier).FirstOrDefault(x => x.Id == id);
        if (model == null) { Console.WriteLine("Model tidak ada atau belum terbuka untuk statusmu."); return; }
        ShowPurchase(pid, model.Price, model.MaxLoad, model.Sim, 0);
        if (!Ui.Yes("\nLanjut beli? (y/n): ")) return;
        Console.WriteLine(FleetSvc.BuyNew(pid, id));
    }

    static void ShowPurchase(int pid, long price, int maxLoad, string sim, long extraBacklog)
    {
        var v = FleetSvc.PreviewPurchase(pid, price, maxLoad, sim, extraBacklog);
        Ui.Section("Ringkasan sebelum membeli");
        Console.WriteLine($"  Saldo sebelum beli        : {Rules.Rp(v.CashBefore)}");
        Console.WriteLine($"  Harga kendaraan           : {Rules.Rp(v.Price)}");
        Console.WriteLine($"  Saldo sesudah beli        : {Rules.Rp(v.CashAfter)}");
        Console.WriteLine($"  Gaji + BPJS 1 supir/bulan : sekitar {Rules.Rp(v.DriverMonthly)}   (baru dibayar setelah supir direkrut)");
        Console.WriteLine($"  Premi All Risk (opsional) : sekitar {Rules.Rp(v.PremiumAllRisk)} per bulan");
        Console.WriteLine($"  UJ pekerjaan pertama      : sekitar {Rules.Rp(v.FirstUj)}   (asumsi rata-rata kelas ini)");
        Console.WriteLine($"  Servis yang tertunda      : {Rules.Rp(v.ServiceReserve)}   (komponen di bawah 60% pada armada + truk ini)");
        Ui.Section("Cadangan operasi yang disarankan");
        Console.WriteLine($"  Tagihan tetap bulan depan : {Rules.Rp(v.FixedNext)}   (termasuk gaji supir untuk truk ini)");
        Console.WriteLine($"  UJ 2 perjalanan per truk  : {Rules.Rp(v.UjReserve)}");
        Console.WriteLine($"  Servis tertunda           : {Rules.Rp(v.ServiceReserve)}");
        Console.WriteLine($"  TOTAL CADANGAN            : {Rules.Rp(v.Reserve)}   (uang yang disarankan tetap ada, bukan biaya yang langsung dipotong)");
        if (v.BelowReserve)
            Console.WriteLine($"  PERINGATAN: saldo sesudah beli ({Rules.Rp(v.CashAfter)}) di bawah cadangan. Kamu tetap boleh membeli, tapi kas bisa mepet saat tutup buku.");
    }

    static void BuyUsed(int pid)
    {
        Ui.Section("Pasar truk bekas");
        var pl = PlayerSvc.Get(pid);
        double lab = FleetSvc.LaborFactor(pl, false);
        var list = FleetSvc.Used(pid);
        if (list.Count == 0) { Ui.Empty("Belum ada truk bekas. Cek lagi nanti."); return; }

        foreach (var u in list)
        {
            Console.WriteLine();
            Console.WriteLine($"[{u.Id}] {u.ModelName} + bak {u.BodyCode}");
            Console.WriteLine($"     Harga   : {Rules.Rp(u.Price)}  |  umur {u.AgeDays} hari");
            Console.WriteLine($"     Oli     : {Ui.Bar(u.OilPct)}");
            Console.WriteLine($"     Rem     : {Ui.Bar(u.BrakePct)}");
            Console.WriteLine($"     Ban     : {Ui.Bar(u.TirePct)}");
            Console.WriteLine($"     Filter  : {Ui.Bar(u.FilterPct)}");
            Console.WriteLine($"     Mesin   : {Ui.Bar(u.EnginePct)}");
            long svc = EconomySvc.ServiceBacklog(u.OilPct, u.BrakePct, u.TirePct, u.FilterPct, u.EnginePct, u.Tier, lab);
            Console.WriteLine($"     Servis  : perkiraan {Rules.Rp(svc)}   ->  TOTAL SIAP KERJA {Rules.Rp(u.Price + svc)}");
        }
        Console.WriteLine();
        int id = Ui.Num("Beli ID (0=batal): ");
        if (id <= 0) return;
         var pick = list.FirstOrDefault(x => x.Id == id);
        if (pick == null) { Console.WriteLine("Listing tidak ada."); return; }
        var cat = FleetSvc.Models(3).FirstOrDefault(x => x.Id == pick.CatalogId);
        if (cat != null)
        {
            long pickSvc = EconomySvc.ServiceBacklog(pick.OilPct, pick.BrakePct, pick.TirePct, pick.FilterPct, pick.EnginePct, pick.Tier, lab);
            ShowPurchase(pid, pick.Price, cat.MaxLoad, cat.Sim, pickSvc);
            if (!Ui.Yes("\nLanjut beli? (y/n): ")) return;
        }
        Console.WriteLine(FleetSvc.BuyUsed(pid, id));
    }

    static void Karoseri(int pid)
    {
        Ui.Section("Truk di karoseri");
        var trucks = FleetSvc.Fleet(pid).Where(x => x.Status is "WAIT_BODY" or "KAROSERI").ToList();
        if (trucks.Count == 0) { Ui.Empty("Tidak ada truk yang menunggu bak."); return; }

        foreach (var t in trucks)
        {
            string eta = t.BodyReadyAt != null
                ? $"selesai {(t.BodyReadyAt.Value - DateTime.UtcNow).TotalMinutes:0.0} menit lagi" : "menunggu dipasang bak";
            Console.WriteLine($"  Truk [{t.Id}] {t.ModelName}  -  {t.Status}, {eta}");
        }
        if (!trucks.Any(x => x.Status == "WAIT_BODY")) return;

        Ui.Section("Jenis bak");
        foreach (var b in FleetSvc.Bodies())
            Console.WriteLine($"  {b.Code,-9} {b.Name,-22} {Rules.Rp(b.Price),-18} {b.Days} hari, {b.WeightKg} kg");

        Console.WriteLine();
        int tid = Ui.Num("ID truk: ");
        string code = Ui.Ask("Kode bak: ").ToUpper();
        Console.WriteLine(FleetSvc.Karoseri(pid, tid, code));
    }
}