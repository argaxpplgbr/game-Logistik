DROP DATABASE IF EXISTS nusantara_logistics;
CREATE DATABASE nusantara_logistics CHARACTER SET utf8mb4;
USE nusantara_logistics;

CREATE TABLE players (
  id INT AUTO_INCREMENT PRIMARY KEY, name VARCHAR(60) UNIQUE NOT NULL, created_at DATETIME NOT NULL,
  cash BIGINT NOT NULL, rep_delivery INT NOT NULL DEFAULT 0, rep_brand INT NOT NULL DEFAULT 0,
  deliveries_ok INT NOT NULL DEFAULT 0, tier INT NOT NULL DEFAULT 1, pool_type VARCHAR(10) NOT NULL DEFAULT 'NONE',
  depot TINYINT(1) NOT NULL DEFAULT 0, warehouse TINYINT(1) NOT NULL DEFAULT 0, manager TINYINT(1) NOT NULL DEFAULT 0,
  b3_cert TINYINT(1) NOT NULL DEFAULT 0, mechanics INT NOT NULL DEFAULT 0, last_day INT NOT NULL DEFAULT 0,
  next_month_day INT NOT NULL DEFAULT 30, arrears BIGINT NOT NULL DEFAULT 0, premium_penalty DOUBLE NOT NULL DEFAULT 1,
  bankrupt TINYINT(1) NOT NULL DEFAULT 0, ad_discount TINYINT(1) NOT NULL DEFAULT 0,
  mgr_service_th INT NOT NULL DEFAULT 30, mgr_uj_mult DOUBLE NOT NULL DEFAULT 1.1, mgr_min_margin DOUBLE NOT NULL DEFAULT 0.25,
  social_last_day INT NOT NULL DEFAULT -99, pool_skin VARCHAR(40) NOT NULL DEFAULT ''
);

CREATE TABLE nodes (id INT PRIMARY KEY, name VARCHAR(30) NOT NULL, island VARCHAR(20) NOT NULL);
CREATE TABLE edges (
  id INT AUTO_INCREMENT PRIMARY KEY, a INT NOT NULL, b INT NOT NULL, name VARCHAR(50) NOT NULL, km INT NOT NULL,
  kind VARCHAR(6) NOT NULL, steep TINYINT(1) NOT NULL, rawan INT NOT NULL, toll TINYINT(1) NOT NULL,
  weigh TINYINT(1) NOT NULL, pantura TINYINT(1) NOT NULL, ferry_fee BIGINT NOT NULL
);

CREATE TABLE truck_catalog (
  id INT PRIMARY KEY, name VARCHAR(60) NOT NULL, tier INT NOT NULL, price BIGINT NOT NULL, km_per_l DOUBLE NOT NULL,
  max_load INT NOT NULL, axles INT NOT NULL, sim VARCHAR(3) NOT NULL, fuel_req VARCHAR(5) NOT NULL,
  factory_body VARCHAR(12) NULL, is_euro TINYINT(1) NOT NULL
);
CREATE TABLE bodies (code VARCHAR(12) PRIMARY KEY, name VARCHAR(40) NOT NULL, price BIGINT NOT NULL, days INT NOT NULL, weight_kg INT NOT NULL);
CREATE TABLE cargo_types (
  code VARCHAR(12) PRIMARY KEY, name VARCHAR(40) NOT NULL, min_tier INT NOT NULL, bodies VARCHAR(80) NOT NULL,
  rate DOUBLE NOT NULL, min_kg INT NOT NULL, max_kg INT NOT NULL, b3 TINYINT(1) NOT NULL
);

CREATE TABLE trucks (
  id INT AUTO_INCREMENT PRIMARY KEY, player_id INT NOT NULL, catalog_id INT NOT NULL, body_code VARCHAR(12) NULL,
  status VARCHAR(12) NOT NULL DEFAULT 'IDLE', body_ready_at DATETIME NULL,
  oil_pct INT NOT NULL DEFAULT 100, brake_pct INT NOT NULL DEFAULT 100, tire_pct INT NOT NULL DEFAULT 100,
  filter_pct INT NOT NULL DEFAULT 100, engine_pct INT NOT NULL DEFAULT 100,
  fuel_pref VARCHAR(5) NOT NULL DEFAULT 'BIO', insurance VARCHAR(8) NOT NULL DEFAULT 'NONE', cargo_ins TINYINT(1) NOT NULL DEFAULT 0,
  purchase_price BIGINT NOT NULL, bought_day INT NOT NULL, driver_id INT NULL, is_used TINYINT(1) NOT NULL DEFAULT 0,
  livery VARCHAR(40) NOT NULL DEFAULT '', horn VARCHAR(40) NOT NULL DEFAULT '', led VARCHAR(40) NOT NULL DEFAULT '', sticker VARCHAR(40) NOT NULL DEFAULT ''
);
CREATE TABLE used_listings (
  id INT AUTO_INCREMENT PRIMARY KEY, player_id INT NOT NULL, catalog_id INT NOT NULL, body_code VARCHAR(12) NOT NULL,
  price BIGINT NOT NULL, age_days INT NOT NULL, oil_pct INT NOT NULL, brake_pct INT NOT NULL, tire_pct INT NOT NULL,
  filter_pct INT NOT NULL, engine_pct INT NOT NULL
);

