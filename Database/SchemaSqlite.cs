namespace Nusantara.Database;

// Skema SQLite v1 (hasil migrasi penuh dari MySQL + kolom v3/v4 + tabel save_meta & game_state).
// Seed katalog (truk, bak, kargo, kota, jalan, kosmetik) ikut dibuat di sini.
public static class SchemaSqlite
{
    public const string V1 = """
CREATE TABLE players (
  id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE NOT NULL, created_at TEXT NOT NULL,
  cash INTEGER NOT NULL, rep_delivery INTEGER NOT NULL DEFAULT 0, rep_brand INTEGER NOT NULL DEFAULT 0,
  deliveries_ok INTEGER NOT NULL DEFAULT 0, tier INTEGER NOT NULL DEFAULT 1, pool_type TEXT NOT NULL DEFAULT 'NONE',
  depot INTEGER NOT NULL DEFAULT 0, warehouse INTEGER NOT NULL DEFAULT 0, manager INTEGER NOT NULL DEFAULT 0,
  b3_cert INTEGER NOT NULL DEFAULT 0, mechanics INTEGER NOT NULL DEFAULT 0, last_day INTEGER NOT NULL DEFAULT 0,
  next_month_day INTEGER NOT NULL DEFAULT 30, arrears INTEGER NOT NULL DEFAULT 0, premium_penalty REAL NOT NULL DEFAULT 1,
  bankrupt INTEGER NOT NULL DEFAULT 0, ad_discount INTEGER NOT NULL DEFAULT 0,
  mgr_service_th INTEGER NOT NULL DEFAULT 30, mgr_uj_mult REAL NOT NULL DEFAULT 1.1, mgr_min_margin REAL NOT NULL DEFAULT 0.25,
  social_last_day INTEGER NOT NULL DEFAULT -99, pool_skin TEXT NOT NULL DEFAULT '',
  tutorial_state INTEGER NOT NULL DEFAULT 0, tutorial_reward INTEGER NOT NULL DEFAULT 0, tutorial_report INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE nodes (id INTEGER PRIMARY KEY, name TEXT NOT NULL, island TEXT NOT NULL);
CREATE TABLE edges (
  id INTEGER PRIMARY KEY AUTOINCREMENT, a INTEGER NOT NULL, b INTEGER NOT NULL, name TEXT NOT NULL, km INTEGER NOT NULL,
  kind TEXT NOT NULL, steep INTEGER NOT NULL, rawan INTEGER NOT NULL, toll INTEGER NOT NULL,
  weigh INTEGER NOT NULL, pantura INTEGER NOT NULL, ferry_fee INTEGER NOT NULL
);

CREATE TABLE truck_catalog (
  id INTEGER PRIMARY KEY, name TEXT NOT NULL, tier INTEGER NOT NULL, price INTEGER NOT NULL, km_per_l REAL NOT NULL,
  max_load INTEGER NOT NULL, axles INTEGER NOT NULL, sim TEXT NOT NULL, fuel_req TEXT NOT NULL,
  factory_body TEXT NULL, is_euro INTEGER NOT NULL
);
CREATE TABLE bodies (code TEXT PRIMARY KEY, name TEXT NOT NULL, price INTEGER NOT NULL, days INTEGER NOT NULL, weight_kg INTEGER NOT NULL);
CREATE TABLE cargo_types (
  code TEXT PRIMARY KEY, name TEXT NOT NULL, min_tier INTEGER NOT NULL, bodies TEXT NOT NULL,
  rate REAL NOT NULL, min_kg INTEGER NOT NULL, max_kg INTEGER NOT NULL, b3 INTEGER NOT NULL
);

CREATE TABLE trucks (
  id INTEGER PRIMARY KEY AUTOINCREMENT, player_id INTEGER NOT NULL, catalog_id INTEGER NOT NULL, body_code TEXT NULL,
  status TEXT NOT NULL DEFAULT 'IDLE', body_ready_at TEXT NULL,
  oil_pct INTEGER NOT NULL DEFAULT 100, brake_pct INTEGER NOT NULL DEFAULT 100, tire_pct INTEGER NOT NULL DEFAULT 100,
  filter_pct INTEGER NOT NULL DEFAULT 100, engine_pct INTEGER NOT NULL DEFAULT 100,
  fuel_pref TEXT NOT NULL DEFAULT 'BIO', insurance TEXT NOT NULL DEFAULT 'NONE', cargo_ins INTEGER NOT NULL DEFAULT 0,
  purchase_price INTEGER NOT NULL, bought_day INTEGER NOT NULL, driver_id INTEGER NULL, is_used INTEGER NOT NULL DEFAULT 0,
  livery TEXT NOT NULL DEFAULT '', horn TEXT NOT NULL DEFAULT '', led TEXT NOT NULL DEFAULT '', sticker TEXT NOT NULL DEFAULT ''
);
CREATE TABLE used_listings (
  id INTEGER PRIMARY KEY AUTOINCREMENT, player_id INTEGER NOT NULL, catalog_id INTEGER NOT NULL, body_code TEXT NOT NULL,
  price INTEGER NOT NULL, age_days INTEGER NOT NULL, oil_pct INTEGER NOT NULL, brake_pct INTEGER NOT NULL, tire_pct INTEGER NOT NULL,
  filter_pct INTEGER NOT NULL, engine_pct INTEGER NOT NULL
);

CREATE TABLE drivers (
  id INTEGER PRIMARY KEY AUTOINCREMENT, player_id INTEGER NOT NULL, name TEXT NOT NULL, sim TEXT NOT NULL,
  speed INTEGER NOT NULL, eco INTEGER NOT NULL, honesty INTEGER NOT NULL, honesty_claim INTEGER NOT NULL, exp_years INTEGER NOT NULL,
  crash_count INTEGER NOT NULL, salary_exp INTEGER NOT NULL, salary INTEGER NOT NULL DEFAULT 0, uj_exp REAL NOT NULL,
  commission_pct REAL NOT NULL, morale INTEGER NOT NULL DEFAULT 70, status TEXT NOT NULL DEFAULT 'CANDIDATE',
  interviewed INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE contracts (
  id INTEGER PRIMARY KEY AUTOINCREMENT, player_id INTEGER NOT NULL, kind TEXT NOT NULL, client TEXT NOT NULL,
  origin TEXT NOT NULL, dest TEXT NOT NULL, cargo_code TEXT NOT NULL, weight_kg INTEGER NOT NULL,
  fee INTEGER NOT NULL, rep_required INTEGER NOT NULL DEFAULT 0, status TEXT NOT NULL DEFAULT 'OPEN',
  created_day INTEGER NOT NULL, expire_day INTEGER NOT NULL, months_total INTEGER NOT NULL DEFAULT 0, months_done INTEGER NOT NULL DEFAULT 0,
  trips_per_month INTEGER NOT NULL DEFAULT 0, trips_done_month INTEGER NOT NULL DEFAULT 0, monthly_pay INTEGER NOT NULL DEFAULT 0,
  vehicle_class TEXT NOT NULL DEFAULT '', quote_km REAL NOT NULL DEFAULT 0, quote_rate INTEGER NOT NULL DEFAULT 0, quote_mult REAL NOT NULL DEFAULT 1
);

CREATE TABLE trips (
  id INTEGER PRIMARY KEY AUTOINCREMENT, player_id INTEGER NOT NULL, truck_id INTEGER NOT NULL, driver_id INTEGER NOT NULL, contract_id INTEGER NOT NULL,
  path TEXT NOT NULL, km_road REAL NOT NULL, km_ferry REAL NOT NULL, steep INTEGER NOT NULL,
  uj_given INTEGER NOT NULL, cost_est INTEGER NOT NULL, fuel_cost INTEGER NOT NULL, fee INTEGER NOT NULL,
  overload_pct INTEGER NOT NULL, odol_policy TEXT NOT NULL, security TEXT NOT NULL, fuel_mode TEXT NOT NULL,
  fuel_type TEXT NOT NULL, uj_mult REAL NOT NULL, started_at TEXT NOT NULL, arrive_at TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'RUNNING', notes TEXT NOT NULL DEFAULT '', result TEXT NOT NULL DEFAULT '',
  ins_type TEXT NOT NULL DEFAULT 'NONE', cargo_ins INTEGER NOT NULL DEFAULT 0,
  wear_est INTEGER NOT NULL DEFAULT 0, overhead_est INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE world_events (
  id INTEGER PRIMARY KEY AUTOINCREMENT, player_id INTEGER NOT NULL, code TEXT NOT NULL, title TEXT NOT NULL,
  start_day INTEGER NOT NULL, end_day INTEGER NOT NULL, magnitude REAL NOT NULL, cargos TEXT NOT NULL DEFAULT ''
);
CREATE TABLE loans (
  id INTEGER PRIMARY KEY AUTOINCREMENT, player_id INTEGER NOT NULL, principal INTEGER NOT NULL, remaining INTEGER NOT NULL,
  installment INTEGER NOT NULL, months_left INTEGER NOT NULL, missed INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE cosmetics (id INTEGER PRIMARY KEY, kind TEXT NOT NULL, name TEXT NOT NULL, price_idr INTEGER NOT NULL);
CREATE TABLE owned_cosmetics (player_id INTEGER NOT NULL, item_id INTEGER NOT NULL, PRIMARY KEY (player_id, item_id));
CREATE TABLE topups (id INTEGER PRIMARY KEY AUTOINCREMENT, player_id INTEGER NOT NULL, pack TEXT NOT NULL, cash INTEGER NOT NULL, price_idr INTEGER NOT NULL, created_at TEXT NOT NULL);

INSERT INTO nodes VALUES
(1,'Jakarta','Jawa'),(2,'Bekasi','Jawa'),(3,'Tangerang','Jawa'),(4,'Depok','Jawa'),(5,'Bogor','Jawa'),(6,'Merak','Jawa'),
(7,'Bandung','Jawa'),(8,'Cimahi','Jawa'),(9,'Cirebon','Jawa'),(10,'Tasikmalaya','Jawa'),(11,'Semarang','Jawa'),
(12,'Ungaran','Jawa'),(13,'Yogyakarta','Jawa'),(14,'Surabaya','Jawa'),(15,'Sidoarjo','Jawa'),(16,'Malang','Jawa'),
(17,'Banyuwangi','Jawa'),(18,'Bakauheni','Sumatra'),(19,'Bandar Lampung','Sumatra'),(20,'Palembang','Sumatra'),
(21,'Pekanbaru','Sumatra'),(22,'Medan','Sumatra'),(23,'Gilimanuk','Bali'),(24,'Denpasar','Bali'),
(25,'Makassar','Sulawesi'),(26,'Parepare','Sulawesi'),(27,'Banjarmasin','Kalimantan'),(28,'Balikpapan','Kalimantan');

INSERT INTO edges (a,b,name,km,kind,steep,rawan,toll,weigh,pantura,ferry_fee) VALUES
(1,2,'Tol Jakarta-Bekasi',25,'ROAD',0,0,1,0,0,0),
(1,3,'Tol Jakarta-Tangerang',30,'ROAD',0,0,1,0,0,0),
(1,4,'Tol Jagorawi Depok',30,'ROAD',0,0,1,0,0,0),
(4,5,'Depok-Bogor',28,'ROAD',0,0,1,0,0,0),
(1,6,'Tol Jakarta-Merak',95,'ROAD',0,1,1,1,0,0),
(6,18,'Roro Merak-Bakauheni',30,'FERRY',0,0,0,0,0,1100000),
(18,19,'Bakauheni-Bandar Lampung',90,'ROAD',0,1,1,0,0,0),
(19,20,'Lintas Timur Lampung-Palembang',380,'ROAD',0,2,0,1,0,0),
(20,21,'Palembang-Pekanbaru',650,'ROAD',0,3,0,1,0,0),
(21,22,'Pekanbaru-Medan',780,'ROAD',0,2,0,1,0,0),
(1,7,'Tol Cipularang',150,'ROAD',0,0,1,0,0,0),
(7,8,'Bandung-Cimahi',15,'ROAD',0,0,0,0,0,0),
(7,10,'Nagreg-Tasikmalaya',110,'ROAD',1,1,0,0,0,0),
(10,13,'Tasikmalaya-Yogyakarta',270,'ROAD',0,1,0,1,0,0),
(1,9,'Pantura Jakarta-Cirebon',260,'ROAD',0,1,1,1,1,0),
(9,11,'Alas Roban',240,'ROAD',1,2,0,1,1,0),
(11,12,'Semarang-Ungaran',25,'ROAD',0,0,0,0,0,0),
(11,13,'Semarang-Yogyakarta',120,'ROAD',0,0,0,0,0,0),
(11,14,'Pantura Semarang-Surabaya',320,'ROAD',0,1,1,1,1,0),
(13,14,'Yogyakarta-Surabaya',320,'ROAD',0,1,0,1,0,0),
(14,15,'Tol Surabaya-Sidoarjo',25,'ROAD',0,0,1,0,0,0),
(14,16,'Tol Surabaya-Malang',90,'ROAD',0,0,1,0,0,0),
(14,17,'Situbondo Surabaya-Banyuwangi',290,'ROAD',1,1,0,1,1,0),
(17,23,'Roro Ketapang-Gilimanuk',8,'FERRY',0,0,0,0,0,380000),
(23,24,'Gilimanuk-Denpasar',130,'ROAD',1,0,0,0,0,0),
(14,25,'Roro Tanjung Perak-Makassar',1100,'FERRY',0,0,0,0,0,6000000),
(25,26,'Makassar-Parepare',150,'ROAD',1,1,0,0,0,0),
(14,27,'Roro Tanjung Perak-Banjarmasin',650,'FERRY',0,0,0,0,0,4500000),
(27,28,'Banjarmasin-Balikpapan',480,'ROAD',0,2,0,1,0,0);

INSERT INTO truck_catalog (id,name,tier,price,km_per_l,max_load,axles,sim,fuel_req,factory_body,is_euro) VALUES
(1,'Grand Maxx Pick-Up',1,190000000,9.0,900,2,'A','ANY','PICKUP',0),
(2,'Mitsu L-3000 Pick-Up',1,215000000,8.5,1100,2,'A','ANY','PICKUP',0),
(3,'Blind Van Grand Maxx',1,180000000,10.0,750,2,'A','ANY','BLINDVAN',0),
(4,'Isusu Elf Engkel CDE',2,310000000,6.5,4200,2,'B1','ANY',NULL,0),
(5,'Hinoo Dutro Engkel CDE',2,335000000,6.0,4500,2,'B1','ANY',NULL,0),
(6,'Isusu NMR Double CDD',2,430000000,5.5,6500,2,'B1','ANY',NULL,0),
(7,'Hinoo Ranger Medium Duty',2,660000000,4.5,10000,2,'B1','ANY',NULL,0),
(8,'Mitsu-Fuso Fighter Medium Duty',2,710000000,4.2,11000,2,'B1','ANY',NULL,0),
(9,'Hinoo 500 Tronton',3,920000000,3.5,22000,3,'B2','ANY',NULL,0),
(10,'Mitsu-Fuso Dump 10-Roda',3,980000000,3.2,20000,3,'B2','ANY',NULL,0),
(11,'Mercy-Truck Axor Head Tractor',3,1650000000,2.8,32000,3,'B2','DEX',NULL,1),
(12,'Volvoo FH Heavy Duty',3,2400000000,3.0,40000,3,'B2','DEX',NULL,1),
(13,'Scanya R-Series Heavy Duty',3,2600000000,3.0,42000,3,'B2','DEX',NULL,1);

INSERT INTO bodies VALUES
('PICKUP','Bak Pick-Up Standar',0,0,150),('BLINDVAN','Blind Van',0,0,250),
('BAKKAYU','Bak Kayu',20000000,1,700),('BOXBESI','Box Besi',45000000,2,1200),
('WINGSET','Wingset',85000000,3,2000),('DUMP','Dump Hidrolik',120000000,3,2500),
('FLATBED','Flatbed Trailer',90000000,2,3000),('TANKI','Tanki Kimia/BBM',180000000,3,3500);

INSERT INTO cargo_types VALUES
('PAKET','Paket E-Commerce',1,'PICKUP,BLINDVAN,BOXBESI,WINGSET',6000,150,700,0),
('SAYUR','Sayur Segar Pasar',1,'PICKUP,BAKKAYU',3500,300,850,0),
('DOKUMEN','Dokumen',1,'PICKUP,BLINDVAN,BOXBESI',25000,20,100,0),
('ECERAN','Eceran Toko',1,'PICKUP,BLINDVAN,BOXBESI,WINGSET',5000,200,900,0),
('MAKANAN','Makanan Kemasan',2,'BOXBESI,WINGSET',1100,2000,8000,0),
('PABRIK','Barang Pabrik',2,'BOXBESI,WINGSET',950,3000,9000,0),
('TEKSTIL','Tekstil',2,'BOXBESI,WINGSET',1050,2000,7000,0),
('PERABOT','Perabotan',2,'BOXBESI,WINGSET,BAKKAYU',1000,2000,8000,0),
('KONTAINER','Kontainer',3,'FLATBED',240,12000,30000,0),
('BAJA','Besi/Baja',3,'FLATBED,BAKKAYU',215,12000,30000,0),
('ALATBERAT','Alat Berat Excavator',3,'FLATBED',380,10000,30000,0),
('TAMBANG','Bahan Tambang',3,'DUMP',230,12000,22000,0),
('SAWIT','Sawit TBS',3,'DUMP,BAKKAYU',215,10000,22000,0),
('PUPUK','Pupuk',3,'BOXBESI,WINGSET,BAKKAYU',195,10000,25000,0),
('KIMIA','Tangki Kimia B3',3,'TANKI',300,8000,28000,1),
('BBM','Tangki BBM B3',3,'TANKI',270,8000,28000,1);

INSERT INTO cosmetics VALUES
(1,'LIVERY','Livery Garuda Hitam',29000),(2,'LIVERY','Livery Batik Parang',35000),
(3,'HORN','Klakson Telolet Basuri',19000),(4,'HORN','Klakson Angin Triple',25000),
(5,'LED','Lampu LED Strobo Pelangi',15000),(6,'STIKER','Stiker Kaca Jangan Ngebut Sayang',9000),
(7,'POOLSKIN','Skin Pool Tropis',49000);

CREATE TABLE ledger (
  id INTEGER PRIMARY KEY AUTOINCREMENT, player_id INTEGER NOT NULL, game_day INTEGER NOT NULL, created_at TEXT NOT NULL,
  category TEXT NOT NULL, note TEXT NOT NULL DEFAULT '', trip_id INTEGER NULL,
  amount INTEGER NOT NULL, balance_after INTEGER NOT NULL
);

CREATE INDEX idx_ledger_player ON ledger(player_id, id);
CREATE INDEX idx_ledger_trip ON ledger(trip_id);
CREATE INDEX idx_trucks_player ON trucks(player_id, status);
CREATE INDEX idx_drivers_player ON drivers(player_id, status);
CREATE INDEX idx_contracts_player ON contracts(player_id, kind, status);
CREATE INDEX idx_trips_player ON trips(player_id, status);
CREATE INDEX idx_trips_arrive ON trips(status, arrive_at);
CREATE INDEX idx_events_player ON world_events(player_id, start_day, end_day);
CREATE INDEX idx_used_player ON used_listings(player_id);

CREATE TABLE save_meta (
  id INTEGER PRIMARY KEY CHECK (id = 1), save_id TEXT NOT NULL, company_name TEXT NOT NULL,
  created_at_utc TEXT NOT NULL, updated_at_utc TEXT NOT NULL, schema_version INTEGER NOT NULL, game_version TEXT NOT NULL
);

CREATE TABLE game_state (
  id INTEGER PRIMARY KEY CHECK (id = 1), sim_minutes REAL NOT NULL DEFAULT 0, speed REAL NOT NULL DEFAULT 1,
  rng_seed INTEGER NOT NULL DEFAULT 0, last_event_sequence INTEGER NOT NULL DEFAULT 0
);

""";
}