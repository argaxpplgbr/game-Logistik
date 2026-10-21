using Nusantara.Models;

namespace Nusantara.Services;

public static class Rules
{
    // 1 = waktu asli GDD (100 km = 15 menit). 20 = dipercepat 20x untuk testing.
    public const double DevSpeed = 20;
    public static double DayMinutes => 30.0 / DevSpeed;   // 1 hari game = 30 menit real (dibagi DevSpeed)
    public const int BioPrice = 6800, DexPrice = 13500;   // per liter
    public static readonly Random Rng = new();

    public static string Rp(long n) => "Rp " + n.ToString("N0");
    public static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));

    public static double MoraleSpeed(int m) => m >= 70 ? 0.9 : m < 30 ? 1.1 : 1.0;
    public static double MoraleFuel(int m) => m >= 70 ? 1.08 : m < 30 ? 0.92 : 1.0;
    public static double MoraleWear(int m) => m >= 70 ? 0.85 : m < 30 ? 1.2 : 1.0;
    public static double MoraleRisk(int m) => m >= 70 ? 0.8 : m < 30 ? 1.5 : 1.0;
    public static int SimRank(string s) => s == "A" ? 1 : s == "B1" ? 2 : 3;

    // Menit game (sebelum dibagi DevSpeed). Laut = setengah kecepatan darat.
    public static double TravelMinutes(double roadKm, double ferryKm, Driver d) =>
        (roadKm / 100.0 * 15 + ferryKm / 100.0 * 30) * (1.1 - d.Speed * 0.02) * MoraleSpeed(d.Morale);

    public static double Liters(double km, Truck t, int load, Driver d)
    {
        double eff = t.KmPerL * (1.15 - 0.25 * Math.Min(1.2, (double)load / Math.Max(1, t.MaxLoad)))
                     * (0.9 + d.Eco * 0.02) * MoraleFuel(d.Morale);
        return km / eff;
    }

    public static double FuelPrice(string type, bool depot, double mult) =>
        (type == "DEX" ? DexPrice : BioPrice) * (depot ? 0.85 : 1.0) * mult;

    public static long TollCost(double tollKm, int axles) => (long)(tollKm * 650 * (axles >= 3 ? 1.8 : 1.0));

    public static long FerryTicket(Edge e, Truck t) =>
        (long)(e.FerryFee * (t.Tier == 1 ? 0.25 : t.Tier == 2 ? 0.6 : 1.0) * (t.Axles >= 3 ? 1.3 : 1.0));

    public static double ClassFactor(int tier) => tier == 1 ? 0.3 : tier == 2 ? 1.0 : 2.2;

    public static long PartPrice(string comp, int tier)
    {
        long b = comp switch { "OIL" => 1_200_000, "BRAKE" => 2_500_000, "TIRE" => 12_000_000, "FILTER" => 800_000, _ => 25_000_000 };
        return (long)(b * ClassFactor(tier));
    }

    public static long TruckValue(Truck t, int day) =>
        (long)(t.PurchasePrice * Math.Max(0.3, 1 - 0.0008 * Math.Max(0, day - t.BoughtDay)));

    public static (int oil, int brake, int tire, int filter, int engine) Wear(Truck t, double km, int steep, int morale, string fuel)
    {
        double u = km / 100.0 * MoraleWear(morale);
        bool bio = fuel == "BIO";
        double brake = u * 2 + steep * 2, tire = u * 1, oil = u * 1, filter = u * (bio ? 2.4 : 1.2), eng = u * (bio ? 0.4 : 0.2);
        if (t.IsEuro && bio) { eng = u * 1.5; filter = u * 3; }   // truk Eropa wajib Dexlite
        return (Cl(t.OilPct - oil), Cl(t.BrakePct - brake), Cl(t.TirePct - tire), Cl(t.FilterPct - filter), Cl(t.EnginePct - eng));
    }

    static int Cl(double v) => (int)Math.Max(0, Math.Round(v));
}