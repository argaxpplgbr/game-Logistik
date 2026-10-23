using Nusantara.Services;

namespace Nusantara.Menus;

public static class ShopMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            Ui.Title("Toko");
            Ui.Options(new[] { "Top-up kas (simulasi)", "Kosmetik", "Tonton iklan (hadiah)" });

            switch (Ui.Ask("Pilih: "))
            {
                case "1": TopUp(pid); break;
                case "2": Cosmetics(pid); break;
                case "3": Ads(pid); break;
                case "0": return;
            }
        }
    }

    static void TopUp(int pid)
    {
        Ui.Section("Paket top-up");
        for (int i = 0; i < PlayerSvc.Packs.Length; i++)
        {
            var k = PlayerSvc.Packs[i];
            Console.WriteLine($"  {i + 1}. {k.name,-22} +{Rules.Rp(k.cash),-20} (Rp {k.idr:N0})");
        }
        Console.WriteLine();
        Console.WriteLine(PlayerSvc.TopUp(pid, Ui.Num("Pilih paket: ", 0) - 1));
    }

    static void Cosmetics(int pid)
    {
        Ui.Section("Kosmetik");
            Console.WriteLine("  (SIMULASI: harga Rupiah hanya contoh, belum ada pembayaran sungguhan dan kas game tidak dipotong.)");
        foreach (var c in PlayerSvc.Shop(pid))
            Console.WriteLine($"  [{c.Id}] {c.Kind,-9} {c.Name,-34} Rp {c.PriceIdr,7:N0} {(c.Owned > 0 ? " (dimiliki)" : "")}");

        Console.WriteLine();
        int item = Ui.Num("Beli ID (0=batal): ", 0);
        if (item > 0) Console.WriteLine(PlayerSvc.BuyCosmetic(pid, item));
    }

    static void Ads(int pid)
    {
        Console.WriteLine("\n(Simulasi iklan 5 detik...)");
        Thread.Sleep(5000);

        int pick = Ui.Choose("Pilih hadiah:",
            new[] { "Diskon sparepart", "Refresh CV supir", "Percepat karoseri" });
        Console.WriteLine();
        switch (pick)
        {
            case 1: Console.WriteLine(PlayerSvc.AdReward(pid, "DISKON")); break;
            case 2: Console.WriteLine(PlayerSvc.AdReward(pid, "CV")); break;
            case 3: Console.WriteLine(PlayerSvc.AdReward(pid, "KAROSERI", Ui.Num("ID truk di karoseri: "))); break;
        }
    }
}