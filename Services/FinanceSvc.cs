using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

public record Bills(long Payroll, long Rent, long Premium);

public class ClosePreview
{
    public int Day, NextDay, DaysLeft;
    public long Cash, UjOnRoad, DedicatedNow, DedicatedFull, Debt, MinNeeded;
    public Bills Bills = new(0, 0, 0);
    public List<Loan> Loans = new();
    public Accounting.CloseSim NoIncome = null!, Current = null!, Full = null!;
}

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
        Db.Post(c, tx, pid, amount, "PINJAMAN_CAIR", $"Pinjaman {months} bulan");
        tx.Commit();
        return $"Pinjaman {Rules.Rp(amount)} cair. Cicilan {Rules.Rp(inst)}/bulan x {months} = total bayar {Rules.Rp(inst * months)}. Gagal bayar = truk disita!";
    }

    public static string Repay(int pid, int loanId)
    {
        using var c = Db.Open(); using var tx = c.BeginTransaction();
        var l = c.QueryFirstOrDefault<Loan>("SELECT * FROM loans WHERE id=@loanId AND player_id=@pid", new { loanId, pid }, tx);
        if (l == null) return "Pinjaman tidak ada.";
        if (!Db.Spend(c, tx, pid, l.Remaining, "PELUNASAN_PINJAMAN", $"Pinjaman #{loanId}")) return $"Uang kurang untuk pelunasan ({Rules.Rp(l.Remaining)}).";
        if (c.Execute("DELETE FROM loans WHERE id=@loanId AND player_id=@pid", new { loanId, pid }, tx) != 1) return "Pinjaman sudah tidak ada.";
        tx.Commit();
        return "Pinjaman lunas.";
    }

    // Tagihan tutup buku: dipakai preview DAN CloseMonth supaya angkanya selalu sama.
    public static Bills ComputeBills(Player p, List<Truck> fleet, List<Driver> staff, int day)
    {
        long payroll = Rules.Payroll(staff.Select(d => d.Salary), p.Mechanics, p.Manager, p.Arrears);
        long rent = p.PoolType == "SEWA" ? 6_000_000 : 0;
        long prem = 0;
        foreach (var t in fleet)
        {
            long v = Rules.TruckValue(t, day);
            if (t.Insurance == "ALLRISK") prem += (long)(v * 0.035 / 12 * p.PremiumPenalty);
            else if (t.Insurance == "TLO") prem += (long)(v * 0.01 / 12 * p.PremiumPenalty);
            if (t.CargoIns) prem += (long)(1_500_000 * Rules.ClassFactor(t.Tier) * p.PremiumPenalty);
        }
        return new Bills(payroll, rent, prem);
    }

    public static ClosePreview Preview(int pid)
    {
        var p = PlayerSvc.Get(pid); int day = PlayerSvc.Day(p);
        var fleet = FleetSvc.Fleet(pid); var staff = HrSvc.Staff(pid); var loans = Loans(pid);
        using var c = Db.Open();
        var ded = c.Query<Contract>("SELECT * FROM contracts WHERE player_id=@pid AND kind='DEDICATED' AND status='ACTIVE'", new { pid }).ToList();
        var bills = ComputeBills(p, fleet, staff, day);
        var inst = loans.Select(l => l.Installment).ToList();
        long now = ded.Sum(d => Rules.DedicatedPay(d.MonthlyPay, d.TripsPerMonth, d.TripsDoneMonth));
        long full = ded.Sum(d => d.MonthlyPay);

        return new ClosePreview
        {
            Day = day, NextDay = p.NextMonthDay, DaysLeft = Math.Max(0, p.NextMonthDay - day), Cash = p.Cash,
            UjOnRoad = c.ExecuteScalar<long>("SELECT COALESCE(SUM(uj_given),0) FROM trips WHERE player_id=@pid AND status='RUNNING'", new { pid }),
            DedicatedNow = now, DedicatedFull = full, Debt = loans.Sum(l => l.Remaining), Bills = bills, Loans = loans,
            MinNeeded = Accounting.MinBalanceNeeded(bills.Payroll, bills.Rent, bills.Premium, inst),
            NoIncome = Accounting.SimulateClose(p.Cash, 0, bills.Payroll, bills.Rent, bills.Premium, inst),
            Current = Accounting.SimulateClose(p.Cash, now, bills.Payroll, bills.Rent, bills.Premium, inst),
            Full = Accounting.SimulateClose(p.Cash, full, bills.Payroll, bills.Rent, bills.Premium, inst)
        };
    }

    public static List<string> CloseMonth(int pid)
    {
        var log = new List<string> { "=== TUTUP BUKU BULANAN ===" };
        var p = PlayerSvc.Get(pid); int day = PlayerSvc.Day(p); var R = Rules.Rng;
        var fleet = FleetSvc.Fleet(pid); var staff = HrSvc.Staff(pid);

        // Seluruh tutup buku satu transaksi: kalau gagal di tengah, tidak ada yang berubah.
        using var c = Db.Open(); using var tx = c.BeginTransaction();

        // Klaim bulan ini lebih dulu: proses ganda/ulang tidak akan membayar dua kali.
        if (c.Execute("UPDATE players SET next_month_day=next_month_day+30 WHERE id=@pid AND next_month_day=@nm", new { pid, nm = p.NextMonthDay }, tx) != 1)
            return new List<string>();

        var loans = c.Query<Loan>("SELECT * FROM loans WHERE player_id=@pid", new { pid }, tx).ToList();
        long cash = c.ExecuteScalar<long>("SELECT cash FROM players WHERE id=@pid", new { pid }, tx);
        int repD = 0;

        // 1) Kontrak borongan. Trip yang masih berjalan TIDAK dihitung untuk target (hanya trip selesai).
        foreach (var d in c.Query<Contract>("SELECT * FROM contracts WHERE player_id=@pid AND kind='DEDICATED' AND status='ACTIVE'", new { pid }, tx).ToList())
        {
            long pay = Rules.DedicatedPay(d.MonthlyPay, d.TripsPerMonth, d.TripsDoneMonth);
            if (d.TripsDoneMonth < d.TripsPerMonth) repD -= 5;
            cash = Db.Post(c, tx, pid, pay, "PEMBAYARAN_BORONGAN", $"{d.Client} bulan ke-{d.MonthsDone + 1}");
            bool end = d.MonthsDone + 1 >= d.MonthsTotal;
            if (end) repD += 10;
            c.Execute("UPDATE contracts SET months_done=months_done+1,trips_done_month=0,status=@s WHERE id=@id", new { s = end ? "ENDED" : "ACTIVE", id = d.Id }, tx);
            log.Add($"{d.Client}: {Accounting.DedicatedFormula(d.MonthlyPay, d.TripsPerMonth, d.TripsDoneMonth)}{(end ? " (kontrak SELESAI, reputasi +10)" : "")}.");
        }

        // 2) Gaji + BPJS + mekanik + manajer (+tunggakan). Kalau kas tidak cukup, SELURUH tagihan jadi tunggakan bulan depan.
        var bills = ComputeBills(p, fleet, staff, day);
        long arrears = 0;
        if (cash >= bills.Payroll)
        {
            cash = Db.Post(c, tx, pid, -bills.Payroll, "GAJI_STAF", "Gaji, BPJS, mekanik, manajer");
            log.Add($"Gaji, BPJS & staf dibayar: {Rules.Rp(bills.Payroll)}.");
        }
        else
        {
            arrears = bills.Payroll; log.Add($"GAJI TERLAMBAT! Tunggakan {Rules.Rp(bills.Payroll)} ditagih lagi bulan depan. Moral semua supir turun.");
            foreach (var d in staff)
            {
                bool quit = d.Status == "IDLE" && R.NextDouble() < 0.2;
                c.Execute("UPDATE drivers SET morale=GREATEST(0,morale-20),status=@s WHERE id=@id", new { s = quit ? "QUIT" : d.Status, id = d.Id }, tx);
                if (quit) { c.Execute("UPDATE trucks SET driver_id=NULL WHERE driver_id=@id", new { id = d.Id }, tx); log.Add($"{d.Name} resign karena gaji telat."); }
            }
        }

        // 3) Sewa pool + premi asuransi
        cash = Db.Post(c, tx, pid, -bills.Rent, "SEWA_POOL", "Sewa pool bulanan");
        cash = Db.Post(c, tx, pid, -bills.Premium, "PREMI_ASURANSI", $"Premi (penalty x{p.PremiumPenalty:0.00})");
        log.Add($"Sewa pool {Rules.Rp(bills.Rent)}, premi asuransi {Rules.Rp(bills.Premium)} (penalty x{p.PremiumPenalty:0.00}).");

        // 4) Cicilan bank & penyitaan
        bool bankrupt = false; var seized = new HashSet<int>();
        foreach (var l in loans)
        {
            if (cash >= l.Installment)
            {
                cash = Db.Post(c, tx, pid, -l.Installment, "CICILAN_BANK", $"Pinjaman #{l.Id}");
                int left = l.MonthsLeft - 1;
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

        c.Execute(@"UPDATE players SET arrears=@arr,premium_penalty=@pen,bankrupt=@b,
            rep_delivery=GREATEST(0,LEAST(400,rep_delivery+@rep)) WHERE id=@pid",
            new { arr = arrears, pen, b = bankrupt, rep = repD, pid }, tx);
        tx.Commit();
        log.Add($"Saldo akhir setelah tutup buku: {Rules.Rp(cash)}.");
        return log;
    }
}