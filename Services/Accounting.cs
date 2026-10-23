namespace Nusantara.Services;

// Rumus uang v3 di satu tempat. Menu hanya menampilkan hasilnya.
public static class Accounting
{
    public const long TutorialReward = 5_000_000;

    public static string Signed(long n) => (n >= 0 ? "+" : "-") + Rules.Rp(Math.Abs(n));

    public static bool Reconciles(long ledgerSum, long cash) => ledgerSum == cash;

    // Kekurangan UJ dibanding biaya wajib (supir nombok)
    public static long UjShortfall(long ujGiven, long costEst) => Math.Max(0, costEst - ujGiven);

    public static double Margin(long net, long income) => income <= 0 ? 0 : (double)net / income;

    public static string DedicatedFormula(long monthlyPay, int target, int done) =>
        done >= target
            ? $"target tercapai ({done}/{target}): bayar penuh {Rules.Rp(monthlyPay)}"
            : $"target kurang ({done}/{target}): 80% x {Rules.Rp(monthlyPay)} x {done}/{target} = {Rules.Rp(Rules.DedicatedPay(monthlyPay, target, done))}, reputasi -5";

    public record CloseSim(long EndCash, long PayrollPaid, long Arrears, int LoansPaid, int LoansMissed);

    // Urutan sama persis dengan FinanceSvc.CloseMonth:
    // pemasukan borongan -> gaji (utuh atau menunggak) -> sewa -> premi -> cicilan satu per satu
    public static CloseSim SimulateClose(long cash, long dedicatedIncome, long payroll, long rent, long premium, IEnumerable<long> installments)
    {
        long c = cash + dedicatedIncome;
        long paid = 0, arrears = 0;
        if (c >= payroll) { c -= payroll; paid = payroll; } else arrears = payroll;
        c -= rent; c -= premium;
        int ok = 0, missed = 0;
        foreach (var i in installments) { if (c >= i) { c -= i; ok++; } else missed++; }
        return new CloseSim(c, paid, arrears, ok, missed);
    }

    public static long MinBalanceNeeded(long payroll, long rent, long premium, IEnumerable<long> installments) =>
        payroll + rent + premium + installments.Sum();

    public static string CashStatus(long endCash) =>
        Rules.IsCashBankrupt(endCash) ? "BANGKRUT" : endCash < 0 ? "DEFISIT" : "AMAN";
}