
namespace Nusantara.Database;

// Operasional v5: jejak status pengiriman dan riwayat perawatan/pelatihan.
// Semua tabel bersifat append-only secara aplikasi; migrasi hanya menambah struktur.
public static class SchemaV5
{
    public const string Sql = """
CREATE TABLE trip_status_history (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  trip_id INTEGER NOT NULL,
  player_id INTEGER NOT NULL,
  status TEXT NOT NULL,
  reason TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  game_day INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX idx_trip_status_history_trip ON trip_status_history(trip_id, id);
CREATE INDEX idx_trip_status_history_player ON trip_status_history(player_id, game_day DESC);

CREATE TABLE maintenance_history (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  player_id INTEGER NOT NULL,
  truck_id INTEGER NOT NULL,
  components TEXT NOT NULL,
  cost INTEGER NOT NULL,
  condition_before TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  game_day INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX idx_maintenance_history_player ON maintenance_history(player_id, game_day DESC);

CREATE TABLE driver_training_history (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  player_id INTEGER NOT NULL,
  driver_id INTEGER NOT NULL,
  skill TEXT NOT NULL,
  gain INTEGER NOT NULL,
  cost INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  game_day INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX idx_driver_training_player ON driver_training_history(player_id, game_day DESC);

CREATE TABLE contract_payment_history (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  player_id INTEGER NOT NULL,
  contract_id INTEGER NOT NULL,
  period_day INTEGER NOT NULL,
  amount INTEGER NOT NULL,
  status TEXT NOT NULL DEFAULT 'PAID',
  created_at TEXT NOT NULL,
  UNIQUE(contract_id, period_day)
);
""";
}
