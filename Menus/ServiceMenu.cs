
using Nusantara.Services;

namespace Nusantara.Menus;

public static class ServiceMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            Ui.Title("Servis");
            Ui.Options(new[]
            {
                "Servis kendaraan",
                "Riwayat perawatan"
            });

            string action = Ui.Ask("Pilih: ");

            if (action == "2")
            {
                Ui.Section("Riwayat perawatan terakhir");

                var history = OperationsSvc.MaintenanceHistory(pid);

                if (history.Count == 0)
                    Ui.Empty("Belum ada riwayat servis.");

                foreach (var item in history)
                {
                    Console.WriteLine(
                        $"Hari {item.GameDay}: truk #{item.TruckId}, " +
                        $"{item.Components}, biaya {Rules.Rp(item.Cost)}");
                }

                Ui.Pause();
                continue;
            }

            if (action == "0") return;
            if (action != "1") continue;

            var idle = FleetSvc.Fleet(pid)
                .Where(x => x.Status == "IDLE")
                .ToList();

            if (idle.Count == 0)
            {
                Ui.Empty("Tidak ada truk berstatus IDLE untuk diservis.");
                Ui.Pause();
                return;
            }

            foreach (var t in idle)
            {
                Console.WriteLine();
                Console.WriteLine($"[{t.Id}] {t.ModelName}");
                Console.WriteLine($"     Oli    : {Ui.Bar(t.OilPct)}");
                Console.WriteLine($"     Rem    : {Ui.Bar(t.BrakePct)}");
                Console.WriteLine($"     Ban    : {Ui.Bar(t.TirePct)}");
                Console.WriteLine($"     Filter : {Ui.Bar(t.FilterPct)}");
                Console.WriteLine($"     Mesin  : {Ui.Bar(t.EnginePct)}");
            }

            Console.WriteLine();

            int id = Ui.Num("ID truk (0=kembali): ");

            if (id <= 0) return;

            int pick = Ui.Choose(
                "Komponen yang diservis:",
                new[]
                {
                    "Oli",
                    "Kanvas rem",
                    "Ban",
                    "Filter BBM",
                    "Mesin",
                    "Semua yang di bawah 60%"
                },
                6);

            string comp = pick switch
            {
                1 => "OIL",
                2 => "BRAKE",
                3 => "TIRE",
                4 => "FILTER",
                5 => "ENGINE",
                _ => "ALL"
            };

            Console.WriteLine();
            Console.WriteLine(FleetSvc.Service(pid, id, comp));
        }
    }
}
