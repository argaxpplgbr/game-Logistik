using Nusantara.Services;

namespace Nusantara.Menus;

public static class FacilityMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            ShowStatus(pid);
            Ui.Options(new[]
            {
                "Naik tier (CV / PT)",
                "Sewa pool",
                "Bangun pool sendiri",
                "Bangun depo solar",
                "Bangun gudang sparepart",
                "Rekrut mekanik",
                "Pecat mekanik",
                "Sertifikasi B3",
                "Rekrut manajer operasional",
                "Atur rule manajer"
            });

            switch (Ui.Ask("Pilih: "))
            {
                case "1": Console.WriteLine(PlayerSvc.UpgradeTier(pid)); break;
                case "2": Console.WriteLine(PlayerSvc.RentPool(pid)); break;
                case "3": Console.WriteLine(PlayerSvc.BuildPool(pid)); break;
                case "4": Console.WriteLine(PlayerSvc.BuildDepot(pid)); break;
                case "5": Console.WriteLine(PlayerSvc.BuildWarehouse(pid)); break;
                case "6": Console.WriteLine(PlayerSvc.HireMechanic(pid)); break;
                case "7": Console.WriteLine(PlayerSvc.FireMechanic(pid)); break;
                case "8": Console.WriteLine(PlayerSvc.BuyB3(pid)); break;
                case "9": Console.WriteLine(PlayerSvc.HireManager(pid)); break;
                case "10": ManagerRules(pid); break;
                case "0": return;
            }
        }
    }

    static void ShowStatus(int pid)
    {
        var p = PlayerSvc.Get(pid);
        Ui.Title("Pool, Tier & Manajer");
        Console.WriteLine($"  Status usaha    : {p.Legal}");
        Console.WriteLine($"  Pool            : {p.PoolType}");
        Console.WriteLine($"  Depo solar      : {(p.Depot ? "ada" : "-")}");
        Console.WriteLine($"  Gudang sparepart: {(p.Warehouse ? "ada" : "-")}");
        Console.WriteLine($"  Mekanik tetap   : {p.Mechanics}");
        Console.WriteLine($"  Manajer         : {(p.Manager ? "ada" : "-")}");
        Console.WriteLine($"  Sertifikasi B3  : {(p.B3Cert ? "ya" : "-")}");
    }

    static void ManagerRules(int pid)
    {
        var p = PlayerSvc.Get(pid);
        if (!p.Manager) { Console.WriteLine("Rekrut manajer dulu."); return; }

        int th = Ui.Num($"Servis otomatis jika komponen di bawah % (sekarang {p.MgrServiceTh}): ", p.MgrServiceTh);
        int um = Ui.Choose("UJ otomatis:",
            new[] { "Pas-pasan (0.9x)", "Standar (1.0x)", "Longgar (1.1x)", "Sangat longgar (1.3x)" }, 3);
        double mult = um == 1 ? 0.9 : um == 2 ? 1.0 : um == 4 ? 1.3 : 1.1;
        int mg = Ui.Num($"Margin minimal kontrak spot % (sekarang {p.MgrMinMargin * 100:0}): ", (int)(p.MgrMinMargin * 100));

        PlayerSvc.SetManagerRules(pid, Rules.Clamp(th, 5, 90), mult, Rules.Clamp(mg, 0, 90) / 100.0);
        Console.WriteLine("Rule manajer disimpan.");
    }
}