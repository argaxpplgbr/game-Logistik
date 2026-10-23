using Nusantara.Models;
using Nusantara.Services;

namespace Nusantara.Menus;

public static class DriverMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            Ui.Title("Supir");
            Ui.Options(new[]
            {
                "Lihat CV pelamar",
                "Interview kandidat",
                "Rekrut supir",
                "Refresh CV (Rp 250.000)",
                "Lihat staf supir",
                "Naikkan gaji",
                "Pecat supir"
            });

            switch (Ui.Ask("Pilih: "))
            {
                case "1": ShowCandidates(pid); break;
                case "2": ShowCandidates(pid); Console.WriteLine(HrSvc.Interview(pid, Ui.Num("ID kandidat: "))); break;
                case "3": Hire(pid); break;
                case "4": Console.WriteLine(HrSvc.Refresh(pid, true)); break;
                case "5": ShowStaff(pid); break;
                case "6": ShowStaff(pid); Console.WriteLine(HrSvc.Raise(pid, Ui.Num("ID supir: "), Ui.Num("Naik % (1-50) [10]: ", 10))); break;
                case "7": ShowStaff(pid); Console.WriteLine(HrSvc.Fire(pid, Ui.Num("ID supir: "))); break;
                case "0": return;
            }
        }
    }

    static void ShowCandidates(int pid)
    {
        Ui.Section("CV pelamar");
        var list = HrSvc.Candidates(pid);
        if (list.Count == 0) Ui.Empty("Belum ada pelamar. Refresh CV dulu.");
        foreach (var d in list)
        {
            Console.WriteLine();
            Console.WriteLine($"[{d.Id}] {d.Name}  (SIM {d.Sim})");
            Console.WriteLine($"     Kecepatan : {d.Speed}/10   Eco-driving : {d.Eco}/10");
            Console.WriteLine($"     Pengalaman: {d.ExpYears} tahun");
            Console.WriteLine($"     Kejujuran : {d.HonestyClaim}/10 (menurut CV)");
            if (d.Interviewed)
                Console.WriteLine($"     Hasil interview: kejujuran ASLI {d.Honesty}/10, pernah nabrak {d.CrashCount}x");
            Console.WriteLine($"     Minta     : gaji {Rules.Rp(d.SalaryExp)}, UJ {d.UjExp:0.00}x, komisi {d.CommissionPct}%");
        }
    }

    static void Hire(int pid)
    {
        ShowCandidates(pid);
        Console.WriteLine();
        int id = Ui.Num("ID kandidat: ");
                int offer = Ui.Num("Tawaran gaji % dari permintaan (80-130) [100]: ", 100);
        Console.WriteLine(HrSvc.Hire(pid, id, offer));
    }

    static void ShowStaff(int pid)
    {
        Ui.Section("Staf supir");
        var staff = HrSvc.Staff(pid);
        if (staff.Count == 0) Ui.Empty("Belum ada supir.");
        foreach (var d in staff)
        {
            Console.WriteLine();
            Console.WriteLine($"[{d.Id}] {d.Name}  (SIM {d.Sim}, {d.Status})");
            Console.WriteLine($"     Moral : {d.Morale}/100");
            Console.WriteLine($"     Gaji  : {Rules.Rp(d.Salary)} + BPJS");
            Console.WriteLine($"     Komisi: {d.CommissionPct}%   UJ minimal: {d.UjExp:0.00}x");
        }
    }
}