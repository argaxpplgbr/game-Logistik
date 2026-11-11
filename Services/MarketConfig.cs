
namespace Nusantara.Services;

public static class MarketConfig
{
    public const int TargetOpenSpot = 12;
    public const int TargetOpenDedicated = 3;

    public const int SpotBidWindowDays = 2;
    public const int DedicatedBidWindowDays = 3;
    public const int SpotLifetimeDays = 6;
    public const int DedicatedLifetimeDays = 12;

    public const double MinPriceFactor = 0.60;
    public const double MaxPriceFactor = 1.10;

    public const int InternationalTier = 4;
    public const bool InternationalGenerationEnabled = false;

    // Lima perusahaan AI dengan karakter berbeda.
    public const int AiCompanyCount = 5;

    public const double WeightPrice = 0.50;
    public const double WeightReputation = 0.25;
    public const double WeightCapacity = 0.10;
    public const double WeightReliability = 0.15;

    public const int MaxCatchUpDays = 10;

    public static string TierName(int tier) => tier switch
    {
        1 => "Lokal",
        2 => "Regional",
        3 => "Nasional",
        4 => "Internasional",
        _ => $"Tier {tier}"
    };
}
