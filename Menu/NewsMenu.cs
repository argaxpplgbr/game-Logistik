using Nusantara.Services;

namespace Nusantara.Menus;

public static class NewsMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            var p = PlayerSvc.Get(pid);
            int day = PlayerSvc.Day(p);

            Ui.Title("Berita & Sosmed");

            Ui.Section("Running news");
            var news = WorldSvc.Recent(pid);
            if (news.Count == 0) Ui.Empty("Belum ada berita.");
            foreach (var e in news)
                Console.WriteLine($"  Hari {e.StartDay}-{e.EndDay}: {e.Title}{(e.EndDay >= day ? "  [AKTIF]" : "")}");

            Ui.Section("Reputasi");
            Console.WriteLine($"  Dari pengiriman : {p.RepDelivery}/400");
            Console.WriteLine($"  Brand awareness : {p.RepBrand}/100");
            Console.WriteLine($"  Total           : {p.Reputation}");

            Ui.Section("Kampanye sosial media");
            for (int i = 0; i < PlayerSvc.Campaigns.Length; i++)
            {
                var k = PlayerSvc.Campaigns[i];
                Console.WriteLine();
                Console.WriteLine($"  {i + 1}. {k.name}");
                Console.WriteLine($"       Biaya: {Rules.Rp(k.cost)}   Brand +{k.gain}   Minimal tier {k.minTier}");
            }

            Console.WriteLine();
            int pick = Ui.Num("Pilih kampanye (0=kembali): ", 0);
            if (pick <= 0) return;
            Console.WriteLine();
            Console.WriteLine(PlayerSvc.Marketing(pid, pick - 1));
        }
    }
}