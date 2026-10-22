using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

public static class FinanceSvc
{
    public static List<Loan> Loans(int pid) { using var c = Db.Open(); return c.Query<Loan>("SELECT * FROM loans WHERE player_id=@pid", new { pid }).ToList(); }

    public static long MaxLoan(Player p)
    {
        using var c = Db.Open();
        long outstanding = c.ExecuteScalar<long?>("SELECT SUM(remaining) FROM loans WHERE player_id=@id", new { id = p.Id }) ?? 0;
        return Math.Max(0, (long)(250_000_000L * p.Tier * (1 + p.Stars)) - outstanding);
    }

    public static string TakeLoan(int pid, long amount, int months)
    {
        var p = PlayerSvc.Get(pid);
        if (months != 12 && months != 24 && months != 36) return "Tenor harus 12/24/36 bulan.";
        if (amount <= 0 || amount > MaxLoan(p)) return $"Plafon pinjaman maksimal {Rules.Rp(MaxLoan(p))}.";
        long inst = (long)Math.Ceiling(amount * (1 + 0.01 * months) / months);   // bunga flat 1%/bulan
        using var c = Db.Open(); using var tx = c.BeginTransaction();
        c.Execute("INSERT INTO loans(player_id,principal,remaining,installment,months_left) VALUES(@pid,@amount,@rem,@inst,@months)", new { pid, amount, rem = inst * months, inst, months }, tx);
        c.Execute("UPDATE players SET cash=cash+@amount WHERE id=@pid", new { amount, pid }, tx);
        tx.Commit();
        return $"Pinjaman {Rules.Rp(amount)} cair. Cicilan {Rules.Rp(inst)}/bulan x {months}. Gagal bayar = truk disita!";
    }

    public static string Repay(int pid, int loanId)
    {
        using var c = Db.Open(); using var tx = c.BeginTransaction();
        var l = c.QueryFirstOrDefault<Loan>("SELECT * FROM loans WHERE id=@loanId AND player_id=@pid", new { loanId, pid }, tx);
        if (l == null) return "Pinjaman tidak ada.";
        if (!Db.Spend(c, tx, pid, l.Remaining)) return $"Uang kurang untuk pelunasan ({Rules.Rp(l.Remaining)}).";
        c.Execute("DELETE FROM loans WHERE id=@loanId", new { loanId }, tx);
        tx.Commit();
        return "Pinjaman lunas.";
    }

