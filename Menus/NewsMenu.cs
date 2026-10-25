using Nusantara.Services;

namespace Nusantara.Menus;

public static class NewsMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            Ui.Title("Berita & Sosmed");
            Ui.Options(new[]
            {
                "Berita terbaru (umpan langsung)",
                "Lihat semua / filter kategori",
                "Analisis 5 hari",
                "Kampanye sosial media & reputasi"
            });

            switch (Ui.Ask("Pilih: "))
            {
                case "1": Feed(null, 10); Ui.Pause(); break;
                case "2": All(); break;
                case "3": Digests(); break;
                case "4": Campaign(pid); break;
                case "0": return;
            }
        }
    }

    static void Print(NewsRow n)
    {
        Console.WriteLine();
        Console.WriteLine($"  [{n.When}] [{n.Category}] {n.Headline}");
        if (n.DigestId != null) Console.WriteLine("      (analisis lengkap ada di menu Berita opsi 3)");
        else if (n.Body != "") Console.WriteLine("      " + n.Body);
    }

    static void Feed(string? category, int limit)
    {
        var list = NewsSvc.Latest(limit, category);
        Ui.Section(category == null ? "Berita terbaru" : "Berita " + category);
        if (list.Count == 0) { Ui.Empty("Belum ada berita."); return; }
        foreach (var n in list) Print(n);
    }

    static void All()
    {
        var cats = NewsSvc.Categories();
        if (cats.Count == 0) { Ui.Empty("Belum ada berita."); Ui.Pause(); return; }

        var options = new List<string> { "Semua kategori" };
        options.AddRange(cats);
        int pick = Ui.Choose("Filter kategori:", options.ToArray(), 1);
        string? category = pick == 1 ? null : cats[pick - 2];

        long? before = null;
        while (true)
        {
            var page = NewsSvc.Latest(10, category, before);
            if (page.Count == 0) { Ui.Empty("Tidak ada berita lagi."); Ui.Pause(); return; }
            foreach (var n in page) Print(n);
            before = page[^1].NewsId;
            if (Ui.Ask("\nEnter = halaman berikutnya, 0 = selesai: ") == "0") return;
        }
    }

    static void Digests()
    {
        var list = NewsSvc.Digests(10);
        Ui.Section("Analisis 5 hari");
        if (list.Count == 0) { Ui.Empty("Belum ada analisis. Analisis pertama muncul di hari ke-5."); Ui.Pause(); return; }

        for (int i = 0; i < list.Count; i++)
            Console.WriteLine($"  {i + 1,2}. Hari {list[i].PeriodStart} sampai {list[i].PeriodEnd - 1}");
        int pick = Ui.Num("Buka nomor (0=kembali): ", 0);
        if (pick < 1 || pick > list.Count) return;

        Console.WriteLine();
        Console.WriteLine(list[pick - 1].Output);
        Ui.Pause();
    }

    static void Campaign(int pid)
    {
        while (true)
        {
            var p = PlayerSvc.Get(pid);

            Ui.Title("Kampanye & Reputasi");

            Ui.Section("Kejadian dunia terbaru");
            var news = WorldSvc.Recent(pid);
            int day = PlayerSvc.Day(p);
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