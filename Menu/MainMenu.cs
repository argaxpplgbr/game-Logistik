using Nusantara.Models;
using Nusantara.Services;

namespace Nusantara.Menus;

public static class MainMenu
{
    public static void ShowEvents(List<string> msgs)
    {
        if (msgs.Count == 0) return;
        Ui.Section("KEJADIAN TERBARU");
        foreach (var m in msgs) Console.WriteLine("  > " + m);
    }

    public static void ShowDashboard(int pid, Player p)
    {
        int day = PlayerSvc.Day(p);
        int trucks = FleetSvc.Fleet(pid).Count;

        Ui.Title("Nusantara Logistics Manager");
        Console.WriteLine($"  Perusahaan : {p.Name} ({p.Legal})");
        Console.WriteLine($"  Hari ke    : {day}");
        Console.WriteLine($"  Kas        : {Rules.Rp(p.Cash)}");
        Console.WriteLine($"  Rating     : {p.Stars:0.0} bintang (reputasi {p.Reputation})");
        Console.WriteLine($"  Armada     : {trucks}/{FleetSvc.MaxTrucks(p.Tier)} truk");

        var events = WorldSvc.Active(pid, day);
        if (events.Count > 0)
        {
            Ui.Section("BERITA AKTIF");
            foreach (var e in events) Console.WriteLine($"  [BERITA] {e.Title}");
        }

        Ui.Options(new[]
        {
            "Armada",
            "Dealer",
            "Servis",
            "Supir",
            "Kontrak & Berangkat",
            "Perjalanan",
            "Berita & Sosmed",
            "Bank & Asuransi",
            "Pool / Tier / Manajer",
            "Toko"
        }, "Keluar");
    }
}