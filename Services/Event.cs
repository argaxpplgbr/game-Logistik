using System.Data;
using System.Text.Json;
using Dapper;

namespace Nusantara.Services;

// Buku kejadian: fakta yang tidak bisa diubah (dijaga trigger di database).
// Berita dibuat dari baris-baris ini, bukan dari angka karangan.
public static class Events
{
    public const int PayloadVersion = 1;

    // tx != null  : ikut transaksi pemanggil (kalau aksinya batal, kejadian ikut batal).
    // tx == null  : transaksi sendiri; kegagalan hanya memunculkan peringatan, aksi game tidak dibatalkan.
    // Mengembalikan event_id (atau -1 kalau gagal pada mode transaksi sendiri).
    public static long Append(IDbConnection c, IDbTransaction? tx, string type, object payload,
        int? actor = null, int? target = null, bool news = true)
    {
        bool own = tx == null;
        var t = tx ?? c.BeginTransaction();
        try
        {
            c.Execute("UPDATE game_state SET last_event_sequence=last_event_sequence+1 WHERE id=1", transaction: t);
            long seq = c.ExecuteScalar<long>("SELECT last_event_sequence FROM game_state WHERE id=1", transaction: t);

            var createdText = c.ExecuteScalar<string>("SELECT created_at FROM players ORDER BY id LIMIT 1", transaction: t);
            double days = createdText == null ? 0 : SimClock.DaysSince(SimClock.FromDb(createdText), SimClock.Minutes);
            int day = (int)days;
            int minute = (int)((days - day) * 1440);

            string json = JsonSerializer.Serialize(payload);
            c.Execute(@"INSERT INTO event_ledger(sequence,type,actor_id,target_id,payload_json,payload_version,sim_day,sim_minute)
                VALUES(@seq,@type,@actor,@target,@json,@pv,@day,@minute)",
                new { seq, type, actor, target, json, pv = PayloadVersion, day, minute }, t);
            long id = c.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: t);

            if (news)
            {
                NewsDraft? d = null;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    d = NewsComposer.Compose(type, doc.RootElement, id);
                }
                catch { d = null; }   // template gagal: kejadian tetap tercatat, hanya tanpa berita

                if (d != null)
                    c.Execute(@"INSERT INTO news_item(event_id,category,headline,body,confidence,created_day,created_minute,template_version)
                        VALUES(@id,@cat,@h,@b,'FAKTA',@day,@minute,@tv)",
                        new { id, cat = d.Category, h = d.Headline, b = d.Body, day, minute, tv = NewsComposer.TemplateVersion }, t);
            }

            if (own) t.Commit();
            return id;
        }
        catch (Exception ex)
        {
            if (own)
            {
                try { t.Rollback(); } catch { /* sudah batal */ }
                Console.WriteLine("Peringatan: gagal mencatat kejadian " + type + ": " + ex.Message);
                return -1;
            }
            throw;
        }
        finally
        {
            if (own) t.Dispose();
        }
    }
}