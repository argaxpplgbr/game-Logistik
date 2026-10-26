using Nusantara.Menus;
using Nusantara.Services;

namespace Nusantara;

// Loop permainan untuk satu slot. Selesai (return) saat pemain memilih 0 atau perusahaan bangkrut.
public static class GameLoop
{
    public static void Play(int pid)
    {
        while (true)
        {
            List<string> msgs;
            try { msgs = WorldSvc.Tick(pid); msgs.AddRange(MarketSvc.RunDaily()); }
            catch (Exception ex) { msgs = new List<string> { "Kesalahan saat memproses waktu game: " + ex.Message }; }

            SaveSlots.Persist();   // simpan otomatis jam game tiap siklus
            var p = PlayerSvc.Get(pid);

            MainMenu.ShowEvents(msgs);

            if (p.Bankrupt)
            {
                Console.WriteLine("\n*** GAME OVER: perusahaan BANGKRUT. ***");
                Ui.Pause();
                return;
            }

            MainMenu.ShowDashboard(pid, p);

            switch (Ui.Ask("Pilih: ").ToLowerInvariant())
            {
                case "1": FleetMenu.Run(pid); break;
                case "2": DealerMenu.Run(pid); break;
                case "3": ServiceMenu.Run(pid); break;
                case "4": DriverMenu.Run(pid); break;
                case "5": ContractMenu.Run(pid); break;
                case "6": TripMenu.Run(pid); break;
                case "7": NewsMenu.Run(pid); break;
                case "8": BankMenu.Run(pid); break;
                case "9": FacilityMenu.Run(pid); break;
                case "10": ShopMenu.Run(pid); break;
                case "11": LedgerMenu.Run(pid); break;
                case "12": FinanceMenu.Run(pid); break;
                case "13": TutorialMenu.Run(pid); break;
                case "14": MarketMenu.Run(pid); break;

                case "p":
                    if (SimClock.Paused) { SimClock.Resume(); Console.WriteLine("  Waktu game berjalan lagi."); }
                    else { SimClock.Pause(); Console.WriteLine("  Waktu game DIJEDA. Menu tetap bisa dipakai, tapi jam, perjalanan, dan tagihan berhenti."); }
                    SaveSlots.Persist();
                    Ui.Pause();
                    break;

                case "s":
                    Console.WriteLine("  " + SaveSlots.Backup());
                    Ui.Pause();
                    break;

                case "v":
                    double sp = SimClock.NextSpeed();
                    SaveSlots.Persist();
                    Console.WriteLine($"  Kecepatan waktu game: x{sp:0.#}");
                    Ui.Pause();
                    break;

                case "0": return;
            }
        }
    }
}