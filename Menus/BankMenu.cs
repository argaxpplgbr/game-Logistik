using Nusantara.Services;

namespace Nusantara.Menus;

public static class BankMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            var p = PlayerSvc.Get(pid);
            Ui.Title("Bank");
            Console.WriteLine($"  Plafon pinjaman tersedia: {Rules.Rp(FinanceSvc.MaxLoan(p))}");

            Ui.Section("Pinjaman aktif");
            var loans = FinanceSvc.Loans(pid);
            if (loans.Count == 0) Ui.Empty("Tidak ada pinjaman.");
            foreach (var l in loans)
            {
                Console.WriteLine();
                Console.WriteLine($"[{l.Id}] Sisa utang {Rules.Rp(l.Remaining)}");
                Console.WriteLine($"     Cicilan : {Rules.Rp(l.Installment)} per bulan, {l.MonthsLeft} bulan lagi");
                Console.WriteLine($"     Telat   : {l.Missed}x");
            }

            Console.WriteLine();
            Console.WriteLine("  (Asuransi diatur di menu Armada.)");
            Ui.Options(new[] { "Ambil pinjaman", "Lunasi pinjaman" });

            switch (Ui.Ask("Pilih: "))
            {
                case "1":
                    long amt = long.TryParse(Ui.Ask("Jumlah (Rp, angka saja): "), out var a) ? a : 0;
                    int months = Ui.Choose("Tenor:", new[] { "12 bulan", "24 bulan", "36 bulan" }) * 12;
                    Console.WriteLine();
                    Console.WriteLine(FinanceSvc.TakeLoan(pid, amt, months));
                    break;
                case "2":
                    Console.WriteLine(FinanceSvc.Repay(pid, Ui.Num("ID pinjaman: ")));
                    break;
                case "0": return;
            }
        }
    }
}