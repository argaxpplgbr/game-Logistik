using Nusantara.Services;

namespace Nusantara.Menus;

public static class FleetMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            Ui.Title("Armada");
            ShowFleet(pid);
            Ui.Options(new[] { "Pasang supir ke truk", "Ganti jenis BBM", "Atur asuransi", "Pasang kosmetik" });

            switch (Ui.Ask("Pilih: "))
            {
                case "1": AssignDriver(pid); break;
                case "2": ChangeFuel(pid); break;
                case "3": SetInsurance(pid); break;
                case "4": Equip(pid); break;
                case "0": return;
            }
        }
    }

    static void ShowFleet(int pid)
    {
        var fleet = FleetSvc.Fleet(pid);
        if (fleet.Count == 0) { Ui.Empty("Belum punya truk. Beli di menu Dealer."); return; }

        var staff = HrSvc.Staff(pid).ToDictionary(d => d.Id);
        foreach (var t in fleet)
        {
            string driver = t.DriverId != null && staff.TryGetValue(t.DriverId.Value, out var d)
                ? $"{d.Name} (moral {d.Morale})" : "-";
            string insurance = t.Insurance + (t.CargoIns ? " + Kargo" : "");
            string cosmetics = string.Join(", ", new[] { t.Livery, t.Horn, t.Led, t.Sticker }.Where(s => s != ""));

            Console.WriteLine();
            Console.WriteLine($"[{t.Id}] {t.ModelName}");
            Console.WriteLine($"     Status   : {t.Status}");
            Console.WriteLine($"     Supir    : {driver}");
            Console.WriteLine($"     Bak      : {t.BodyCode ?? "-"} (muatan maks {t.Payload:N0} kg)");
            Console.WriteLine($"     BBM      : {t.FuelPref}");
            Console.WriteLine($"     Asuransi : {insurance}");
            Console.WriteLine($"     Oli      : {Ui.Bar(t.OilPct)}");
            Console.WriteLine($"     Rem      : {Ui.Bar(t.BrakePct)}");
            Console.WriteLine($"     Ban      : {Ui.Bar(t.TirePct)}");
            Console.WriteLine($"     Filter   : {Ui.Bar(t.FilterPct)}");
            Console.WriteLine($"     Mesin    : {Ui.Bar(t.EnginePct)}");
            if (cosmetics != "") Console.WriteLine($"     Kosmetik : {cosmetics}");
        }
    }

    static void AssignDriver(int pid)
    {
        Ui.Section("Supir yang tersedia");
        var idle = HrSvc.Staff(pid).Where(x => x.Status == "IDLE").ToList();
        if (idle.Count == 0) Ui.Empty("Tidak ada supir menganggur. Rekrut di menu Supir.");
        foreach (var d in idle) Console.WriteLine($"  Supir [{d.Id}] {d.Name}  SIM {d.Sim}");

        Console.WriteLine();
        Console.WriteLine(FleetSvc.Assign(pid, Ui.Num("ID truk: "), Ui.Num("ID supir: ")));
    }

    static void ChangeFuel(int pid)
    {
        int tid = Ui.Num("ID truk: ");
        int pick = Ui.Choose("Jenis BBM:", new[] { "Bio Solar (murah, mesin cepat rusak)", "Dexlite (mahal, mesin awet)" });
        Console.WriteLine(FleetSvc.SetFuel(pid, tid, pick == 2 ? "DEX" : "BIO"));
    }

    static void SetInsurance(int pid)
    {
        int tid = Ui.Num("ID truk: ");
        int pick = Ui.Choose("Jenis asuransi:", new[] { "Tanpa asuransi", "TLO (Total Loss Only)", "All Risk" });
        string ins = pick switch { 2 => "TLO", 3 => "ALLRISK", _ => "NONE" };
        bool cargo = Ui.Yes("Tambah Cargo Insurance? (y/n): ");
        Console.WriteLine(FleetSvc.SetInsurance(pid, tid, ins, cargo));
    }

    static void Equip(int pid)
    {
        Ui.Section("Kosmetik yang kamu miliki");
        var owned = PlayerSvc.Shop(pid).Where(x => x.Owned > 0).ToList();
        if (owned.Count == 0) Ui.Empty("Belum punya kosmetik. Beli di menu Toko.");
        foreach (var c in owned) Console.WriteLine($"  [{c.Id}] {c.Kind,-9} {c.Name}");

        Console.WriteLine();
        Console.WriteLine(PlayerSvc.Equip(pid, Ui.Num("ID truk (0 untuk pool): ", 0), Ui.Num("ID item: ")));
    }
}