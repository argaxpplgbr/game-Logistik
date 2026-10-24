using Nusantara.Models;

namespace Nusantara.Services;

public record RateBand(string Code, string Name, long MinPerKm, long MaxPerKm, long MinDispatch, bool Assumed);

public record Quote(string VehicleClass, double BilledKm, long Rate, double Market, long ServiceFee, long FerrySurcharge, long Fee);

public record PurchaseView(long CashBefore, long Price, long CashAfter, long DriverMonthly, long PremiumAllRisk,
    long FirstUj, long FixedNext, long UjReserve, long ServiceReserve, long Reserve, bool BelowReserve);

// V4: SATU tempat untuk rumus ekonomi. Tidak ada akses database di file ini (murni, mudah dites).
public static class EconomySvc
{
    // ---------- Konfigurasi tuning (ubah di sini, lalu jalankan test) ----------
    public const long StartingCash = 1_500_000_000;
    public const int PlannedTripsPerTruckMonth = 22;
    public const double B3Premium = 1.25;
    public const double DedicatedDiscount = 0.85;
    public const double FerrySurchargeMult = 1.3;
    public const double MarketMin = 0.8, MarketMax = 1.6;
    public const double ServiceThreshold = 60;

    public static readonly RateBand[] Bands =
    {
        new RateBand("PICKUP",  "Pickup / van kecil",       2_500,  5_000,   500_000, true),
        new RateBand("CDE",     "Engkel CDE",               3_000,  5_000,   700_000, false),
        new RateBand("CDD",     "CDD 5-8 ton",              5_000,  9_000, 1_200_000, true),
        new RateBand("FUSO",    "Fuso / medium duty",       9_000, 14_000, 2_000_000, true),
        new RateBand("TRONTON", "Tronton / wingbox",       10_000, 19_000, 3_500_000, false),
        new RateBand("HEAVY",   "Prime mover / heavy duty",12_000, 22_000, 5_000_000, true),
    };

    public static RateBand Band(string code) => Bands.FirstOrDefault(b => b.Code == code) ?? Bands[0];
    public static int ClassRank(string code) => Math.Max(0, Array.FindIndex(Bands, b => b.Code == code));

    public static string ClassForWeight(int kg) =>
        kg <= 950 ? "PICKUP" : kg <= 3_500 ? "CDE" : kg <= 5_500 ? "CDD" : kg <= 9_500 ? "FUSO" : kg <= 21_000 ? "TRONTON" : "HEAVY";

    public static string ClassForTruck(int maxLoad) =>
        maxLoad <= 1_500 ? "PICKUP" : maxLoad <= 5_000 ? "CDE" : maxLoad <= 8_000 ? "CDD" : maxLoad <= 12_000 ? "FUSO" : maxLoad <= 25_000 ? "TRONTON" : "HEAVY";

    public static double BilledKm(double roadKm, double seaKm) => Math.Max(0, roadKm) + Math.Max(0, seaKm) * 0.5;

    public static long PickRate(RateBand b, double u) =>
        b.MinPerKm + (long)((b.MaxPerKm - b.MinPerKm) * Math.Clamp(u, 0.0, 1.0));

    static long RoundK(long v) => v / 1000 * 1000;

    // Tarif jasa = max(minimum dispatch, jarak tertagih x tarif/km x faktor pasar). Ongkos kapal terpisah.
    // u (0..1) dikirim dari luar supaya bisa dites. Dipanggil sekali saat kuotasi dibuat.
    public static Quote MakeQuote(string vehicleClass, double billedKm, double market, double u, long ferryFeeSum, bool b3)
    {
        var b = Band(vehicleClass);
        long rate = PickRate(b, u);
        double mkt = Math.Clamp(market, MarketMin, MarketMax);
        double raw = Math.Max(0, billedKm) * rate * mkt * (b3 ? B3Premium : 1.0);
        long service = Math.Max(b.MinDispatch, RoundK((long)Math.Round(raw)));
        long ferry = RoundK((long)Math.Round(Math.Max(0, ferryFeeSum) * FerrySurchargeMult));
        return new Quote(b.Code, billedKm, rate, mkt, service, ferry, service + ferry);
    }

    // ---------- Keausan: cadangan ekonomi, BUKAN transaksi kas ----------
    public static long PartCost(Truck t, string comp, double pct) =>
        (long)(Rules.PartPrice(comp, t.Tier) * pct / 100.0);

    public static long WearReserve(Truck t, double km, int steep, int morale, string fuel, double labor)
    {
        var d = Rules.WearDrop(t, km, steep, morale, fuel);
        double sum = PartCost(t, "OIL", Math.Min(d.oil, t.OilPct)) + PartCost(t, "BRAKE", Math.Min(d.brake, t.BrakePct))
                   + PartCost(t, "TIRE", Math.Min(d.tire, t.TirePct)) + PartCost(t, "FILTER", Math.Min(d.filter, t.FilterPct))
                   + PartCost(t, "ENGINE", Math.Min(d.engine, t.EnginePct));
        return (long)(sum * labor);
    }

    // Biaya servis semua komponen di bawah 60% (sama dengan tombol "servis semua").
    public static long ServiceBacklog(int oil, int brake, int tire, int filter, int engine, int tier, double labor)
    {
        double sum = 0;
        if (oil < ServiceThreshold) sum += Rules.PartPrice("OIL", tier);
        if (brake < ServiceThreshold) sum += Rules.PartPrice("BRAKE", tier);
        if (tire < ServiceThreshold) sum += Rules.PartPrice("TIRE", tier);
        if (filter < ServiceThreshold) sum += Rules.PartPrice("FILTER", tier);
        if (engine < ServiceThreshold) sum += Rules.PartPrice("ENGINE", tier);
        return (long)(sum * labor);
    }

    // ---------- Overhead & cadangan operasi ----------
    public static long OverheadPerTrip(long fixedMonthly, int trucks) =>
        Math.Max(0, fixedMonthly) / Math.Max(1, trucks * PlannedTripsPerTruckMonth);

    public static long AvgUj(string vehicleClass) => vehicleClass switch
    {
        "PICKUP" => 120_000, "CDE" => 400_000, "CDD" => 700_000, "FUSO" => 1_100_000, "TRONTON" => 2_500_000, _ => 4_000_000
    };

    public static long DriverMonthly(string sim)
    {
        long b = sim == "A" ? 3_200_000 : sim == "B1" ? 4_800_000 : 6_500_000;
        return b + (long)(b * 0.1024);
    }

    public static long OperatingReserve(long fixedNext, IEnumerable<int> activeTruckLoads, long serviceBacklog) =>
        fixedNext + activeTruckLoads.Sum(l => 2 * AvgUj(ClassForTruck(l))) + serviceBacklog;

    public static PurchaseView Purchase(long cash, long price, string sim, int maxLoad, long fixedMonthly,
        IEnumerable<int> activeTruckLoads, long serviceBacklog)
    {
        long driver = DriverMonthly(sim);
        long fixedNext = fixedMonthly + driver;
        var loads = activeTruckLoads.Append(maxLoad).ToList();
        long uj = loads.Sum(l => 2 * AvgUj(ClassForTruck(l)));
        long reserve = fixedNext + uj + serviceBacklog;
        long after = cash - price;
        long prem = (long)(price * 0.035 / 12);
        return new PurchaseView(cash, price, after, driver, prem, AvgUj(ClassForTruck(maxLoad)), fixedNext, uj, serviceBacklog, reserve, after < reserve);
    }
}