using Nusantara.Models;
using Nusantara.Services;
using Xunit;

namespace Nusantara.Tests;

public class EconomyV4Tests
{
    static Truck NewTruck(int tier = 1) => new Truck
    {
        Tier = tier, OilPct = 100, BrakePct = 100, TirePct = 100, FilterPct = 100, EnginePct = 100, IsEuro = false
    };

    [Fact]
    public void New_account_gets_1_5_billion_and_tier1_limit_is_5()
    {
        Assert.Equal(1_500_000_000L, EconomySvc.StartingCash);
        Assert.Equal(5, FleetSvc.MaxTrucks(1));
        Assert.Equal(12, FleetSvc.MaxTrucks(2));
    }

    [Theory]
    [InlineData(100, "PICKUP")]
    [InlineData(950, "PICKUP")]
    [InlineData(951, "CDE")]
    [InlineData(3_500, "CDE")]
    [InlineData(5_500, "CDD")]
    [InlineData(9_500, "FUSO")]
    [InlineData(21_000, "TRONTON")]
    [InlineData(21_001, "HEAVY")]
    public void Contract_class_follows_weight(int kg, string expected) =>
        Assert.Equal(expected, EconomySvc.ClassForWeight(kg));

    [Theory]
    [InlineData(900, "PICKUP")]
    [InlineData(4_200, "CDE")]
    [InlineData(6_500, "CDD")]
    [InlineData(11_000, "FUSO")]
    [InlineData(22_000, "TRONTON")]
    [InlineData(42_000, "HEAVY")]
    public void Truck_class_follows_capacity(int load, string expected) =>
        Assert.Equal(expected, EconomySvc.ClassForTruck(load));

    [Fact]
    public void Design_example_cde_200km_at_4000_per_km_is_800k()
    {
        var q = EconomySvc.MakeQuote("CDE", 200, 1.0, 0.5, 0, false);
        Assert.Equal(4_000, q.Rate);
        Assert.Equal(800_000, q.Fee);
    }

    [Fact]
    public void Quote_never_below_minimum_dispatch()
    {
        foreach (var b in EconomySvc.Bands)
            Assert.True(EconomySvc.MakeQuote(b.Code, 1, 1.0, 0.0, 0, false).Fee >= b.MinDispatch);
    }

    [Fact]
    public void Rate_stays_inside_band_for_any_random_value()
    {
        foreach (var b in EconomySvc.Bands)
            foreach (var u in new[] { -5.0, 0.0, 0.3, 1.0, 9.0 })
            {
                long r = EconomySvc.PickRate(b, u);
                Assert.InRange(r, b.MinPerKm, b.MaxPerKm);
            }
    }

    [Fact]
    public void Market_factor_is_clamped()
    {
        var hi = EconomySvc.MakeQuote("TRONTON", 1000, 99.0, 0.5, 0, false);
        var lo = EconomySvc.MakeQuote("TRONTON", 1000, 0.01, 0.5, 0, false);
        Assert.Equal(EconomySvc.MarketMax, hi.Market);
        Assert.Equal(EconomySvc.MarketMin, lo.Market);
    }

    [Fact]
    public void Same_inputs_give_same_quote_so_fee_is_never_redrawn()
    {
        var a = EconomySvc.MakeQuote("CDD", 300, 1.1, 0.42, 1_000_000, false);
        var b = EconomySvc.MakeQuote("CDD", 300, 1.1, 0.42, 1_000_000, false);
        Assert.Equal(a, b);
    }

    [Fact]
    public void B3_premium_and_ferry_surcharge_are_separate_components()
    {
        var plain = EconomySvc.MakeQuote("TRONTON", 1000, 1.0, 0.5, 0, false);
        var b3 = EconomySvc.MakeQuote("TRONTON", 1000, 1.0, 0.5, 0, true);
        Assert.True(b3.ServiceFee > plain.ServiceFee);
        var ferry = EconomySvc.MakeQuote("TRONTON", 1000, 1.0, 0.5, 1_000_000, false);
        Assert.Equal(plain.ServiceFee, ferry.ServiceFee);
        Assert.Equal(1_300_000, ferry.FerrySurcharge);
        Assert.Equal(ferry.ServiceFee + ferry.FerrySurcharge, ferry.Fee);
    }

    [Fact]
    public void Weight_does_not_change_fee_inside_the_same_class()
    {
        var a = EconomySvc.MakeQuote(EconomySvc.ClassForWeight(2_000), 200, 1.0, 0.5, 0, false);
        var b = EconomySvc.MakeQuote(EconomySvc.ClassForWeight(3_400), 200, 1.0, 0.5, 0, false);
        Assert.Equal(a.Fee, b.Fee);
    }

