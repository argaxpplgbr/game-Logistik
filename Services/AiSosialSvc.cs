
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

public sealed record AiRelationship(
    int OtherCompanyId,
    string OtherCompanyName,
    string RelationStatus,
    int Trust,
    int Respect,
    int Grievance,
    int LastInteractionDay,
    int LastContactDay
);

public static class AiSocialSvc
{
    private static readonly object SchemaLock = new();
    private static int _readyGeneration = -1;

    private const int ContactCooldownDays = 7;
    private const int DefaultAgreementDays = 30;

    private static readonly HashSet<string> AllowedMessages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CHAT",
            "COOPERATION",
            "ALLIANCE_OFFER",
            "COVERT_OFFER",
            "TRUCE_OFFER",
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

        using (var c = Db.Open())
        {
            string kind = c.ExecuteScalar<string>(
                "SELECT kind FROM players WHERE id=@companyId",
                new { companyId }) ?? "";

            if (kind != "AI")
                return;
        }

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
VALUES(@senderId, @recipientId);
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

        if (messageType is "COOPERATION" or "ALLIANCE_OFFER" or "TRUCE_OFFER")
        {
            SetRelation(c, tx, senderId, recipientId, "NEGOTIATING", day);
        }

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

    public static List<CompanyMessage> RecentMessages(
        int companyId,
        int otherCompanyId,
        int limit = 20)
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
WHERE
    (sender_id=@companyId AND recipient_id=@otherCompanyId)
    OR
    (sender_id=@otherCompanyId AND recipient_id=@companyId)
ORDER BY day DESC, id DESC
LIMIT @limit
""", new
        {
            companyId,
            otherCompanyId,
            limit = Math.Clamp(limit, 1, 100)
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

        reply = string.IsNullOrWhiteSpace(reply)
            ? (accept ? "Kami menerima tawaran Anda." : "Kami belum dapat menerima tawaran tersebut.")
            : reply.Trim();

        if (reply.Length > 500)
            reply = reply[..500];

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

        bool result = ResolveMessage(c, tx, message, accept, reply, day);

        if (!result)
            return false;

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

                if (ResolveMessage(c, tx, message, accept, reply, day))
                    tx.Commit();
                else
                    tx.Rollback();
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
        string personality = Personality(companyId);
        var profile = AiProfiles.Get(personality);
        var memory = Memory(companyId, message.SenderId);

        int trust = memory?.Trust ?? 0;
        int respect = memory?.Respect ?? 0;
        int grievance = memory?.Grievance ?? 0;

        return message.MessageType switch
        {
            "CHAT" => true,

            "COOPERATION" => profile.Code switch
            {
                "HEMAT" => trust >= 0 && grievance < 45,
                "PREMIUM" => respect >= -10 && grievance < 40,
                "AGRESIF" => trust >= -20 && grievance < 65,
                "EKSPANSI" => trust >= -10 && grievance < 60,
                _ => trust >= -10 && grievance < 50
            },

            "ALLIANCE_OFFER" =>
                trust >= (profile.Code == "HEMAT" ? 15 : 0)
                && respect >= -10
                && grievance < 40,

            "COVERT_OFFER" =>
                (profile.Code is "AGRESIF" or "EKSPANSI")
                    ? trust >= -5 && grievance < 75
                    : trust >= 35 && grievance < 30,

            "TRUCE_OFFER" =>
                grievance >= 10 || RelationStatus(companyId, message.SenderId) == "HOSTILE",

            "COMPETITIVE_PROPOSAL" =>
                profile.Code is "AGRESIF" or "EKSPANSI",

            "THREAT" => false,

            _ => false
        };
    }

    private static string BuildReply(
        int companyId,
        CompanyMessage message,
        bool accept)
    {
        string personality = Personality(companyId);

        if (!accept)
        {
            if (message.MessageType == "THREAT")
            {
                return "Kami tidak menerima ancaman. Kami akan melindungi kepentingan perusahaan kami.";
            }

            return personality switch
            {
                "AGRESIF" => "Kami tidak menerima tawaran tersebut. Kami akan tetap bersaing secara profesional.",
                "HEMAT" => "Tawaran belum sesuai dengan perhitungan risiko dan efisiensi kami.",
                "PREMIUM" => "Kami menghargai tawaran Anda, tetapi belum sesuai standar kerja sama kami.",
                "EKSPANSI" => "Kami belum dapat menyetujui tawaran tersebut saat ini.",
                _ => "Kami sudah mempertimbangkan tawaran Anda, tetapi belum dapat menyetujuinya."
            };
        }

        if (message.MessageType == "CHAT")
        {
            return personality switch
            {
                "AGRESIF" => "Kami terbuka untuk berdiskusi. Namun, kepentingan bisnis kami tetap menjadi prioritas.",
                "HEMAT" => "Terima kasih sudah menghubungi kami. Kami siap membicarakan peluang yang efisien.",
                "PREMIUM" => "Senang berkomunikasi dengan Anda. Kami mengutamakan profesionalisme.",
                "EKSPANSI" => "Kami selalu terbuka terhadap peluang dan jaringan bisnis baru.",
                _ => "Terima kasih atas pesan Anda. Kami terbuka untuk komunikasi lebih lanjut."
            };
        }

        return message.MessageType switch
        {
            "ALLIANCE_OFFER" =>
                "Kami menerima tawaran aliansi. Mari membangun hubungan bisnis yang saling menguntungkan.",

            "COVERT_OFFER" =>
                "Kami menerima usulan kerja sama rahasia dalam simulasi ini. Kita perlu mempertimbangkan risikonya.",

            "TRUCE_OFFER" =>
                "Kami menerima tawaran gencatan. Mari menghentikan perselisihan dan menjaga kepentingan masing-masing.",

            "COMPETITIVE_PROPOSAL" =>
                "Kami menerima tantangan persaingan. Mari buktikan kemampuan perusahaan masing-masing.",

            _ =>
                "Kami menerima tawaran kerja sama. Mari membahas langkah berikutnya."
        };
    }

    private static bool ResolveMessage(
        System.Data.IDbConnection c,
        System.Data.IDbTransaction tx,
        CompanyMessage message,
        bool accept,
        string reply,
        int day)
    {
        string status = accept ? "ACCEPTED" : "REJECTED";

        int updated = c.Execute("""
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