    public static List<string> CloseMonth(int pid)
    {
        var log = new List<string> { "=== TUTUP BUKU BULANAN ===" };
        var p = PlayerSvc.Get(pid); int day = PlayerSvc.Day(p); var R = Rules.Rng;
        var fleet = FleetSvc.Fleet(pid); var staff = HrSvc.Staff(pid); var loans = Loans(pid);

        // Seluruh tutup buku satu transaksi: kalau gagal di tengah, tidak ada yang berubah.
        using var c = Db.Open(); using var tx = c.BeginTransaction();
        long cash = p.Cash; int repD = 0;

        // 1) Kontrak borongan (arus kas pasti)
        foreach (var d in c.Query<Contract>("SELECT * FROM contracts WHERE player_id=@pid AND kind='DEDICATED' AND status='ACTIVE'", new { pid }, tx).ToList())
        {
            long pay = Rules.DedicatedPay(d.MonthlyPay, d.TripsPerMonth, d.TripsDoneMonth);
            if (d.TripsDoneMonth < d.TripsPerMonth) { repD -= 5; log.Add($"{d.Client}: target {d.TripsPerMonth} trip tidak tercapai ({d.TripsDoneMonth}), bayaran dipotong, reputasi turun."); }
            cash += pay;
            bool end = d.MonthsDone + 1 >= d.MonthsTotal;
            if (end) repD += 10;
            c.Execute("UPDATE contracts SET months_done=months_done+1,trips_done_month=0,status=@s WHERE id=@id", new { s = end ? "ENDED" : "ACTIVE", id = d.Id }, tx);
            log.Add($"{d.Client}: terima {Rules.Rp(pay)}{(end ? " (kontrak SELESAI, reputasi +10)" : "")}.");
        }

        // 2) Gaji + BPJS + mekanik + manajer (+tunggakan). Gaji negatif tidak pernah dihitung sebagai pemasukan.
        long payroll = Rules.Payroll(staff.Select(d => d.Salary), p.Mechanics, p.Manager, p.Arrears);
        long arrears = 0;
        if (cash >= payroll) { cash -= payroll; log.Add($"Gaji, BPJS & staf dibayar: {Rules.Rp(payroll)}."); }
        else
        {
            arrears = payroll; log.Add($"GAJI TERLAMBAT! Tunggakan {Rules.Rp(payroll)}. Moral semua supir turun.");
            foreach (var d in staff)
            {
                bool quit = d.Status == "IDLE" && R.NextDouble() < 0.2;
                c.Execute("UPDATE drivers SET morale=GREATEST(0,morale-20),status=@s WHERE id=@id", new { s = quit ? "QUIT" : d.Status, id = d.Id }, tx);
                if (quit) { c.Execute("UPDATE trucks SET driver_id=NULL WHERE driver_id=@id", new { id = d.Id }, tx); log.Add($"{d.Name} resign karena gaji telat."); }
            }
        }

        // 3) Sewa pool + premi asuransi
        long rent = p.PoolType == "SEWA" ? 6_000_000 : 0; cash -= rent;
        long prem = 0;
        foreach (var t in fleet)
        {
            long v = Rules.TruckValue(t, day);
            if (t.Insurance == "ALLRISK") prem += (long)(v * 0.035 / 12 * p.PremiumPenalty);
            else if (t.Insurance == "TLO") prem += (long)(v * 0.01 / 12 * p.PremiumPenalty);
            if (t.CargoIns) prem += (long)(1_500_000 * Rules.ClassFactor(t.Tier) * p.PremiumPenalty);
        }
        cash -= prem;
        log.Add($"Sewa pool {Rules.Rp(rent)}, premi asuransi {Rules.Rp(prem)} (penalty x{p.PremiumPenalty:0.00}).");

        // 4) Cicilan bank & penyitaan
        bool bankrupt = false; var seized = new HashSet<int>();
        foreach (var l in loans)
        {
            if (cash >= l.Installment)
            {
                cash -= l.Installment; int left = l.MonthsLeft - 1;
                if (left <= 0) { c.Execute("DELETE FROM loans WHERE id=@id", new { id = l.Id }, tx); log.Add("Pinjaman lunas!"); }
                else c.Execute("UPDATE loans SET remaining=remaining-@i,months_left=@left,missed=0 WHERE id=@id", new { i = l.Installment, left, id = l.Id }, tx);
                log.Add($"Cicilan bank dibayar {Rules.Rp(l.Installment)}.");
                continue;
            }
            int missed = l.Missed + 1; log.Add($"GAGAL BAYAR CICILAN ({missed}x)!");
            long remaining = l.Remaining;
            if (missed >= 2)
            {
                var avail = fleet.Where(t => !seized.Contains(t.Id)).ToList();
                var vic = avail.Where(t => t.Status != "ON_TRIP").OrderByDescending(t => Rules.TruckValue(t, day)).FirstOrDefault()
                          ?? avail.OrderByDescending(t => Rules.TruckValue(t, day)).FirstOrDefault();
                if (vic != null)
                {
                    long v = (long)(Rules.TruckValue(vic, day) * 0.7); remaining -= v; seized.Add(vic.Id);
                    c.Execute("UPDATE trucks SET status='TOTALLOSS',driver_id=NULL WHERE id=@id", new { id = vic.Id }, tx);
                    log.Add($"BANK MENYITA {vic.ModelName}! Sisa utang dikurangi {Rules.Rp(v)}.");
                    missed = 1;
                }
            }
            if (remaining <= 0) c.Execute("DELETE FROM loans WHERE id=@id", new { id = l.Id }, tx);
            else c.Execute("UPDATE loans SET remaining=@r,missed=@m WHERE id=@id", new { r = remaining, m = missed, id = l.Id }, tx);
            if (missed >= 3) bankrupt = true;
        }
        if (Rules.IsCashBankrupt(cash)) bankrupt = true;
        double pen = Math.Max(1.0, p.PremiumPenalty - 0.05);

        c.Execute(@"UPDATE players SET cash=@cash,arrears=@arr,premium_penalty=@pen,bankrupt=@b,next_month_day=next_month_day+30,
            rep_delivery=GREATEST(0,LEAST(400,rep_delivery+@rep)) WHERE id=@pid",
            new { cash, arr = arrears, pen, b = bankrupt, rep = repD, pid }, tx);
        tx.Commit();
        return log;
    }
}