    [Fact]
    public void Billed_km_counts_sea_as_half()
    {
        Assert.Equal(150, EconomySvc.BilledKm(100, 100));
        Assert.Equal(0, EconomySvc.BilledKm(-5, -5));
    }

    [Fact]
    public void Wear_reserve_is_zero_for_zero_km_and_grows_with_distance()
    {
        var t = NewTruck();
        Assert.Equal(0, EconomySvc.WearReserve(t, 0, 0, 50, "DEX", 1.0));
        long a = EconomySvc.WearReserve(t, 100, 0, 50, "DEX", 1.0);
        long b = EconomySvc.WearReserve(t, 200, 0, 50, "DEX", 1.0);
        Assert.True(a > 0);
        Assert.True(b > a);
        Assert.True(EconomySvc.WearReserve(t, 100, 3, 50, "DEX", 1.0) > a);
        Assert.True(EconomySvc.WearReserve(t, 100, 0, 50, "BIO", 1.0) > a);
    }

    [Fact]
    public void Wear_reserve_never_exceeds_remaining_component_life()
    {
        var t = NewTruck(); t.OilPct = 0; t.BrakePct = 0; t.TirePct = 0; t.FilterPct = 0; t.EnginePct = 0;
        Assert.Equal(0, EconomySvc.WearReserve(t, 500, 5, 50, "BIO", 2.0));
    }

    [Fact]
    public void Rules_Wear_still_rounds_like_before()
    {
        var w = Rules.Wear(NewTruck(), 100, 0, 50, "DEX");
        Assert.Equal((99, 98, 99, 99, 100), w);
    }

    [Fact]
    public void Service_backlog_counts_only_components_under_60()
    {
        Assert.Equal(0, EconomySvc.ServiceBacklog(100, 100, 100, 100, 100, 1, 1.0));
        Assert.Equal(0, EconomySvc.ServiceBacklog(60, 60, 60, 60, 60, 1, 2.0));
        Assert.Equal(Rules.PartPrice("BRAKE", 1), EconomySvc.ServiceBacklog(100, 10, 100, 100, 100, 1, 1.0));
        Assert.Equal(Rules.PartPrice("BRAKE", 1) * 2, EconomySvc.ServiceBacklog(100, 10, 100, 100, 100, 1, 2.0));
    }

    [Fact]
    public void Overhead_is_fixed_cost_split_across_planned_trips()
    {
        Assert.Equal(1_000_000, EconomySvc.OverheadPerTrip(22_000_000, 1));
        Assert.Equal(500_000, EconomySvc.OverheadPerTrip(22_000_000, 2));
        Assert.Equal(0, EconomySvc.OverheadPerTrip(-5, 3));
        Assert.Equal(22_000_000 / EconomySvc.PlannedTripsPerTruckMonth, EconomySvc.OverheadPerTrip(22_000_000, 0));
    }

    [Fact]
    public void Driver_monthly_includes_bpjs()
    {
        Assert.Equal(3_200_000 + (long)(3_200_000 * 0.1024), EconomySvc.DriverMonthly("A"));
        Assert.True(EconomySvc.DriverMonthly("B2") > EconomySvc.DriverMonthly("B1"));
    }

    [Fact]
    public void Purchase_summary_adds_up_and_warns_when_cash_is_thin()
    {
        var ok = EconomySvc.Purchase(1_500_000_000, 190_000_000, "A", 900, 0, Array.Empty<int>(), 0);
        Assert.Equal(1_310_000_000, ok.CashAfter);
        Assert.False(ok.BelowReserve);
        Assert.Equal(ok.FixedNext + ok.UjReserve + ok.ServiceReserve, ok.Reserve);

        var thin = EconomySvc.Purchase(200_000_000, 190_000_000, "A", 900, 20_000_000, Array.Empty<int>(), 0);
        Assert.True(thin.BelowReserve);
    }

    [Fact]
    public void Five_starter_trucks_fit_in_starting_cash()
    {
        long total = 215_000_000L * 5;
        Assert.True(EconomySvc.StartingCash - total > 0);
    }

    [Fact]
    public void Operating_reserve_sums_parts()
    {
        long r = EconomySvc.OperatingReserve(10_000_000, new[] { 900, 4_200 }, 1_000_000);
        Assert.Equal(10_000_000 + 2 * 120_000 + 2 * 400_000 + 1_000_000, r);
    }
}