using Nusantara.Services;
using Xunit;

namespace Nusantara.Tests;

public class AccountingTests
{
    [Fact]
    public void Close_pays_everything_when_cash_is_enough()
    {
        var s = Accounting.SimulateClose(100_000_000, 0, 10_000_000, 6_000_000, 1_000_000, new long[] { 5_000_000 });
        Assert.Equal(78_000_000, s.EndCash);
        Assert.Equal(10_000_000, s.PayrollPaid);
        Assert.Equal(0, s.Arrears);
        Assert.Equal(1, s.LoansPaid);
        Assert.Equal(0, s.LoansMissed);
    }

    [Fact]
    public void Close_without_income_creates_arrears_and_missed_loan()
    {
        var s = Accounting.SimulateClose(5_000_000, 0, 10_000_000, 6_000_000, 1_000_000, new long[] { 5_000_000 });
        Assert.Equal(10_000_000, s.Arrears);
        Assert.Equal(0, s.PayrollPaid);
        Assert.Equal(-2_000_000, s.EndCash);
        Assert.Equal(1, s.LoansMissed);
    }

    [Fact]
    public void Dedicated_income_can_rescue_payroll()
    {
        var s = Accounting.SimulateClose(5_000_000, 20_000_000, 10_000_000, 0, 0, Array.Empty<long>());
        Assert.Equal(0, s.Arrears);
        Assert.Equal(15_000_000, s.EndCash);
    }

    [Fact]
    public void Min_balance_sums_all_bills() =>
        Assert.Equal(22_000_000, Accounting.MinBalanceNeeded(10_000_000, 6_000_000, 1_000_000, new long[] { 5_000_000 }));

    [Fact]
    public void Cash_status_levels()
    {
        Assert.Equal("AMAN", Accounting.CashStatus(0));
        Assert.Equal("DEFISIT", Accounting.CashStatus(-1));
        Assert.Equal("BANGKRUT", Accounting.CashStatus(-100_000_001));
    }

    [Fact]
    public void Uj_shortfall_only_when_uj_below_cost()
    {
        Assert.Equal(0, Accounting.UjShortfall(1_000_000, 900_000));
        Assert.Equal(100_000, Accounting.UjShortfall(900_000, 1_000_000));
    }

    [Fact]
    public void Ledger_reconciles_only_when_equal()
    {
        Assert.True(Accounting.Reconciles(300_000_000, 300_000_000));
        Assert.False(Accounting.Reconciles(299_999_999, 300_000_000));
    }

    [Fact]
    public void Signed_shows_direction()
    {
        Assert.StartsWith("+", Accounting.Signed(1_000));
        Assert.StartsWith("-", Accounting.Signed(-1_000));
    }

    [Fact]
    public void Margin_handles_zero_income()
    {
        Assert.Equal(0.0, Accounting.Margin(100, 0), 6);
        Assert.Equal(0.25, Accounting.Margin(250, 1000), 6);
    }

    [Fact]
    public void Dedicated_formula_matches_pay()
    {
        Assert.Contains("bayar penuh", Accounting.DedicatedFormula(10_000_000, 4, 4));
        Assert.Contains("reputasi -5", Accounting.DedicatedFormula(10_000_000, 4, 2));
    }
}