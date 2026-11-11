
using Dapper;
using Nusantara.Database;

namespace Nusantara.Services;

public sealed record CompanyMessage(
    long Id,
    int SenderId,
    int RecipientId,
    string MessageType,
    string Body,
    int Day,
    string Status,
    string? Reply
);

public sealed record AiSocialMemory(
    int CompanyId,
    int OtherCompanyId,
    int Trust,
    int Respect,
    int Grievance,
    int LastInteractionDay,
    int LastContactDay,
    string Notes
);

public sealed record CompanyAgreement(
    long Id,
    int CompanyA,
    int CompanyB,
    string AgreementType,
    string Status,
    int CreatedDay,
    int ExpiresDay,
    string Terms
);

public static class AiSocialSvc
{
    private static readonly object SchemaLock = new();
    private static int _readyGeneration = -1;

    private const int ContactCooldownDays = 7;
    private const int AgreementDays = 30;

    private static readonly HashSet<string> AllowedMessages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CHAT",
            "COOPERATION",
            "ALLIANCE_OFFER",
            "COVERT_OFFER",
            "THREAT",
            "COMPETITIVE_PROPOSAL"
        };

    private const string SchemaSql = """
CREATE TABLE IF NOT EXISTS ai_social_schema (
    version INTEGER PRIMARY KEY,
    applied_day INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS ai_social_memory (
    company_id INTEGER NOT NULL,
    other_company_id INTEGER NOT NULL,
    trust INTEGER NOT NULL DEFAULT 0,
    respect INTEGER NOT NULL DEFAULT 0,
    grievance INTEGER NOT NULL DEFAULT 0,
    last_interaction_day INTEGER NOT NULL DEFAULT 0,
    last_contact_day INTEGER NOT NULL DEFAULT -1000,
    notes TEXT NOT NULL DEFAULT '',
    PRIMARY KEY(company_id, other_company_id),
    CHECK(company_id <> other_company_id)
);

CREATE TABLE IF NOT EXISTS company_messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sender_id INTEGER NOT NULL,
    recipient_id INTEGER NOT NULL,
    message_type TEXT NOT NULL,
    body TEXT NOT NULL,
    day INTEGER NOT NULL,
    status TEXT NOT NULL DEFAULT 'PENDING'
        CHECK(status IN ('PENDING','ACCEPTED','REJECTED')),
    reply TEXT NULL,
    resolved_day INTEGER NULL,
    CHECK(sender_id <> recipient_id)
);

CREATE INDEX IF NOT EXISTS idx_company_messages_inbox
ON company_messages(recipient_id, status, day);

CREATE INDEX IF NOT EXISTS idx_company_messages_sender
ON company_messages(sender_id, recipient_id, day);

CREATE TABLE IF NOT EXISTS ai_social_turns (
    company_id INTEGER NOT NULL,
    day INTEGER NOT NULL,
    PRIMARY KEY(company_id, day)
);

CREATE TABLE IF NOT EXISTS ai_social_agreements (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    company_a INTEGER NOT NULL,
    company_b INTEGER NOT NULL,
    agreement_type TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'ACTIVE'
        CHECK(status IN ('ACTIVE','EXPIRED','CANCELLED','BROKEN')),
    created_day INTEGER NOT NULL,
    expires_day INTEGER NOT NULL,
    terms TEXT NOT NULL DEFAULT '',
    CHECK(company_a < company_b)
);

CREATE INDEX IF NOT EXISTS idx_ai_agreements_companies
ON ai_social_agreements(company_a, company_b, status);

INSERT OR IGNORE INTO ai_social_schema(version, applied_day)
VALUES(1, 0);
""";

    public static void EnsureReady()
    {
        if (_readyGeneration == Db.Generation)
            return;

        lock (SchemaLock)
        {
            if (_readyGeneration == Db.Generation)
                return;

            using var c = Db.Open();
            using var tx = c.BeginTransaction();

            c.Execute(SchemaSql, transaction: tx);

            tx.Commit();
            _readyGeneration = Db.Generation;
        }
    }

    private static void EnsureRelations()
    {
        using var c = Db.Open();

        c.Execute("""
INSERT OR IGNORE INTO company_relations(company_a, company_b)
SELECT a.id, b.id
FROM players a
JOIN players b ON a.id < b.id;

INSERT OR IGNORE INTO ai_social_memory(company_id, other_company_id)
SELECT a.id, b.id
FROM players a
JOIN players b ON a.id <> b.id;
""");
    }

    public static void RunDaily(int companyId, int day)
    {
        EnsureReady();
        EnsureRelations();

        using (var c = Db.Open())
        {
            int claimed = c.Execute("""
INSERT OR IGNORE INTO ai_social_turns(company_id, day)
VALUES(@companyId, @day)
""", new { companyId, day });

            if (claimed == 0)
                return;
        }

        ProcessInbox(companyId, day);
        TryAutonomousContact(companyId, day);
        ExpireAgreements(day);
    }

    public static long SendMessage(
        int senderId,
        int recipientId,
        string messageType,
        string body,
        int day)
    {
        EnsureReady();

        if (senderId == recipientId)
            throw new ArgumentException("Perusahaan tidak bisa mengirim pesan kepada dirinya sendiri.");

        messageType = messageType.Trim().ToUpperInvariant();

        if (!AllowedMessages.Contains(messageType))
            throw new ArgumentException("Jenis pesan tidak dikenal.");

        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Pesan tidak boleh kosong.");

        body = body.Trim();

        if (body.Length > 1000)
            body = body[..1000];

        using var c = Db.Open();
        using var tx = c.BeginTransaction();

        int senderExists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM players WHERE id=@id",
            new { id = senderId }, tx);

        int recipientExists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM players WHERE id=@id",
            new { id = recipientId }, tx);

        if (senderExists == 0 || recipientExists == 0)
            throw new InvalidOperationException("Perusahaan tidak ditemukan.");

        c.Execute("""
INSERT OR IGNORE INTO ai_social_memory(company_id, other_company_id)
VALUES(@senderId, @recipientId)
""", new { senderId, recipientId }, tx);

        c.Execute("""
INSERT INTO company_messages(
    sender_id, recipient_id, message_type, body, day
)
VALUES(
    @senderId, @recipientId, @messageType, @body, @day
)
""", new { senderId, recipientId, messageType, body, day }, tx);

        long id = c.ExecuteScalar<long>(
            "SELECT last_insert_rowid()", transaction: tx);

        c.Execute("""
UPDATE ai_social_memory
SET last_contact_day=@day
WHERE company_id=@senderId
AND other_company_id=@recipientId
""", new { senderId, recipientId, day }, tx);

        tx.Commit();
        return id;
    }

    public static List<CompanyMessage> Inbox(int companyId, int limit = 50)
    {
        EnsureReady();

        using var c = Db.Open();

        return c.Query<CompanyMessage>("""
SELECT
    id AS Id,
    sender_id AS SenderId,
    recipient_id AS RecipientId,
    message_type AS MessageType,
    body AS Body,
    day AS Day,
    status AS Status,
    reply AS Reply
FROM company_messages
WHERE recipient_id=@companyId
ORDER BY day DESC, id DESC
LIMIT @limit
""", new
        {
            companyId,
            limit = Math.Clamp(limit, 1, 200)
        }).ToList();
    }

    public static bool Respond(
        int companyId,
        long messageId,
        bool accept,
        string reply,
        int day)
    {
        EnsureReady();

        using var c = Db.Open();
        using var tx = c.BeginTransaction();

        var message = c.QueryFirstOrDefault<CompanyMessage>("""
SELECT
    id AS Id,
    sender_id AS SenderId,
    recipient_id AS RecipientId,
    message_type AS MessageType,
    body AS Body,
    day AS Day,
    status AS Status,
    reply AS Reply
FROM company_messages
WHERE id=@messageId
AND recipient_id=@companyId
AND status='PENDING'
""", new { messageId, companyId }, tx);

        if (message == null)
            return false;

        ResolveMessage(c, tx, message, accept, reply, day);

        tx.Commit();
        return true;
    }

    private static void ProcessInbox(int companyId, int day)
    {
        using var c = Db.Open();

        var messages = c.Query<CompanyMessage>("""
SELECT
    id AS Id,
    sender_id AS SenderId,
    recipient_id AS RecipientId,
    message_type AS MessageType,
    body AS Body,
    day AS Day,
    status AS Status,
    reply AS Reply
FROM company_messages
WHERE recipient_id=@companyId
AND status='PENDING'
ORDER BY day, id
LIMIT 20
""", new { companyId }).ToList();

        foreach (var message in messages)
        {
            try
            {
                bool accept = EvaluateOffer(companyId, message);
                string reply = BuildReply(companyId, message, accept);

                using var tx = c.BeginTransaction();

                ResolveMessage(c, tx, message, accept, reply, day);

                tx.Commit();
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Peringatan: pesan AI gagal diproses: {ex.Message}");
            }
        }
    }

    private static bool EvaluateOffer(
        int companyId,
        CompanyMessage message)
    {
        string personality;

        using (var c = Db.Open())
        {
            personality = c.ExecuteScalar<string>(
                "SELECT personality FROM players WHERE id=@companyId",
                new { companyId }) ?? "SEIMBANG";
        }

        var profile = AiProfiles.Get(personality);
        var memory = GetMemory(companyId, message.SenderId);

        int trust = memory?.Trust ?? 0;
        int grievance = memory?.Grievance ?? 0;

        switch (message.MessageType)
        {
            case "CHAT":
                return true;

            case "COOPERATION":
                return profile.Code switch
                {
                    "HEMAT" => trust >= 0 && grievance < 50,
                    "PREMIUM" => trust >= 0 && grievance < 40,
                    "AGRESIF" => trust >= -20 && grievance < 70,
                    "EKSPANSI" => trust >= -10 && grievance < 60,
                    _ => trust >= -10 && grievance < 50
                };

            case "ALLIANCE_OFFER":
                return trust >=
                    (profile.Code == "HEMAT" ? 15 : 0)
                    && grievance < 40;

            case "COVERT_OFFER":
                // Persetujuan tindakan rahasia hanya dalam simulasi game.
                return profile.Code == "AGRESIF" ||
                       profile.Code == "EKSPANSI"
                    ? trust >= -5 && grievance < 80
                    : trust >= 35 && grievance < 30;

            case "COMPETITIVE_PROPOSAL":
                return profile.Code == "AGRESIF" ||
                       profile.Code == "EKSPANSI";

            case "THREAT":
                return false;

            default:
                return false;
        }
    }

    private static string BuildReply(
        int companyId,
        CompanyMessage message,
        bool accept)
    {
        if (!accept)
        {
            return message.MessageType == "THREAT"
                ? "Kami tidak menerima ancaman. Kami akan melindungi kepentingan perusahaan kami."
                : "Kami sudah mempertimbangkan tawaran Anda, tetapi belum dapat menyetujuinya.";
        }

        string personality;

        using (var c = Db.Open())
        {
            personality = c.ExecuteScalar<string>(
                "SELECT personality FROM players WHERE id=@companyId",
                new { companyId }) ?? "SEIMBANG";
        }

        if (message.MessageType == "CHAT")
        {
            return personality switch
            {
                "AGRESIF" => "Kami terbuka untuk berdiskusi. Namun, kepentingan bisnis kami tetap menjadi prioritas.",
                "HEMAT" => "Terima kasih sudah menghubungi kami. Kami siap membicarakan peluang yang efisien.",
                "PREMIUM" => "Senang berkomunikasi dengan Anda. Kami mengutamakan profesionalisme dalam setiap hubungan bisnis.",
                "EKSPANSI" => "Kami selalu terbuka terhadap peluang dan jaringan bisnis baru.",
                _ => "Terima kasih atas pesan Anda. Kami terbuka untuk komunikasi lebih lanjut."
            };
        }

        return message.MessageType switch
        {
            "ALLIANCE_OFFER" => "Kami menerima tawaran aliansi. Mari membangun hubungan bisnis yang saling menguntungkan.",
            "COVERT_OFFER" => "Kami menerima usulan kerja sama rahasia dalam simulasi ini. Kita perlu mempertimbangkan risikonya.",
            "COMPETITIVE_PROPOSAL" => "Kami menerima tantangan persaingan. Mari buktikan kemampuan perusahaan masing-masing.",
            _ => "Kami menerima tawaran kerja sama. Mari membahas langkah berikutnya."
        };
    }

    private static void ResolveMessage(
        System.Data.IDbConnection c,
        System.Data.IDbTransaction tx,
        CompanyMessage message,
        bool accept,
        string reply,
        int day)
    {
        string status = accept ? "ACCEPTED" : "REJECTED";

        c.Execute("""
UPDATE company_messages
SET status=@status,
    reply=@reply,
    resolved_day=@day
WHERE id=@id
AND status='PENDING'
""", new
        {
            status,
            reply = reply.Length > 500 ? reply[..500] : reply,
            day,
            id = message.Id
        }, tx);

        int receiver = message.RecipientId;
        int sender = message.SenderId;

        int trustChange = accept
            ? (message.MessageType == "CHAT" ? 2 : 8)
            : -2;

        int grievanceChange = accept ? -2 : 1;

        if (message.MessageType == "THREAT")
        {
            trustChange = -20;
            grievanceChange = 15;
        }

        c.Execute("""
INSERT INTO ai_social_memory(
    company_id, other_company_id, trust, respect,
    grievance, last_interaction_day, notes
)
VALUES(
    @receiver, @sender, @trust, 0,
    @grievance, @day, @notes
)
ON CONFLICT(company_id, other_company_id)
DO UPDATE SET
    trust=MAX(-100, MIN(100, ai_social_memory.trust + @trust)),
    grievance=MAX(0, MIN(100, ai_social_memory.grievance + @grievance)),
    last_interaction_day=@day,
    notes=@notes
""", new
        {
            receiver,
            sender,
            trust = trustChange,
            grievance = grievanceChange,
            day,
            notes = accept ? "Interaksi diterima." : "Interaksi ditolak."
        }, tx);

        string relationStatus =
            message.MessageType == "THREAT" ? "HOSTILE" :
            accept && message.MessageType == "ALLIANCE_OFFER" ? "ALLY" :
            "NEUTRAL";

        int a = Math.Min(receiver, sender);
        int b = Math.Max(receiver, sender);

        c.Execute("""
INSERT INTO company_relations(
    company_a, company_b, status, since_day
)
VALUES(@a, @b, @relationStatus, @day)
ON CONFLICT(company_a, company_b)
DO UPDATE SET
    status=@relationStatus,
    since_day=@day
""", new { a, b, relationStatus, day }, tx);

        if (accept && message.MessageType != "CHAT")
        {
            string agreementType = message.MessageType switch
            {
                "ALLIANCE_OFFER" => "ALLIANCE",
                "COOPERATION" => "COOPERATION",
                "COVERT_OFFER" => "COVERT_COOPERATION",
                "COMPETITIVE_PROPOSAL" => "COMPETITIVE_CHALLENGE",
                _ => "COOPERATION"
            };

            c.Execute("""
INSERT INTO ai_social_agreements(
    company_a, company_b, agreement_type,
    status, created_day, expires_day, terms
)
VALUES(
    @a, @b, @agreementType,
    'ACTIVE', @day, @expiresDay, @terms
)
""", new
            {
                a,
                b,
                agreementType,
                day,
                expiresDay = day + AgreementDays,
                terms = message.Body
            }, tx);
        }
    }

    private static void TryAutonomousContact(int companyId, int day)
    {
        using var c = Db.Open();

        var sender = c.QueryFirstOrDefault<CompanyInfo>("""
SELECT
    id AS Id,
    name AS Name,
    kind AS Kind,
    personality AS Personality,
    tier AS Tier,
    reputation AS Reputation
FROM players
WHERE id=@companyId
AND kind='AI'
AND bankrupt=0
""", new { companyId });

        if (sender == null)
            return;

        var candidates = c.Query<CompanyInfo>("""
SELECT
    id AS Id,
    name AS Name,
    kind AS Kind,
    personality AS Personality,
    tier AS Tier,
    reputation AS Reputation
FROM players
WHERE id<>@companyId
AND bankrupt=0
ORDER BY id
""", new { companyId }).ToList();

        var eligible = candidates
            .Where(x =>
            {
                var memory = GetMemory(companyId, x.Id);

                return memory == null ||
                       day - memory.LastContactDay >= ContactCooldownDays;
            })
            .ToList();

        if (eligible.Count == 0)
            return;

        var target = SelectTarget(sender, eligible);

        if (target == null)
            return;

        string type;
        string body;

        switch (sender.Personality)
        {
            case "AGRESIF":
                type = "COMPETITIVE_PROPOSAL";
                body = $"Kami melihat perkembangan {target.Name}. " +
                       "Kami mengusulkan persaingan bisnis yang terbuka dan profesional.";
                break;

            case "HEMAT":
                type = "COOPERATION";
                body = $"Kami ingin menjajaki kerja sama operasional dengan {target.Name} " +
                       "untuk meningkatkan efisiensi biaya.";
                break;

            case "PREMIUM":
                type = "COOPERATION";
                body = $"Kami ingin menawarkan kemitraan layanan logistik berkualitas " +
                       $"kepada {target.Name}.";
                break;

            case "EKSPANSI":
                type = "ALLIANCE_OFFER";
                body = $"Kami sedang memperluas jaringan bisnis dan ingin menawarkan " +
                       $"aliansi strategis kepada {target.Name}.";
                break;

            default:
                type = "COOPERATION";
                body = $"Kami ingin menjajaki kemungkinan kerja sama bisnis " +
                       $"dengan {target.Name}. Apakah Anda tertarik?";
                break;
        }

        SendMessage(companyId, target.Id, type, body, day);
    }

    private static CompanyInfo? SelectTarget(
        CompanyInfo sender,
        List<CompanyInfo> candidates)
    {
        return sender.Personality switch
        {
            "AGRESIF" => candidates
                .OrderByDescending(x => x.Tier)
                .ThenByDescending(x => x.Reputation)
                .FirstOrDefault(),

            "PREMIUM" => candidates
                .OrderByDescending(x => x.Reputation)
                .ThenByDescending(x => x.Tier)
                .FirstOrDefault(),

            "EKSPANSI" => candidates
                .OrderByDescending(x => x.Tier)
                .ThenByDescending(x => x.Reputation)
                .FirstOrDefault(),

            "HEMAT" => candidates
                .OrderBy(x => x.Tier)
                .ThenByDescending(x => x.Reputation)
                .FirstOrDefault(),

            _ => candidates
                .OrderByDescending(x =>
                    GetMemory(sender.Id, x.Id)?.Trust ?? 0)
                .ThenByDescending(x => x.Reputation)
                .FirstOrDefault()
        };
    }

    private static AiSocialMemory? GetMemory(
        int companyId,
        int otherId)
    {
        using var c = Db.Open();

        return c.QueryFirstOrDefault<AiSocialMemory>("""
SELECT
    company_id AS CompanyId,
    other_company_id AS OtherCompanyId,
    trust AS Trust,
    respect AS Respect,
    grievance AS Grievance,
    last_interaction_day AS LastInteractionDay,
    last_contact_day AS LastContactDay,
    notes AS Notes
FROM ai_social_memory
WHERE company_id=@companyId
AND other_company_id=@otherId
""", new { companyId, otherId });
    }

    public static List<CompanyAgreement> Agreements(int companyId)
    {
        EnsureReady();

        using var c = Db.Open();

        return c.Query<CompanyAgreement>("""
SELECT
    id AS Id,
    company_a AS CompanyA,
    company_b AS CompanyB,
    agreement_type AS AgreementType,
    status AS Status,
    created_day AS CreatedDay,
    expires_day AS ExpiresDay,
    terms AS Terms
FROM ai_social_agreements
WHERE company_a=@companyId
OR company_b=@companyId
ORDER BY created_day DESC
""", new { companyId }).ToList();
    }

    private static void ExpireAgreements(int day)
    {
        using var c = Db.Open();

        c.Execute("""
UPDATE ai_social_agreements
SET status='EXPIRED'
WHERE status='ACTIVE'
AND expires_day < @day
""", new { day });
    }

    private sealed record CompanyInfo(
        int Id,
        string Name,
        string Kind,
        string Personality,
        int Tier,
        int Reputation
    );
}