        if (updated == 0)
            return false;

        int receiver = message.RecipientId;
        int sender = message.SenderId;

        int trustChange = accept
            ? (message.MessageType == "CHAT" ? 3 : 8)
            : -4;

        int respectChange = accept ? 3 : -1;
        int grievanceChange = accept ? -3 : 2;

        if (message.MessageType == "THREAT")
        {
            trustChange = -25;
            respectChange = -10;
            grievanceChange = 25;
        }

        UpdateMemory(
            c, tx, receiver, sender,
            trustChange, respectChange, grievanceChange,
            day, accept ? "Tawaran diterima." : "Tawaran ditolak.");

        UpdateMemory(
            c, tx, sender, receiver,
            accept ? 2 : -3,
            accept ? 2 : -2,
            accept ? -1 : 3,
            day,
            accept ? "Interaksi berhasil." : "Interaksi ditolak.");

        string oldStatus = GetRelationStatus(c, tx, sender, receiver);
        string newStatus = oldStatus;

        if (message.MessageType == "THREAT")
        {
            newStatus = "HOSTILE";

            c.Execute("""
UPDATE ai_social_agreements
SET status='BROKEN'
WHERE company_a=@a
AND company_b=@b
AND status='ACTIVE'
""", Pair(sender, receiver), tx);
        }
        else if (accept)
        {
            newStatus = message.MessageType switch
            {
                "ALLIANCE_OFFER" => "ALLY",
                "TRUCE_OFFER" => "TRUCE",
                "COOPERATION" => "NEGOTIATING",
                _ => oldStatus
            };
        }
        else if (oldStatus == "NEGOTIATING")
        {
            newStatus = "NEUTRAL";
        }

        if (newStatus != oldStatus)
        {
            SetRelation(c, tx, sender, receiver, newStatus, day);

            string senderName = CompanyName(c, tx, sender);
            string receiverName = CompanyName(c, tx, receiver);

            Events.Append(
                c,
                tx,
                EventTypes.CompanyRelationChanged,
                new
                {
                    companyA = senderName,
                    companyB = receiverName,
                    status = newStatus,
                    messageType = message.MessageType
                },
                sender,
                receiver,
                news: message.MessageType != "COVERT_OFFER");
        }

