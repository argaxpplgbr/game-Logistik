using Nusantara.Models;

namespace Nusantara.Services;

public sealed record BidScore(double Total, double Price, double Reputation, double Capacity, double Reliability, string Detail);

// Penilaian penawaran yang transparan. Bobot ada di MarketConfig.
// Harga terendah tidak otomatis menang: reputasi, muatan truk, dan kondisi armada ikut dihitung.
public static class BidScoring
{
    public static BidScore Score(Player p, WorldContract wc, long price)
    {
        var fit = BidValidator.FitTrucks(p.Id, wc);

        double priceScore = Math.Clamp(wc.Fee / (double)Math.Max(1L, price), 0.0, 1.5) / 1.5;
        double repScore = Math.Min(Math.Max(p.Reputation, 0), 400) / 400.0;
        double maxPayload = fit.Count == 0 ? 0 : fit.Max(t => t.Payload);
        double capScore = fit.Count == 0 ? 0 : Math.Min(1.0, maxPayload / (Math.Max(1, wc.WeightKg) * 1.5));
        double relScore = fit.Count == 0 ? 0 : fit.Average(t => (t.OilPct + t.BrakePct + t.TirePct + t.FilterPct + t.EnginePct) / 500.0);

        double total = 100.0 * (MarketConfig.WeightPrice * priceScore + MarketConfig.WeightReputation * repScore
                              + MarketConfig.WeightCapacity * capScore + MarketConfig.WeightReliability * relScore);
        total = Math.Round(total, 2);

        string detail = $"harga {Rules.Rp(price)} ({price * 100.0 / Math.Max(1L, wc.Fee):0}% dari tarif acuan), reputasi {p.Reputation}, " +
                        $"muatan maks {maxPayload:N0} kg, kondisi armada {relScore:P0}";
        return new BidScore(total, priceScore, repScore, capScore, relScore, detail);
    }
}