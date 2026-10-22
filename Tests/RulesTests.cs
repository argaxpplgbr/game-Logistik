using Nusantara.Services;
using Xunit;

namespace Nusantara.Tests;

public class RulesTests
{
    // ---- Gaji / moral ----
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(-100)]
    [InlineData(-500)]
    [InlineData(51)]
    [InlineData(1000)]
    public void Raise_invalid_is_rejected(int pct) => Assert.NotNull(Rules.ValidateRaise(pct));

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    public void Raise_valid_is_accepted(int pct) => Assert.Null(Rules.ValidateRaise(pct));

    [Fact]
    public void Raise_morale_is_proportional_and_capped()
    {
        Assert.Equal(1, Rules.RaiseMorale(1));
        Assert.Equal(10, Rules.RaiseMorale(10));
        Assert.Equal(10, Rules.RaiseMorale(50));
    }

    [Fact]
    public void Raised_salary_goes_up()
    {
        Assert.Equal(3_300_000, Rules.RaisedSalary(3_000_000, 10));
    }

    [Fact]
    public void Payroll_ignores_negative_salary()
    {
        long pay = Rules.Payroll(new long[] { -5_000_000, 3_000_000 }, 0, false, 0);
        Assert.InRange(pay, 3_307_000, 3_308_000);   // 3.000.000 + BPJS 10,24%
    }

    [Fact]
    public void Payroll_includes_mechanics_manager_and_arrears()
    {
        long pay = Rules.Payroll(Array.Empty<long>(), 2, true, 1_000_000);
        Assert.Equal(2 * 5_000_000 + 15_000_000 + 1_000_000, pay);
    }

    [Theory]
    [InlineData(79)]
    [InlineData(131)]
    [InlineData(-5)]
    public void Offer_out_of_range_is_rejected(int pct) => Assert.NotNull(Rules.ValidateOffer(pct));

    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(130)]
    public void Offer_in_range_is_accepted(int pct) => Assert.Null(Rules.ValidateOffer(pct));

    [Fact]
    public void Driver_cap_is_trucks_plus_one() => Assert.Equal(3, Rules.MaxDrivers(2));

    // ---- Asuransi ----
    [Fact]
    public void Claim_without_policy_pays_nothing() =>
        Assert.Equal(0, Rules.InsuranceClaim(true, 100_000_000, 5_000_000, 0, "NONE", false));

    [Fact]
    public void Claim_unknown_policy_pays_nothing() =>
        Assert.Equal(0, Rules.InsuranceClaim(true, 100_000_000, 0, 0, "", false));

    [Fact]
    public void Claim_tlo_pays_on_total_loss() =>
        Assert.InRange(Rules.InsuranceClaim(true, 100_000_000, 0, 0, "TLO", false), 79_999_999, 80_000_000);

    [Fact]
    public void Claim_tlo_ignores_ordinary_damage() =>
        Assert.Equal(0, Rules.InsuranceClaim(false, 100_000_000, 5_000_000, 0, "TLO", false));

    [Fact]
    public void Claim_allrisk_pays_damage() =>
        Assert.InRange(Rules.InsuranceClaim(false, 100_000_000, 5_000_000, 0, "ALLRISK", false), 4_499_999, 4_500_000);

    [Fact]
    public void Claim_cargo_insurance_pays_cargo_loss() =>
        Assert.InRange(Rules.InsuranceClaim(false, 100_000_000, 0, 10_000_000, "NONE", true), 8_499_999, 8_500_000);

    // ---- Kontrak borongan ----
    [Fact]
    public void Slots_count_finished_and_running_trips()
    {
        Assert.Equal(1, Rules.DedicatedSlotsLeft(4, 2, 1));
        Assert.Equal(0, Rules.DedicatedSlotsLeft(4, 2, 2));
        Assert.Equal(0, Rules.DedicatedSlotsLeft(4, 4, 0));
        Assert.Equal(0, Rules.DedicatedSlotsLeft(4, 3, 3));
    }

    [Fact]
    public void Dedicated_pay_is_full_when_target_met() =>
        Assert.Equal(10_000_000, Rules.DedicatedPay(10_000_000, 4, 4));

    [Fact]
    public void Dedicated_pay_is_cut_when_target_missed() =>
        Assert.InRange(Rules.DedicatedPay(10_000_000, 4, 2), 3_999_999, 4_000_000);

    [Fact]
    public void Overload_only_for_spot()
    {
        Assert.True(Rules.OverloadAllowed("SPOT"));
        Assert.False(Rules.OverloadAllowed("DEDICATED"));
    }

    // ---- UJ, event, bangkrut, risiko ----
    [Fact]
    public void Uj_standard_is_base_cost_times_1_1() =>
        Assert.InRange(Rules.UjStd(1_000_000), 1_099_999, 1_100_000);

    [Fact]
    public void Event_lasts_exactly_the_configured_days()
    {
        int last = Rules.EventLastDay(10, 3);
        Assert.Equal(12, last);
        Assert.Equal(3, last - 10 + 1);
    }

    [Fact]
    public void Bankruptcy_threshold()
    {
        Assert.False(Rules.IsCashBankrupt(-100_000_000));
        Assert.True(Rules.IsCashBankrupt(-100_000_001));
    }

    [Fact]
    public void AtLeastOne_combines_probabilities()
    {
        Assert.Equal(0.75, Rules.AtLeastOne(new[] { 0.5, 0.5 }), 6);
        Assert.Equal(0.0, Rules.AtLeastOne(Array.Empty<double>()), 6);
    }

    [Fact]
    public void Brake_failure_gets_worse_when_pads_are_worn()
    {
        Assert.Equal(0.35, Rules.BrakeFailProb(10), 6);
        Assert.Equal(0.03, Rules.BrakeFailProb(20), 6);
        Assert.Equal(0.003, Rules.BrakeFailProb(80), 6);
    }

    [Fact]
    public void Security_lowers_theft_and_kawal_lowers_weigh_fine()
    {
        Assert.True(Rules.TheftProb(2, "ORMAS", 1.0) < Rules.TheftProb(2, "NONE", 1.0));
        Assert.True(Rules.WeighProb(false, "KAWAL") < Rules.WeighProb(false, "DENDA"));
        Assert.True(Rules.WeighProb(true, "DENDA") > Rules.WeighProb(false, "DENDA"));
    }
}