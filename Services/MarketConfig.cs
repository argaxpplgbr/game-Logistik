namespace Nusantara.Services;

// Semua angka pasar bersama ada di sini (bukan tersebar di banyak file).
public static class MarketConfig
{
    // Jumlah peluang kontrak terbuka yang dijaga pasar.
    public const int TargetOpenSpot = 12;
    public const int TargetOpenDedicated = 3;

    // Lama penawaran dibuka (hari game) dan umur kontrak sebelum kedaluwarsa.
    public const int SpotBidWindowDays = 2;
    public const int DedicatedBidWindowDays = 3;
    public const int SpotLifetimeDays = 6;
    public const int DedicatedLifetimeDays = 12;

    // Batas harga penawaran terhadap tarif acuan kontrak.
    public const double MinPriceFactor = 0.60;
    public const double MaxPriceFactor = 1.10;

    // Tier 4 = Internasional. Layanan internasional hanya lewat kontrak scope INTERNATIONAL.
    public const int InternationalTier = 4;
    // Belum ada simpul peta luar negeri, jadi pasar belum membuat kontrak internasional.
    public const bool InternationalGenerationEnabled = false;

    // Jumlah perusahaan AI yang dijaga ada di setiap dunia.
    public const int AiCompanyCount = 3;

    // Bobot penilaian penawaran (jumlah = 1.0).
    public const double WeightPrice = 0.50, WeightReputation = 0.25, WeightCapacity = 0.10, WeightReliability = 0.15;

    // Maksimum hari yang dikejar saat game dibuka kembali (sama seperti WorldSvc.Tick).
    public const int MaxCatchUpDays = 10;

    public static string TierName(int tier) => tier switch
    {
        1 => "Lokal", 2 => "Regional", 3 => "Nasional", 4 => "Internasional", _ => $"Tier {tier}"
    };
}