
using Dapper;
using Nusantara.Database;

namespace Nusantara.Services;

public sealed record CompetitorOverview(
    int Id,
    string Name,
    string Personality,
    int Tier,
    int Reputation,
    long Cash,
    bool Bankrupt,
    int Trucks,
    int ActiveTrips,
    int ActiveContracts,
    int Drivers,
    string RelationStatus,
    int Trust,
    int Respect,
    int Grievance,
    int LastInteractionDay
);

public sealed record CompetitorActivity(
    int Day,
    string Action,
    string Status,
    string Reason,
    string Detail
);

public static class CompetitorIntelSvc
{
    public static List<CompetitorOverview> List(int playerId)
    {
        using var c = Db.Open();

        var companies = c.Query<CompetitorOverview>("""
SELECT
    p.id AS Id,
    p.name AS Name,
    COALESCE(p.personality, 'SEIMBANG') AS Personality,
    p.tier AS Tier,
    COALESCE(p.rep_delivery, 0) + COALESCE(p.rep_brand, 0) AS Reputation,
    p.cash AS Cash,
    p.bankrupt AS Bankrupt,

    (
        SELECT COUNT(*)
        FROM trucks t
        WHERE t.player_id = p.id
        AND t.status <> 'TOTALLOSS'
    ) AS Trucks,

    (
        SELECT COUNT(*)
        FROM trips tr
        WHERE tr.player_id = p.id
        AND tr.status = 'RUNNING'
    ) AS ActiveTrips,

    (
        SELECT COUNT(*)
        FROM contracts ct
        WHERE ct.player_id = p.id
        AND ct.status IN ('OPEN', 'ACTIVE')
    ) AS ActiveContracts,

    (
        SELECT COUNT(*)
        FROM drivers d
        WHERE d.player_id = p.id
        AND d.status NOT IN ('CANDIDATE', 'QUIT')
    ) AS Drivers,

    'NEUTRAL' AS RelationStatus,
    0 AS Trust,
    0 AS Respect,
    0 AS Grievance,
    0 AS LastInteractionDay

FROM players p
WHERE p.kind = 'AI'
AND p.id <> @playerId
ORDER BY Reputation DESC, Trucks DESC, p.name
""", new { playerId }).ToList();

        return companies.Select(x =>
        {
            var memory = AiSocialSvc.Memory(playerId, x.Id);

            return x with
            {
                RelationStatus = AiSocialSvc.RelationStatus(playerId, x.Id),
                Trust = memory?.Trust ?? 0,
                Respect = memory?.Respect ?? 0,
                Grievance = memory?.Grievance ?? 0,
                LastInteractionDay = memory?.LastInteractionDay ?? 0
            };
        }).ToList();
    }

    public static List<CompetitorActivity> RecentActivity(
        int companyId,
        int limit = 10)
    {
        using var c = Db.Open();

        return c.Query<CompetitorActivity>("""
SELECT
    day AS Day,
    action AS Action,
    status AS Status,
    reason AS Reason,
    detail AS Detail
FROM ai_decision_log
WHERE company_id = @companyId
ORDER BY day DESC, id DESC
LIMIT @limit
""", new
        {
            companyId,
            limit = Math.Clamp(limit, 1, 50)
        }).ToList();
    }

    public static string CashBand(long cash)
    {
        if (cash < 0) return "Defisit";
        if (cash < 100_000_000) return "Sangat rendah";
        if (cash < 300_000_000) return "Rendah";
        if (cash < 750_000_000) return "Menengah";
        if (cash < 1_500_000_000) return "Kuat";
        return "Sangat kuat";
    }

    public static string PersonalityDescription(string personality)
    {
        return personality switch
        {
            "HEMAT" =>
                "Berhati-hati, mengutamakan efisiensi biaya dan cadangan kas.",

            "AGRESIF" =>
                "Kompetitif, berani mengambil risiko dan mengejar pangsa pasar.",

            "PREMIUM" =>
                "Mengutamakan reputasi, kualitas layanan, dan kontrak bernilai tinggi.",

            "EKSPANSI" =>
                "Berorientasi pada pertumbuhan armada, jaringan, dan skala perusahaan.",

            _ =>
                "Mengutamakan keseimbangan antara pertumbuhan, risiko, dan stabilitas."
        };
    }
}
