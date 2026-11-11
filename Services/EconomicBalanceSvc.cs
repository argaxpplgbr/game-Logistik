
using Nusantara.Models;

namespace Nusantara.Services;

public sealed record EconomicAssessment(
    string Risk,
    string Advice,
    long MonthlyFixedCost,
    long OperatingReserve,
    double RunwayMonths
);

public static class EconomicBalanceSvc
{
    public static EconomicAssessment Assess(Player player, ClosePreview preview)
    {
        long monthlyFixed =
            preview.Bills.Payroll +
            preview.Bills.Rent +
            preview.Bills.Premium +
            preview.Loans.Sum(x => x.Installment);

        long reserve = FinanceSvc.OperatingReserve(player, preview);

        double runway = monthlyFixed <= 0
            ? double.PositiveInfinity
            : Math.Max(0, player.Cash) / (double)monthlyFixed;

        if (player.Bankrupt)
        {
            return new EconomicAssessment(
                "BANGKRUT",
                "Perusahaan sudah bangkrut. Periksa laporan keuangan dan kewajiban yang belum dibayar.",
                monthlyFixed, reserve, runway);
        }

        if (preview.Current.EndCash < 0 || preview.Current.LoansMissed > 0)
        {
            return new EconomicAssessment(
                "KRITIS",
                "Simulasi tutup buku menunjukkan risiko gagal bayar. Prioritaskan pemasukan dan kurangi pengeluaran yang belum wajib.",
                monthlyFixed, reserve, runway);
        }

        if (player.Cash < reserve)
        {
            return new EconomicAssessment(
                "TINGGI",
                "Kas berada di bawah cadangan operasi. Tunda pembelian armada dan siapkan dana perjalanan serta servis.",
                monthlyFixed, reserve, runway);
        }

        if (preview.NoIncome.EndCash < 0)
        {
            return new EconomicAssessment(
                "WASPADA",
                "Keuangan masih bergantung pada pemasukan kontrak. Jaga kontrak aktif dan pastikan ada pekerjaan berikutnya.",
                monthlyFixed, reserve, runway);
        }

        if (runway < 1.5)
        {
            return new EconomicAssessment(
                "PERLU DIAWASI",
                "Cadangan kas hanya mencakup kurang dari sekitar 1,5 bulan biaya tetap. Pertimbangkan menahan ekspansi.",
                monthlyFixed, reserve, runway);
        }

        return new EconomicAssessment(
            "STABIL",
            "Kas dan simulasi kewajiban saat ini masih dalam kondisi relatif aman.",
            monthlyFixed, reserve, runway);
    }
}
