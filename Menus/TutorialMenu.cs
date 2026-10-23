using Nusantara.Services;

namespace Nusantara.Menus;

public static class TutorialMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            Ui.Title("Panduan Pemula");
            int state = TutorialSvc.State(pid);
            var steps = TutorialSvc.Steps(pid);

            if (state == 1) Console.WriteLine("  Tutorial sudah selesai. Hadiah sudah diklaim.");
            else if (state == 2) Console.WriteLine("  Tutorial dilewati. Daftar di bawah hanya sebagai catatan alur main.");
            else Console.WriteLine($"  Selesaikan semua langkah untuk hadiah satu kali {Rules.Rp(Accounting.TutorialReward)}. Menu lain tidak dikunci.");

            Console.WriteLine();
            foreach (var s in steps)
            {
                Console.WriteLine($"  [{(s.Done ? "x" : " ")}] {s.No}. {s.Title}");
                if (!s.Done) Console.WriteLine($"        {s.Hint}");
            }

            if (state == 0)
            {
                Ui.Options(new[] { "Lewati tutorial (tanpa hadiah)" });
                if (Ui.Ask("Pilih: ") == "1" && Ui.Yes("Yakin lewati tutorial? Hadiah tidak bisa diklaim lagi. (y/n): "))
                    Console.WriteLine(TutorialSvc.Skip(pid));
                else if (state == 0) { }
                if (TutorialSvc.State(pid) == 0) return;   // 0 / batal -> kembali ke menu utama
            }
            else { Ui.Pause(); return; }
        }
    }
}