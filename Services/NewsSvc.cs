using Dapper;
using Nusantara.Database;

namespace Nusantara.Services;

public class NewsRow
{
    public long NewsId { get; set; }
    public long? EventId { get; set; }
    public string Category { get; set; } = "";
    public string Headline { get; set; } = "";
    public string Body { get; set; } = "";
    public string Confidence { get; set; } = "FAKTA";
    public int CreatedDay { get; set; }
    public int CreatedMinute { get; set; }
    public long? DigestId { get; set; }

    public string When => $"Hari {CreatedDay} • {CreatedMinute / 60:00}:{CreatedMinute % 60:00}";
}

public class DigestRow
{
    public long DigestId { get; set; }
    public int PeriodStart { get; set; }
    public int PeriodEnd { get; set; }
    public int CreatedDay { get; set; }
    public string Output { get; set; } = "";
}

public static class NewsSvc
{
    // Berita terbaru dulu. beforeId dipakai untuk halaman berikutnya.
    public static List<NewsRow> Latest(int limit, string? category = null, long? beforeId = null)
    {
        using var c = Db.Open();
        return c.Query<NewsRow>(@"SELECT news_id, event_id, category, headline, body, confidence, created_day, created_minute, digest_id
            FROM news_item
            WHERE (@category IS NULL OR category=@category) AND (@beforeId IS NULL OR news_id<@beforeId)
            ORDER BY news_id DESC LIMIT @limit", new { category, beforeId, limit }).ToList();
    }

    public static List<string> Categories()
    {
        using var c = Db.Open();
        return c.Query<string>("SELECT DISTINCT category FROM news_item ORDER BY category").ToList();
    }

    public static List<DigestRow> Digests(int limit)
    {
        using var c = Db.Open();
        return c.Query<DigestRow>(@"SELECT digest_id, period_start, period_end, created_day, output
            FROM digest ORDER BY period_end DESC LIMIT @limit", new { limit }).ToList();
    }
}