        if (accept && message.MessageType != "CHAT")
        {
            string agreementType = message.MessageType switch
            {
                "ALLIANCE_OFFER" => "ALLIANCE",
                "COOPERATION" => "COOPERATION",
                "COVERT_OFFER" => "COVERT_COOPERATION",
                "TRUCE_OFFER" => "TRUCE",
                "COMPETITIVE_PROPOSAL" => "COMPETITIVE_CHALLENGE",
                _ => "COOPERATION"
            };

            int duration = agreementType switch
            {
                "ALLIANCE" => 60,
                "TRUCE" => 15,
                "COVERT_COOPERATION" => 20,
                "COMPETITIVE_CHALLENGE" => 7,
                _ => DefaultAgreementDays
            };

            var pair = Pair(sender, receiver);

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
                pair.a,
                pair.b,
                agreementType,
                day,
                expiresDay = day + duration,
                terms = message.Body
            }, tx);

            if (agreementType != "COVERT_COOPERATION")
            {
                Events.Append(
                    c,
                    tx,
                    EventTypes.CompanyAgreementSigned,
                    new
                    {
                        companyA = CompanyName(c, tx, sender),
                        companyB = CompanyName(c, tx, receiver),
                        agreementType,
                        expiresDay = day + duration
                    },
                    sender,
                    receiver);
            }
        }

        return true;
    }

    private static void UpdateMemory(
        System.Data.IDbConnection c,
        System.Data.IDbTransaction tx,
        int companyId,
        int otherId,
        int trust,
        int respect,
        int grievance,
        int day,
        string note)
    {
        c.Execute("""
INSERT INTO ai_social_memory(
    company_id, other_company_id,
    trust, respect, grievance,
    last_interaction_day, notes
)
VALUES(
    @companyId, @otherId,
    @trust, @respect, @grievance,
    @day, @note
)
ON CONFLICT(company_id, other_company_id)
DO UPDATE SET
    trust=MAX(-100, MIN(100, ai_social_memory.trust + @trust)),
    respect=MAX(-100, MIN(100, ai_social_memory.respect + @respect)),
    grievance=MAX(0, MIN(100, ai_social_memory.grievance + @grievance)),
    last_interaction_day=@day,
    notes=@note
""", new
        {
            companyId,
            otherId,
            trust,
            respect,
            grievance,
            day,
            note
        }, tx);
    }

    private static void SetRelation(
        System.Data.IDbConnection c,
        System.Data.IDbTransaction tx,
        int companyA,
        int companyB,
        string status,
        int day)
    {
        var pair = Pair(companyA, companyB);

        c.Execute("""
INSERT INTO company_relations(
    company_a, company_b, status, since_day
)
VALUES(@a, @b, @status, @day)
ON CONFLICT(company_a, company_b)
DO UPDATE SET
    status=@status,
    since_day=@day
""", new
        {
            pair.a,
            pair.b,
            status,
            day
        }, tx);
    }

    private static string GetRelationStatus(
        System.Data.IDbConnection c,
        System.Data.IDbTransaction tx,
        int companyA,
        int companyB)
    {
        var pair = Pair(companyA, companyB);

        return c.ExecuteScalar<string>("""
SELECT status
FROM company_relations
WHERE company_a=@a AND company_b=@b
""", pair, tx) ?? "NEUTRAL";
    }

    private static (int a, int b) Pair(int x, int y) =>
        (Math.Min(x, y), Math.Max(x, y));

    private static string CompanyName(
        System.Data.IDbConnection c,
        System.Data.IDbTransaction tx,
        int id)
    {
        return c.ExecuteScalar<string>(
            "SELECT name FROM players WHERE id=@id",
            new { id }, tx) ?? "Perusahaan tidak dikenal";
    }

    private static string Personality(int companyId)
    {
        using var c = Db.Open();

        return c.ExecuteScalar<string>(
            "SELECT personality FROM players WHERE id=@companyId",
            new { companyId }) ?? "SEIMBANG";
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
    COALESCE(rep_delivery, 0) + COALESCE(rep_brand, 0) AS Reputation,
    cash AS Cash
FROM players
WHERE id=@companyId
AND kind='AI'
AND bankrupt=0
""", new { companyId });

        if (sender == null)
            return;

        var candidates = c.Query<CompanyInfo>("""
SELECT
    p.id AS Id,
    p.name AS Name,
    p.kind AS Kind,
    p.personality AS Personality,
    p.tier AS Tier,
    COALESCE(p.rep_delivery, 0) + COALESCE(p.rep_brand, 0) AS Reputation,
    p.cash AS Cash
FROM players p
WHERE p.id<>@companyId
AND p.bankrupt=0
AND NOT EXISTS (
    SELECT 1
    FROM company_messages m
    WHERE m.status='PENDING'
    AND (
        (m.sender_id=@companyId AND m.recipient_id=p.id)
        OR
        (m.sender_id=p.id AND m.recipient_id=@companyId)
    )
)
ORDER BY p.id
""", new { companyId }).ToList();

        var eligible = candidates
            .Where(x =>
            {
                var memory = Memory(companyId, x.Id);

                return memory == null ||
                       day - memory.LastContactDay >= ContactCooldownDays;
            })
            .ToList();

        if (eligible.Count == 0)
            return;

        var target = SelectTarget(sender, eligible);

        if (target == null)
            return;

        string relation = RelationStatus(companyId, target.Id);
        var targetMemory = Memory(companyId, target.Id);

        string type;
        string body;

        if (relation == "HOSTILE" &&
            (targetMemory?.Grievance ?? 0) >= 10)
        {
            type = "TRUCE_OFFER";
            body = $"Kami ingin menghentikan perselisihan dengan {target.Name}. " +
                   "Kami mengusulkan gencatan agar kedua perusahaan dapat fokus pada operasional.";
        }
        else
        {
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
                    type = relation == "ALLY" ? "COOPERATION" : "ALLIANCE_OFFER";
                    body = $"Kami sedang memperluas jaringan bisnis dan ingin membahas " +
                           $"peluang strategis dengan {target.Name}.";
                    break;

                default:
                    type = "COOPERATION";
                    body = $"Kami ingin menjajaki kemungkinan kerja sama bisnis " +
                           $"dengan {target.Name}. Apakah Anda tertarik?";
                    break;
            }
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
                .OrderByDescending(x => x.Reputation)
                .ThenByDescending(x => x.Tier)
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
                .OrderByDescending(x => Memory(sender.Id, x.Id)?.Trust ?? 0)
                .ThenByDescending(x => x.Reputation)
                .FirstOrDefault()
        };
    }

    public static AiSocialMemory? Memory(int companyId, int otherId)
    {
        EnsureReady();

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

    public static string RelationStatus(int companyA, int companyB)
    {
        EnsureReady();

        using var c = Db.Open();

        var pair = Pair(companyA, companyB);

        return c.ExecuteScalar<string>("""
SELECT status
FROM company_relations
WHERE company_a=@a
AND company_b=@b
""", pair) ?? "NEUTRAL";
    }

    public static List<AiRelationship> Relationships(int companyId)
    {
        EnsureReady();

        using var c = Db.Open();

        return c.Query<AiRelationship>("""
SELECT
    p.id AS OtherCompanyId,
    p.name AS OtherCompanyName,
    COALESCE(r.status, 'NEUTRAL') AS RelationStatus,
    COALESCE(m.trust, 0) AS Trust,
    COALESCE(m.respect, 0) AS Respect,
    COALESCE(m.grievance, 0) AS Grievance,
    COALESCE(m.last_interaction_day, 0) AS LastInteractionDay,
    COALESCE(m.last_contact_day, -1000) AS LastContactDay
FROM players p
LEFT JOIN company_relations r
    ON r.company_a = CASE WHEN @companyId < p.id THEN @companyId ELSE p.id END
    AND r.company_b = CASE WHEN @companyId < p.id THEN p.id ELSE @companyId END
LEFT JOIN ai_social_memory m
    ON m.company_id=@companyId
    AND m.other_company_id=p.id
WHERE p.id<>@companyId
ORDER BY p.kind, p.name
""", new { companyId }).ToList();
    }

    public static string CompanyName(int companyId)
    {
        using var c = Db.Open();

        return c.ExecuteScalar<string>(
            "SELECT name FROM players WHERE id=@companyId",
            new { companyId }) ?? "Perusahaan tidak dikenal";
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
ORDER BY created_day DESC, id DESC
""", new { companyId }).ToList();
    }

    private static void ExpireAgreements(int day)
    {
        using var c = Db.Open();

        var expired = c.Query<CompanyAgreement>("""
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
WHERE status='ACTIVE'
AND expires_day < @day
""", new { day }).ToList();

        foreach (var agreement in expired)
        {
            using var tx = c.BeginTransaction();

            c.Execute("""
UPDATE ai_social_agreements
SET status='EXPIRED'
WHERE id=@id AND status='ACTIVE'
""", new { agreement.Id }, tx);

            int stillActive = c.ExecuteScalar<int>("""
SELECT COUNT(*)
FROM ai_social_agreements
WHERE company_a=@a
AND company_b=@b
AND status='ACTIVE'
""", new { a = agreement.CompanyA, b = agreement.CompanyB }, tx);

            if (stillActive == 0)
            {
                c.Execute("""
UPDATE company_relations
SET status='NEUTRAL'
WHERE company_a=@a
AND company_b=@b
AND status IN ('ALLY','TRUCE','NEGOTIATING')
""", new { a = agreement.CompanyA, b = agreement.CompanyB }, tx);
            }

            if (agreement.AgreementType != "COVERT_COOPERATION")
            {
                Events.Append(
                    c,
                    tx,
                    EventTypes.CompanyAgreementExpired,
                    new
                    {
                        companyA = CompanyName(c, tx, agreement.CompanyA),
                        companyB = CompanyName(c, tx, agreement.CompanyB),
                        agreementType = agreement.AgreementType
                    },
                    agreement.CompanyA,
                    agreement.CompanyB);
            }

            tx.Commit();
        }
    }

    private sealed class CompanyInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Personality { get; set; } = "SEIMBANG";
        public int Tier { get; set; }
        public int Reputation { get; set; }
        public long Cash { get; set; }
    }
}
