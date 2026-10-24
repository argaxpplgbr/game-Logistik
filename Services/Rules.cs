
using Nusantara.Models;

namespace Nusantara.Services;

public static class Rules
{
    public const double DevSpeed = 20;
    public static double DayMinutes => 30.0 / DevSpeed;

    public const int BioPrice = 6800, DexPrice = 13500;
    public static readonly Random Rng = new();

    public static string Rp(long n) => "Rp " + n.ToString("N0");
    public static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));

    public static double MoraleSpeed(int m) => m >= 70 ? 0.9 : m < 30 ? 1.1 : 1.0;
    public static double MoraleFuel(int m) => m >= 70 ? 1.08 : m < 30 ? 0.92 : 1.0;
    public static double MoraleWear(int m) => m >= 70 ? 0.85 : m < 30 ? 1.2 : 1.0;
    public static double MoraleRisk(int m) => m >= 70 ? 0.8 : m < 30 ? 1.5 : 1.0;

    public static int SimRank(string s) => s == "A" ? 1 : s == "B1" ? 2 : 3;

    public static double TravelMinutes(double roadKm, double ferryKm, Driver d) =>
        (roadKm / 100.0 * 15 + ferryKm / 100.0 * 30)
        * (1.1 - d.Speed * 0.02)
        * MoraleSpeed(d.Morale);

    public static double Liters(double km, Truck t, int load, Driver d)
    {
        double eff = t.KmPerL
            * (1.15 - 0.25 * Math.Min(1.2, (double)load / Math.Max(1, t.MaxLoad)))
            * (0.9 + d.Eco * 0.02)
            * MoraleFuel(d.Morale);

        return km / eff;
    }

    public static double FuelPrice(string type, bool depot, double mult) =>
        (type == "DEX" ? DexPrice : BioPrice)
        * (depot ? 0.85 : 1.0)
        * mult;

    public static long TollCost(double tollKm, int axles) =>
        (long)(tollKm * 650 * (axles >= 3 ? 1.8 : 1.0));

    public static long FerryTicket(Edge e, Truck t) =>
        (long)(e.FerryFee
        * (t.Tier == 1 ? 0.25 : t.Tier == 2 ? 0.6 : 1.0)
        * (t.Axles >= 3 ? 1.3 : 1.0));

    public static double ClassFactor(int tier) =>
        tier == 1 ? 0.3 : tier == 2 ? 1.0 : 2.2;

    public static long PartPrice(string comp, int tier)
    {
        long b = comp switch
        {
            "OIL" => 1_200_000,
            "BRAKE" => 2_500_000,
            "TIRE" => 12_000_000,
            "FILTER" => 800_000,
            _ => 25_000_000
        };

        return (long)(b * ClassFactor(tier));
    }

    public static long TruckValue(Truck t, int day) =>
        (long)(t.PurchasePrice * Math.Max(
            0.3,
            1 - 0.0008 * Math.Max(0, day - t.BoughtDay)
        ));

    public static (double oil, double brake, double tire, double filter, double engine)
        WearDrop(Truck t, double km, int steep, int morale, string fuel)
    {
        double u = km / 100.0 * MoraleWear(morale);
        bool bio = fuel == "BIO";

        double brake = u * 2 + steep * 2;
        double tire = u;
        double oil = u;
        double filter = u * (bio ? 2.4 : 1.2);
        double eng = u * (bio ? 0.4 : 0.2);

        if (t.IsEuro && bio)
        {
            eng = u * 1.5;
            filter = u * 3;
        }

        return (oil, brake, tire, filter, eng);
    }

    // Nama Wear menggunakan huruf W besar agar sesuai dengan TripSvc.cs.
    public static (int oil, int brake, int tire, int filter, int engine)
        Wear(Truck t, double km, int steep, int morale, string fuel)
    {
        var d = WearDrop(t, km, steep, morale, fuel);

        return (
            Cl(t.OilPct - d.oil),
            Cl(t.BrakePct - d.brake),
            Cl(t.TirePct - d.tire),
            Cl(t.FilterPct - d.filter),
            Cl(t.EnginePct - d.engine)
        );
    }

    static int Cl(double v) => (int)Math.Max(0, Math.Round(v));

    // ---- Gaji & rekrutmen ----
    public const int MaxRaisePct = 50;

    public static string? ValidateRaise(int pct) =>
        pct <= 0 ? "Persentase kenaikan gaji harus lebih dari 0." :
        pct > MaxRaisePct ? $"Kenaikan gaji maksimal {MaxRaisePct}% sekali naik." : null;

    public static int RaiseMorale(int pct) => Math.Clamp(pct, 0, 10);

    public static long RaisedSalary(long salary, int pct) =>
        salary * (100 + pct) / 100;

    public static string? ValidateOffer(int pct) =>
        pct < 80 ? "Tawaran gaji terlalu rendah, kandidat menolak." :
        pct > 130 ? "Tawaran gaji maksimal 130% dari permintaan." : null;

    public static int MaxDrivers(int trucks) => trucks + 1;

    public static long Payroll(IEnumerable<long> salaries, int mechanics, bool manager, long arrears) =>
        salaries.Sum(s =>
        {
            long x = Math.Max(0, s);
            return x + (long)(x * 0.1024);
        })
        + mechanics * 5_000_000L
        + (manager ? 15_000_000L : 0)
        + arrears;

    // ---- Asuransi ----
    public static long InsuranceClaim(
        bool total,
        long truckValue,
        long damage,
        long cargoLoss,
        string insurance,
        bool cargoIns)
    {
        bool tlo = insurance == "TLO";
        bool all = insurance == "ALLRISK";
        long claim = 0;

        if (total)
        {
            if (tlo || all)
                claim += (long)(truckValue * (tlo ? 0.8 : 0.9));
        }
        else if (damage > 0 && all)
        {
            claim += (long)(damage * 0.9);
        }

        if (cargoLoss > 0 && cargoIns)
            claim += (long)(cargoLoss * 0.85);

        return claim;
    }

    // ---- Kontrak ----
    public static int DedicatedSlotsLeft(int tripsPerMonth, int doneThisMonth, int running) =>
        Math.Max(0, tripsPerMonth - doneThisMonth - running);

    public static long DedicatedPay(long monthlyPay, int tripsPerMonth, int tripsDone) =>
        tripsDone >= tripsPerMonth
            ? monthlyPay
            : (long)(monthlyPay * 0.8 * tripsDone / Math.Max(1, tripsPerMonth));

    public static bool OverloadAllowed(string contractKind) =>
        contractKind == "SPOT";

    public static long UjStd(long baseCost) => (long)(baseCost * 1.1);

    // ---- Event & kebangkrutan ----
    public static int EventLastDay(int startDay, int days) =>
        startDay + Math.Max(1, days) - 1;

    public static bool IsCashBankrupt(long cash) =>
        cash < -100_000_000;

    // ---- Peluang risiko ----
    public static double BrakeFailProb(int brakePct) =>
        brakePct < 15 ? 0.35 : brakePct < 30 ? 0.03 : 0.003;

    public static double SecMult(string sec) =>
        sec == "ORMAS" ? 0.2 :
        sec == "RESTAREA" ? 0.4 : 1.0;

    public static double TheftProb(int rawan, string sec, double moraleRisk) =>
        rawan * 0.04 * SecMult(sec) * moraleRisk;

    public static double WeighProb(bool razia, string policy) =>
        0.45 * (razia ? 2.0 : 1.0)
        * (policy == "KAWAL" ? 0.3 : 1.0);

    public static double AtLeastOne(IEnumerable<double> probs)
    {
        double none = 1;

        foreach (var p in probs)
            none *= 1 - Math.Clamp(p, 0, 1);

        return 1 - none;
    }
}
