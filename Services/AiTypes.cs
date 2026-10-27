using Nusantara.Models;

namespace Nusantara.Services;

// Tujuan jangka pendek dan panjang perusahaan AI.
public static class AiGoals
{
    // Jangka pendek
    public const string RaiseCash = "RAISE_CASH";
    public const string BuildFleet = "BUILD_FLEET";
    public const string CompleteContracts = "COMPLETE_CONTRACTS";
    public const string FillCapacity = "FILL_CAPACITY";
    // Jangka panjang
    public const string UpgradeTier = "UPGRADE_TIER";
    public const string ExpandFleet = "EXPAND_FLEET";
    public const string BuildReputation = "BUILD_REPUTATION";
}

// Jenis tindakan yang boleh diusulkan strategi. Hanya jenis di sini yang akan diproses.
public static class AiActions
{
    public const string Bid = "BID";
    public const string BuyTruck = "BUY_TRUCK";
    public const string InstallBody = "INSTALL_BODY";
    public const string Hire = "HIRE";
    public const string RefreshCandidates = "REFRESH_CANDIDATES";
    public const string Assign = "ASSIGN";
    public const string Dispatch = "DISPATCH";
    public const string Service = "SERVICE";
    public const string UpgradeTier = "UPGRADE_TIER";
    public const string RentPool = "RENT_POOL";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Bid, BuyTruck, InstallBody, Hire, RefreshCandidates, Assign, Dispatch, Service, UpgradeTier, RentPool
    };
}

// Konfigurasi perilaku AI di satu tempat.
public static class AiRules
{
    public const int MaxActionsPerDay = 15;
    public const long LowCash = 250_000_000;        // di bawah ini: tujuan jangka pendek RAISE_CASH
    public const long CriticalCash = 150_000_000;   // di bawah ini: RAISE_CASH langsung, tanpa menunggu konfirmasi
    public const double ProtocolCostFactor = 1.2;   // estimasi biaya trip = uang jalan rata-rata x 1.2

    // Harus sama dengan PlayerSvc.UpgradeTier. PlayerSvc tetap yang memvalidasi; angka ini hanya untuk perencanaan.
    public static long TierUpgradeCost(int tier) => tier == 1 ? 150_000_000 : 1_200_000_000;
}

// Kepribadian = bobot keputusan, bukan izin melanggar aturan.
public sealed record AiProfile(string Code, double MinMargin, double PriceFactor, int MaxBidsPerDay, long CashReserve, int FleetTarget, bool PremiumOnly);

public static class AiProfiles
{
    public static readonly AiProfile Hemat = new("HEMAT", 0.25, 0.97, 1, 400_000_000, 2, false);
    public static readonly AiProfile Agresif = new("AGRESIF", 0.08, 0.82, 3, 200_000_000, 4, false);
    public static readonly AiProfile Premium = new("PREMIUM", 0.30, 1.00, 2, 350_000_000, 2, true);
    public static readonly AiProfile Ekspansi = new("EKSPANSI", 0.12, 0.90, 2, 250_000_000, 4, false);
    public static readonly AiProfile Seimbang = new("SEIMBANG", 0.15, 0.93, 2, 300_000_000, 3, false);

    public static AiProfile Get(string code) => code switch
    {
        "HEMAT" => Hemat, "AGRESIF" => Agresif, "PREMIUM" => Premium, "EKSPANSI" => Ekspansi, _ => Seimbang
    };
}

// Usulan tindakan dari strategi. C# yang memvalidasi dan menjalankan; strategi hanya mengusulkan.
public sealed record AiProposal(int CompanyId, string Type, int Priority, string Reason, string ExpectedImpact, long EstCost,
                                int A = 0, int B = 0, long Price = 0, string Code = "");

// Potret data yang dilihat strategi. Tidak ada akses SQL: strategi hanya menerima data ini.
public sealed record AiTruck(int Id, string Status, string? Body, int Payload, int MaxLoad, string Sim, int? DriverId, int MinCondition)
{
    public bool HasDriver => DriverId != null;
}
public sealed record AiDriver(int Id, string Sim, string Status, long Salary, int Quality);
public sealed record AiRival(string Name, int Tier, int Reputation, int Trucks);

public sealed record AiSnapshot(
    int CompanyId, string Name, string Personality, int Day, long Cash, int Tier, int Reputation, string PoolType,
    string ShortGoal, string LongGoal,
    IReadOnlyList<AiTruck> Trucks, IReadOnlyList<AiDriver> Staff, IReadOnlyList<AiDriver> Candidates,
    IReadOnlyList<WorldContract> Market, IReadOnlyList<Contract> MyContracts,
    IReadOnlyDictionary<string, string[]> CargoBodies,
    IReadOnlyList<TruckModel> Models, IReadOnlyList<BodyType> Bodies, IReadOnlyList<AiRival> Rivals);

public interface IStrategyProvider
{
    string Name { get; }
    IReadOnlyList<AiProposal> Propose(AiSnapshot snapshot);
}