using Nusantara.Models;
using Nusantara.Services;

namespace Nusantara.Menus;

public static class MarketMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            int day = CompanySvc.WorldDay();
            var me = PlayerSvc.Get(pid);
            var open = MarketSvc.Open(day);

            Ui.Title("Pasar Kontrak Bersama");
            Console.WriteLine($"  Hari dunia {day}. Semua perusahaan (kamu dan AI) melihat peluang yang sama dan memakai aturan yang sama.");
            Console.WriteLine($"  Status kamu: tier {MarketConfig.TierName(me.Tier)}, reputasi {me.Reputation}.");

            if (open.Count == 0) Ui.Empty("Belum ada peluang kontrak.");
            else
            {
                Console.WriteLine();
                Console.WriteLine("   ID  Jenis      Rute                                  Muatan      Tarif acuan      Tier       Rep  Tutup  Penawar");
                foreach (var w in open)
                    Console.WriteLine($"  {w.Id,3}  {(w.Kind == "SPOT" ? "Spot" : "Borongan"),-9}  {w.Origin + " - " + w.Dest,-36}  {w.WeightKg,6:N0} kg  {Rules.Rp(w.Fee),15}  {MarketConfig.TierName(w.MinTier),-9}  {w.RepRequired,3}  h{w.BidCloseDay,-4}  {MarketSvc.Bids(w.Id).Count}");
            }

            Ui.Options(new[] { "Pasang penawaran", "Hasil penawaran saya", "Daftar pesaing" });
            switch (Ui.Ask("Pilih: "))
            {
                case "1": Bid(pid, day); break;
                case "2": Results(pid); break;
                case "3": Rivals(pid); break;
                case "0": return;
            }
        }
    }

    static void Bid(int pid, int day)
    {
        int id = Ui.Num("ID kontrak (0=batal): ");
        if (id <= 0) return;
        var wc = MarketSvc.Get(id);
        if (wc == null) { Console.WriteLine("  Kontrak tidak ditemukan."); Ui.Pause(); return; }

        int pct = Ui.Num($"Harga penawaran sebagai % tarif acuan ({(int)(MarketConfig.MinPriceFactor * 100)}-{(int)(MarketConfig.MaxPriceFactor * 100)}) [95]: ", 95);
        long price = wc.Fee * pct / 100 / 1000 * 1000;
        Console.WriteLine("  Harga penawaran: " + Rules.Rp(price));
        Console.WriteLine("  " + MarketSvc.PlaceBid(pid, id, price, day));
        Console.WriteLine("  Catatan: harga terendah tidak otomatis menang; reputasi, muatan, dan kondisi armada ikut dinilai.");
        Ui.Pause();
    }

    static void Results(int pid)
    {
        Ui.Section("Penawaran terakhir kamu");
        var bids = MarketSvc.MyBids(pid, 10);
        if (bids.Count == 0) { Ui.Empty("Belum pernah menawar."); Ui.Pause(); return; }
        foreach (var b in bids)
        {
            var w = MarketSvc.Get(b.WorldContractId);
            string route = w == null ? "?" : $"{w.Origin} - {w.Dest}";
            Console.WriteLine($"  #{b.Id} {route}: {Rules.Rp(b.Price)} -> {b.Status}" + (b.Note != "" ? $" ({b.Note})" : ""));
        }
        Ui.Pause();
    }

    static void Rivals(int pid)
    {
        Ui.Section("Pesaing");
        var ids = CompanySvc.AiIds(false);
        if (ids.Count == 0) { Ui.Empty("Belum ada perusahaan AI."); Ui.Pause(); return; }
        foreach (var id in ids)
        {
            var r = PlayerSvc.Get(id);
            Console.WriteLine($"  {r.Name,-28} tier {MarketConfig.TierName(r.Tier),-9} reputasi {r.Reputation,3}  armada {FleetSvc.Fleet(id).Count} truk" + (r.Bankrupt ? "  (BANGKRUT)" : ""));
        }
        Ui.Pause();
    }
}