CREATE TABLE drivers (
  id INT AUTO_INCREMENT PRIMARY KEY, player_id INT NOT NULL, name VARCHAR(60) NOT NULL, sim VARCHAR(3) NOT NULL,
  speed INT NOT NULL, eco INT NOT NULL, honesty INT NOT NULL, honesty_claim INT NOT NULL, exp_years INT NOT NULL,
  crash_count INT NOT NULL, salary_exp BIGINT NOT NULL, salary BIGINT NOT NULL DEFAULT 0, uj_exp DOUBLE NOT NULL,
  commission_pct DOUBLE NOT NULL, morale INT NOT NULL DEFAULT 70, status VARCHAR(10) NOT NULL DEFAULT 'CANDIDATE',
  interviewed TINYINT(1) NOT NULL DEFAULT 0
);

CREATE TABLE contracts (
  id INT AUTO_INCREMENT PRIMARY KEY, player_id INT NOT NULL, kind VARCHAR(10) NOT NULL, client VARCHAR(60) NOT NULL,
  origin VARCHAR(30) NOT NULL, dest VARCHAR(30) NOT NULL, cargo_code VARCHAR(12) NOT NULL, weight_kg INT NOT NULL,
  fee BIGINT NOT NULL, rep_required INT NOT NULL DEFAULT 0, status VARCHAR(10) NOT NULL DEFAULT 'OPEN',
  created_day INT NOT NULL, expire_day INT NOT NULL, months_total INT NOT NULL DEFAULT 0, months_done INT NOT NULL DEFAULT 0,
  trips_per_month INT NOT NULL DEFAULT 0, trips_done_month INT NOT NULL DEFAULT 0, monthly_pay BIGINT NOT NULL DEFAULT 0
);

CREATE TABLE trips (
  id INT AUTO_INCREMENT PRIMARY KEY, player_id INT NOT NULL, truck_id INT NOT NULL, driver_id INT NOT NULL, contract_id INT NOT NULL,
  path VARCHAR(400) NOT NULL, km_road DOUBLE NOT NULL, km_ferry DOUBLE NOT NULL, steep INT NOT NULL,
  uj_given BIGINT NOT NULL, cost_est BIGINT NOT NULL, fuel_cost BIGINT NOT NULL, fee BIGINT NOT NULL,
  overload_pct INT NOT NULL, odol_policy VARCHAR(10) NOT NULL, security VARCHAR(10) NOT NULL, fuel_mode VARCHAR(10) NOT NULL,
  fuel_type VARCHAR(5) NOT NULL, uj_mult DOUBLE NOT NULL, started_at DATETIME NOT NULL, arrive_at DATETIME NOT NULL,
  status VARCHAR(10) NOT NULL DEFAULT 'RUNNING', notes VARCHAR(500) NOT NULL DEFAULT '', result VARCHAR(600) NOT NULL DEFAULT ''
);

CREATE TABLE world_events (
  id INT AUTO_INCREMENT PRIMARY KEY, player_id INT NOT NULL, code VARCHAR(20) NOT NULL, title VARCHAR(160) NOT NULL,
  start_day INT NOT NULL, end_day INT NOT NULL, magnitude DOUBLE NOT NULL, cargos VARCHAR(60) NOT NULL DEFAULT ''
);
CREATE TABLE loans (
  id INT AUTO_INCREMENT PRIMARY KEY, player_id INT NOT NULL, principal BIGINT NOT NULL, remaining BIGINT NOT NULL,
  installment BIGINT NOT NULL, months_left INT NOT NULL, missed INT NOT NULL DEFAULT 0
);
CREATE TABLE cosmetics (id INT PRIMARY KEY, kind VARCHAR(10) NOT NULL, name VARCHAR(60) NOT NULL, price_idr INT NOT NULL);
CREATE TABLE owned_cosmetics (player_id INT NOT NULL, item_id INT NOT NULL, PRIMARY KEY (player_id, item_id));
CREATE TABLE topups (id INT AUTO_INCREMENT PRIMARY KEY, player_id INT NOT NULL, pack VARCHAR(40) NOT NULL, cash BIGINT NOT NULL, price_idr INT NOT NULL, created_at DATETIME NOT NULL);

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