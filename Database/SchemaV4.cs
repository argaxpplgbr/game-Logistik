namespace Nusantara.Database;

// Migrasi v4 (Dokumen 02): perusahaan AI, pasar kontrak bersama, penawaran, pemenang, rencana AI.
// Hanya MENAMBAH kolom/tabel. Perusahaan AI memakai tabel players yang sama dengan pemain
// (kolom kind membedakan), jadi semua aturan armada, supir, uang, dan trip otomatis sama.
public static class SchemaV4
{
    public const string Sql = """
ALTER TABLE players ADD COLUMN kind TEXT NOT NULL DEFAULT 'PLAYER' CHECK (kind IN ('PLAYER','AI'));
ALTER TABLE players ADD COLUMN personality TEXT NOT NULL DEFAULT 'SEIMBANG';
ALTER TABLE contracts ADD COLUMN world_contract_id INTEGER NULL;

-- Satu kontrak dunia hanya boleh punya satu kontrak pelaksanaan.
CREATE UNIQUE INDEX ux_contracts_world ON contracts(world_contract_id) WHERE world_contract_id IS NOT NULL;

-- Peluang kontrak yang terlihat oleh semua perusahaan dalam satu sesi.
CREATE TABLE world_contracts (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  kind TEXT NOT NULL CHECK (kind IN ('SPOT','DEDICATED')),
  scope TEXT NOT NULL DEFAULT 'DOMESTIC' CHECK (scope IN ('DOMESTIC','INTERNATIONAL')),
  min_tier INTEGER NOT NULL,
  client TEXT NOT NULL, origin TEXT NOT NULL, dest TEXT NOT NULL, cargo_code TEXT NOT NULL,
  weight_kg INTEGER NOT NULL, fee INTEGER NOT NULL, rep_required INTEGER NOT NULL DEFAULT 0,
  vehicle_class TEXT NOT NULL, quote_km REAL NOT NULL, quote_rate INTEGER NOT NULL, quote_mult REAL NOT NULL,
  months_total INTEGER NOT NULL DEFAULT 0, trips_per_month INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','AWARDED','EXPIRED','CANCELLED')),
  created_day INTEGER NOT NULL, bid_close_day INTEGER NOT NULL, expire_day INTEGER NOT NULL
);
CREATE INDEX idx_world_contracts_status ON world_contracts(status, bid_close_day);

-- Penawaran perusahaan. Satu perusahaan hanya boleh menawar satu kali per kontrak.
CREATE TABLE contract_bids (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  world_contract_id INTEGER NOT NULL,
  company_id INTEGER NOT NULL,
  price INTEGER NOT NULL CHECK (price > 0),
  placed_day INTEGER NOT NULL,
  status TEXT NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','WON','LOST','REJECTED')),
  score REAL NULL,
  note TEXT NOT NULL DEFAULT '',
  UNIQUE (world_contract_id, company_id)
);
CREATE INDEX idx_bids_company ON contract_bids(company_id, status);

-- Pemenang. PRIMARY KEY pada world_contract_id menjamin satu kontrak hanya punya satu pemenang.
CREATE TABLE contract_awards (
  world_contract_id INTEGER PRIMARY KEY,
  company_id INTEGER NOT NULL,
  bid_id INTEGER NOT NULL,
  score REAL NOT NULL,
  reason TEXT NOT NULL,
  awarded_day INTEGER NOT NULL,
  execution_contract_id INTEGER NULL
);

-- Rencana jangka pendek/panjang perusahaan AI. pending_* dipakai agar tujuan tidak berganti acak.
CREATE TABLE ai_plan (
  company_id INTEGER PRIMARY KEY,
  short_goal TEXT NOT NULL, long_goal TEXT NOT NULL,
  short_set_day INTEGER NOT NULL, long_set_day INTEGER NOT NULL,
  pending_short TEXT NULL, pending_short_count INTEGER NOT NULL DEFAULT 0,
  pending_long TEXT NULL, pending_long_count INTEGER NOT NULL DEFAULT 0,
  last_review_day INTEGER NOT NULL, reason TEXT NOT NULL DEFAULT ''
);

-- Jejak keputusan AI: apa yang diusulkan, apakah lolos validasi, dan alasannya.
CREATE TABLE ai_decision_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  company_id INTEGER NOT NULL, day INTEGER NOT NULL,
  action TEXT NOT NULL, status TEXT NOT NULL CHECK (status IN ('DONE','REJECTED','FAILED')),
  reason TEXT NOT NULL, detail TEXT NOT NULL DEFAULT '', provider TEXT NOT NULL DEFAULT ''
);
CREATE INDEX idx_ai_log_company ON ai_decision_log(company_id, day);

-- Hubungan bisnis antarperusahaan (disiapkan untuk Dokumen 03; default NEUTRAL bila tidak ada baris).
CREATE TABLE company_relations (
  company_a INTEGER NOT NULL, company_b INTEGER NOT NULL,
  status TEXT NOT NULL DEFAULT 'NEUTRAL' CHECK (status IN ('NEUTRAL','ALLY','HOSTILE','NEGOTIATING','TRUCE')),
  since_day INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (company_a, company_b),
  CHECK (company_a < company_b)
);
""";
}