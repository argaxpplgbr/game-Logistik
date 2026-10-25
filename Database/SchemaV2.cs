namespace Nusantara.Database;

// Migrasi v2: buku kejadian (event ledger), berita, dan digest analisis.
public static class SchemaV2
{
    public const string Sql = """
CREATE TABLE event_ledger (
  event_id INTEGER PRIMARY KEY AUTOINCREMENT,
  sequence INTEGER NOT NULL UNIQUE,
  type TEXT NOT NULL,
  actor_id INTEGER NULL,
  target_id INTEGER NULL,
  payload_json TEXT NOT NULL,
  payload_version INTEGER NOT NULL DEFAULT 1,
  sim_day INTEGER NOT NULL,
  sim_minute INTEGER NOT NULL
);
CREATE INDEX idx_event_day ON event_ledger(sim_day, sequence);
CREATE INDEX idx_event_type ON event_ledger(type, sequence);

CREATE TRIGGER trg_event_no_update BEFORE UPDATE ON event_ledger
BEGIN
  SELECT RAISE(ABORT, 'event_ledger tidak boleh diubah');
END;

CREATE TRIGGER trg_event_no_delete BEFORE DELETE ON event_ledger
BEGIN
  SELECT RAISE(ABORT, 'event_ledger tidak boleh dihapus');
END;

CREATE TABLE digest (
  digest_id INTEGER PRIMARY KEY AUTOINCREMENT,
  period_start INTEGER NOT NULL,
  period_end INTEGER NOT NULL UNIQUE,
  created_day INTEGER NOT NULL,
  rule_version INTEGER NOT NULL,
  event_ids TEXT NOT NULL,
  metrics_json TEXT NOT NULL,
  output TEXT NOT NULL
);

CREATE TABLE news_item (
  news_id INTEGER PRIMARY KEY AUTOINCREMENT,
  event_id INTEGER NULL,
  category TEXT NOT NULL,
  headline TEXT NOT NULL,
  body TEXT NOT NULL,
  confidence TEXT NOT NULL DEFAULT 'FAKTA',
  created_day INTEGER NOT NULL,
  created_minute INTEGER NOT NULL DEFAULT 0,
  digest_id INTEGER NULL,
  template_version INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX idx_news_id ON news_item(news_id);
CREATE INDEX idx_news_cat ON news_item(category, news_id);
CREATE INDEX idx_news_day ON news_item(created_day);
""";
}