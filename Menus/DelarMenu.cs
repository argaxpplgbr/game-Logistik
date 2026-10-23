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
        Console.WriteLine(FleetSvc.BuyNew(pid, id));
    }

    static void BuyUsed(int pid)
    {
        Ui.Section("Pasar truk bekas");
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
        }
        Console.WriteLine();
        int id = Ui.Num("Beli ID (0=batal): ");
        if (id <= 0) return;
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