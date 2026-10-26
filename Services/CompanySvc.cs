using Dapper;
using Nusantara.Database;

namespace Nusantara.Services;

// Perusahaan pemain dan AI sama-sama baris di tabel players (kolom kind membedakan).
// Karena itu armada, supir, uang, trip, dan tier memakai service dan aturan yang SAMA.
public static class CompanySvc
{
    public const string KindPlayer = "PLAYER";
    public const string KindAi = "AI";

    static readonly (string Name, string Personality)[] AiRoster =
    {
        ("PT Garuda Lintas Nusa", "AGRESIF"),
        ("CV Sumber Rezeki Jaya", "HEMAT"),
        ("PT Prima Cargo Utama", "PREMIUM"),
        ("PT Samudra Ekspansi Raya", "EKSPANSI"),
        ("CV Mitra Seimbang Logistik", "SEIMBANG")
    };

    public static string Kind(int id)
    {
        using var c = Db.Open();
        return c.ExecuteScalar<string>("SELECT kind FROM players WHERE id=@id", new { id }) ?? KindPlayer;
    }

    public static bool IsAi(int id) => Kind(id) == KindAi;

    public static int? HumanId()
    {
        using var c = Db.Open();
        return c.ExecuteScalar<int?>("SELECT id FROM players WHERE kind='PLAYER' ORDER BY id LIMIT 1");
    }

    // Hari dunia = hari perusahaan pemain. Perusahaan AI dibuat dengan tanggal pendirian yang sama,
    // jadi nomor hari semua perusahaan selalu sama.
    public static int WorldDay()
    {
        var h = HumanId();
        return h == null ? 0 : PlayerSvc.Day(PlayerSvc.Get(h.Value));
    }

    public static List<int> AiIds(bool activeOnly = true)
    {
        using var c = Db.Open();
        string sql = "SELECT id FROM players WHERE kind='AI'" + (activeOnly ? " AND bankrupt=0" : "") + " ORDER BY id";
        return c.Query<int>(sql).ToList();
    }

    // Idempotent: hanya menambah sampai jumlah AI mencapai count. Mengembalikan jumlah yang baru dibuat.
    public static int EnsureAiCompanies(int count)
    {
        var humanId = HumanId();
        if (humanId == null) return 0;
        var human = PlayerSvc.Get(humanId.Value);
        int day = PlayerSvc.Day(human);
        int nextMonth = (day / 30 + 1) * 30;

        using var c = Db.Open();
        int have = c.ExecuteScalar<int>("SELECT COUNT(*) FROM players WHERE kind='AI'");
        int created = 0;
        foreach (var r in AiRoster)
        {
            if (have + created >= count) break;
            if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM players WHERE name=@n", new { n = r.Name }) > 0) continue;

            // AI memulai dengan modal yang sama dengan pemain (tanpa keuntungan tersembunyi).
            c.Execute(@"INSERT INTO players(name,created_at,cash,kind,personality,last_day,next_month_day)
                VALUES(@n,@created,@cash,'AI',@pers,@day,@nm)",
                new { n = r.Name, created = human.CreatedAt, cash = EconomySvc.StartingCash, pers = r.Personality, day, nm = nextMonth });
            int id = c.ExecuteScalar<int>("SELECT id FROM players WHERE name=@n", new { n = r.Name });

            LedgerSvc.EnsureOpening(id);
            HrSvc.Refresh(id, false);
            Events.Append(c, null, EventTypes.CompanyFounded, new { company = r.Name, cash = EconomySvc.StartingCash }, id);
            created++;
        }
        return created;
    }
}