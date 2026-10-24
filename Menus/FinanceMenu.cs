using Nusantara.Services;

namespace Nusantara.Menus;

public static class FinanceMenu
{
    public static void Run(int pid)
    {
        while (true)
        {
            var v = FinanceSvc.Preview(pid);
            Ui.Title("Keuangan & Tutup Buku");

            Ui.Section("Posisi kas");
            Console.WriteLine($"  Kas tersedia            : {Rules.Rp(v.Cash)}");
            Console.WriteLine($"  UJ sedang di jalan      : {Rules.Rp(v.UjOnRoad)}   (sudah keluar dari kas, sebagian kembali saat trip selesai)");
            Console.WriteLine($"  Piutang borongan        : {Rules.Rp(v.DedicatedFull)}   (BUKAN uang tunai, baru cair saat tutup buku)");
            Console.WriteLine($"  Total utang bank        : {Rules.Rp(v.Debt)}");

            Ui.Section($"Tagihan tutup buku berikutnya: hari {v.NextDay} ({v.DaysLeft} hari lagi)");
            Console.WriteLine($"  Gaji, BPJS, staf (+tunggakan) : {Rules.Rp(v.Bills.Payroll)}");
            Console.WriteLine($"  Sewa pool                     : {Rules.Rp(v.Bills.Rent)}");
            Console.WriteLine($"  Premi asuransi                : {Rules.Rp(v.Bills.Premium)}");
            foreach (var l in v.Loans) Console.WriteLine($"  Cicilan pinjaman #{l.Id,-12}     : {Rules.Rp(l.Installment)}");
            Console.WriteLine($"  SALDO MINIMUM YANG PERLU DIJAGA: {Rules.Rp(v.MinNeeded)}");
            var pl = PlayerSvc.Get(pid);
            Console.WriteLine($"  Cadangan operasi disarankan    : {Rules.Rp(FinanceSvc.OperatingReserve(pl, v))}   (tagihan + UJ 2 perjalanan per truk aktif + servis tertunda)");

            Ui.Section("Jawaban cepat: kalau tidak ada pemasukan sampai tutup buku?");
            long gap = v.NoIncome.EndCash;
            Console.WriteLine(gap >= 0
                ? $"  Kas CUKUP. Sisa setelah tutup buku: {Rules.Rp(gap)}."
                : $"  Kas TIDAK CUKUP. Kekurangan {Rules.Rp(-gap)} ({Accounting.CashStatus(gap)}). Pemasukan kas minimal {Rules.Rp(-gap)} dibutuhkan sebelum tutup buku.");

            Ui.Options(new[] { "Proyeksi 7 hari", "Proyeksi 30 hari", "Lunasi pinjaman" });
            switch (Ui.Ask("Pilih: "))
            {
                case "1": Projection(v, 7); break;
                case "2": Projection(v, 30); break;
                case "3": Repay(pid); break;
                case "0": return;
            }
        }
    }

    static void Projection(ClosePreview v, int days)
    {
        Ui.Section($"Proyeksi {days} hari ke depan");
        if (v.DaysLeft > days)
        {
            Console.WriteLine($"  Tidak ada tutup buku dalam {days} hari (berikutnya hari {v.NextDay}, {v.DaysLeft} hari lagi).");
            Console.WriteLine($"  Kas saat ini {Rules.Rp(v.Cash)}; tidak ada tagihan bulanan yang jatuh tempo pada periode ini.");
            Ui.Pause();
            return;
        }
        Scenario("Pesimis: tanpa pemasukan sama sekali", v.NoIncome);
        Scenario("Sesuai capaian trip borongan saat ini", v.Current);
        Scenario("Semua target borongan tercapai", v.Full);
        Console.WriteLine();
        Console.WriteLine("  Asumsi: tidak memasukkan pendapatan spot, pungli, kerusakan, dan klaim. Ini estimasi, bukan jaminan.");
        Ui.Pause();
    }

    static void Scenario(string name, Accounting.CloseSim s)
    {
        Console.WriteLine();
        Console.WriteLine($"  {name}");
        Console.WriteLine($"     Gaji {(s.Arrears > 0 ? "MENUNGGAK" : "dibayar")}, cicilan dibayar {s.LoansPaid}, gagal {s.LoansMissed}");
        Console.WriteLine($"     Kas akhir {Rules.Rp(s.EndCash)}  [{Accounting.CashStatus(s.EndCash)}]");
    }

    static void Repay(int pid)
    {
        var loans = FinanceSvc.Loans(pid);
        if (loans.Count == 0) { Ui.Empty("Tidak ada pinjaman."); Ui.Pause(); return; }
        foreach (var l in loans) Console.WriteLine($"  [{l.Id}] sisa {Rules.Rp(l.Remaining)}, cicilan {Rules.Rp(l.Installment)}, {l.MonthsLeft} bulan lagi");
        int id = Ui.Num("ID pinjaman (0=batal): ", 0);
        if (id <= 0) return;
        var l2 = loans.FirstOrDefault(x => x.Id == id);
        if (l2 == null) { Console.WriteLine("Pinjaman tidak ada di daftar."); Ui.Pause(); return; }
        if (Ui.Yes($"Lunasi {Rules.Rp(l2.Remaining)} sekarang? (y/n): "))
            Console.WriteLine(FinanceSvc.Repay(pid, id));
        Ui.Pause();
    }
}