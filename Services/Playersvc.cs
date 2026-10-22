using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

public static class PlayerSvc
{
    public static Player Get(int id) { using var c = Db.Open(); return c.QueryFirst<Player>("SELECT * FROM players WHERE id=@id", new { id }); }

    public static Player GetOrCreate(string name)
    {
        using var c = Db.Open();
        var p = c.QueryFirstOrDefault<Player>("SELECT * FROM players WHERE name=@name", new { name });
        if (p != null) return p;
        c.Execute("INSERT INTO players(name,created_at,cash) VALUES(@name,@now,300000000)", new { name, now = DateTime.UtcNow });
        p = c.QueryFirst<Player>("SELECT * FROM players WHERE name=@name", new { name });
        HrSvc.Refresh(p.Id, false);
        WorldSvc.Daily(p.Id, 0);
        return p;
    }

    public static int Day(Player p) => (int)((DateTime.UtcNow - p.CreatedAt).TotalMinutes / Rules.DayMinutes);

        static string Spend(int id, long cost, string setSql, string ok)
    {
        using var c = Db.Open(); using var tx = c.BeginTransaction();
        if (!Db.Spend(c, tx, id, cost)) return $"Uang kurang ({Rules.Rp(cost)}).";
        c.Execute($"UPDATE players SET {setSql} WHERE id=@id", new { id }, tx);
        tx.Commit();
        return ok;
    }

    // ---- Tier / legalitas ----
    public static string UpgradeTier(int id)
    {
        var p = Get(id);
        if (p.Tier == 1)
        {
            if (p.Reputation < 50) return "Butuh reputasi minimal 50 (0,5 bintang) dari pengiriman sukses.";
            return Spend(id, 150_000_000, "tier=2", "Resmi menjadi CV (izin KIU/KIR). Engkel, Double & Medium Duty terbuka!");
        }
        if (p.Tier == 2)
        {
            if (p.Reputation < 250) return "Butuh reputasi minimal 250 (2,5 bintang).";
            if (p.PoolType == "NONE") return "Butuh Pool (sewa/milik) dulu.";
            return Spend(id, 1_200_000_000, "tier=3", "Resmi menjadi PT (SMK lengkap). Tronton, Trailer, antar-pulau terbuka!");
        }
        return "Sudah di tier tertinggi.";
    }

    public static string BuyB3(int id)
    {
        var p = Get(id);
        if (p.Tier < 3) return "Butuh status PT.";
        if (p.B3Cert) return "Sertifikasi B3 sudah dimiliki.";
        return Spend(id, 150_000_000, "b3_cert=1", "Sertifikasi B3 didapat. Kargo kimia & BBM terbuka.");
    }

    // ---- Fasilitas ----
    public static string RentPool(int id)
    {
        var p = Get(id);
        if (p.Tier < 2) return "Butuh status CV.";
        if (p.PoolType != "NONE") return "Sudah punya pool.";
        return Spend(id, 10_000_000, "pool_type='SEWA'", "Pool sewa aktif (Rp 6 jt/bulan). Kini bisa mempekerjakan mekanik tetap.");
    }
    public static string BuildPool(int id)
    {
        var p = Get(id);
        if (p.Tier < 3) return "Butuh status PT.";
        if (p.PoolType == "MILIK") return "Sudah punya pool sendiri.";
        return Spend(id, 250_000_000, "pool_type='MILIK'", "Pool sendiri dibangun (tanpa sewa bulanan).");
    }
    public static string BuildDepot(int id)
    {
        var p = Get(id);
        if (p.PoolType != "MILIK") return "Butuh pool milik sendiri.";
        if (p.Depot) return "Depo solar sudah ada.";
        return Spend(id, 400_000_000, "depot=1", "Depo solar jadi: BBM 15% lebih murah.");
    }
    public static string BuildWarehouse(int id)
    {
        var p = Get(id);
        if (p.PoolType != "MILIK") return "Butuh pool milik sendiri.";
        if (p.Warehouse) return "Gudang sparepart sudah ada.";
        return Spend(id, 300_000_000, "warehouse=1", "Gudang sparepart mandiri jadi: biaya servis 15% lebih murah.");
    }
    public static string HireMechanic(int id)
    {
        var p = Get(id);
        if (p.PoolType == "NONE") return "Butuh pool dulu.";
        if (p.Mechanics >= 5) return "Maksimal 5 mekanik.";
        return Spend(id, 0, "mechanics=mechanics+1", "Mekanik tetap direkrut (Rp 5 jt/bulan). Biaya servis jadi lebih hemat.");
    }
    public static string FireMechanic(int id)
    {
        var p = Get(id);
        if (p.Mechanics <= 0) return "Tidak ada mekanik.";
        return Spend(id, 0, "mechanics=mechanics-1", "Mekanik diberhentikan.");
    }
    public static string HireManager(int id)
    {
        var p = Get(id);
        if (p.Tier < 2) return "Butuh status CV.";
        if (p.Manager) return "Manajer sudah ada.";
        return Spend(id, 5_000_000, "manager=1", "Manajer Operasional dipekerjakan (Rp 15 jt/bulan). Atur rule-nya.");
    }
    public static void SetManagerRules(int id, int th, double mult, double margin)
    {
        using var c = Db.Open();
        c.Execute("UPDATE players SET mgr_service_th=@th,mgr_uj_mult=@mult,mgr_min_margin=@margin WHERE id=@id", new { th, mult, margin, id });
    }

    // ---- Media sosial (marketing) ----
    public static readonly (string name, long cost, int gain, int minTier)[] Campaigns =
    {
        ("Iklan Sosmed (IG/TikTok)", 5_000_000, 3, 1),
        ("Kolaborasi Konten Trucker", 20_000_000, 10, 2),
        ("Iklan TV Nasional", 100_000_000, 25, 3)
    };

    public static string Marketing(int id, int idx)
    {
        if (idx < 0 || idx >= Campaigns.Length) return "Pilihan tidak ada.";
        var p = Get(id); int day = Day(p); var cp = Campaigns[idx];
        if (p.Tier < cp.minTier) return "Tier belum cukup.";
        if (day - p.SocialLastDay < 3) return "Kampanye berikutnya baru bisa setelah 3 hari game.";
        if (p.RepBrand >= 100) return "Brand awareness sudah maksimal (100). Sisanya wajib dari pengiriman sukses.";
        if (p.Cash < cp.cost) return "Uang kurang.";
        int g = Math.Min(cp.gain, 100 - p.RepBrand);
        using var c = Db.Open();
        c.Execute("UPDATE players SET cash=cash-@cost,rep_brand=rep_brand+@g,social_last_day=@day WHERE id=@id", new { cost = cp.cost, g, day, id });
        return $"Kampanye '{cp.name}' sukses: reputasi brand +{g}.";
    }

    // ---- Monetisasi (simulasi) ----
    public static readonly (string name, long cash, int idr)[] Packs =
    {
        ("Dana Darurat Kecil", 50_000_000, 15000),
        ("Dana Ekspansi", 200_000_000, 49000),
        ("Dana Konglomerat", 500_000_000, 99000)
    };

        public static string TopUp(int id, int idx)
    {
        if (idx < 0 || idx >= Packs.Length) return "Paket tidak ada.";
        var pk = Packs[idx]; using var c = Db.Open(); using var tx = c.BeginTransaction();
        c.Execute("INSERT INTO topups(player_id,pack,cash,price_idr,created_at) VALUES(@id,@n,@cash,@idr,@now)",
            new { id, n = pk.name, cash = pk.cash, idr = pk.idr, now = DateTime.UtcNow }, tx);
        c.Execute("UPDATE players SET cash=cash+@cash WHERE id=@id", new { cash = pk.cash, id }, tx);
        tx.Commit();
        return $"[SIMULASI bayar Rp {pk.idr:N0}] +{Rules.Rp(pk.cash)}. Ingat: rating/reputasi tidak bisa dibeli, kontrak kelas atas tetap terkunci.";
    }

    public static List<Cosmetic> Shop(int pid)
    {
        using var c = Db.Open();
        return c.Query<Cosmetic>(@"SELECT s.*, (SELECT COUNT(*) FROM owned_cosmetics o WHERE o.player_id=@pid AND o.item_id=s.id) owned FROM cosmetics s", new { pid }).ToList();
    }

    public static string BuyCosmetic(int pid, int itemId)
    {
        using var c = Db.Open();
        var it = c.QueryFirstOrDefault<Cosmetic>("SELECT * FROM cosmetics WHERE id=@itemId", new { itemId });
        if (it == null) return "Item tidak ada.";
        c.Execute("INSERT IGNORE INTO owned_cosmetics(player_id,item_id) VALUES(@pid,@itemId)", new { pid, itemId });
                return $"[SIMULASI] {it.Name} dimiliki. Harga Rp {it.PriceIdr:N0} hanya contoh (belum ada pembayaran sungguhan), kas game tidak dipotong. Pasang lewat menu Armada.";
    }

    public static string Equip(int pid, int truckId, int itemId)
    {
        using var c = Db.Open();
        var it = c.QueryFirstOrDefault<Cosmetic>("SELECT * FROM cosmetics WHERE id=@itemId", new { itemId });
        if (it == null) return "Item tidak ada.";
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM owned_cosmetics WHERE player_id=@pid AND item_id=@itemId", new { pid, itemId }) == 0) return "Belum dimiliki.";
        if (it.Kind == "POOLSKIN") { c.Execute("UPDATE players SET pool_skin=@n WHERE id=@pid", new { n = it.Name, pid }); return "Skin pool dipasang."; }
        string col = it.Kind == "LIVERY" ? "livery" : it.Kind == "HORN" ? "horn" : it.Kind == "LED" ? "led" : "sticker";
        int n = c.Execute($"UPDATE trucks SET {col}=@name WHERE id=@truckId AND player_id=@pid", new { name = it.Name, truckId, pid });
        return n > 0 ? $"{it.Name} dipasang di truk {truckId}." : "Truk tidak ditemukan.";
    }

    // ---- Rewarded ads (efek setelah iklan ditonton) ----
    public static string AdReward(int pid, string kind, int truckId = 0)
    {
        using var c = Db.Open();
        if (kind == "DISKON") { c.Execute("UPDATE players SET ad_discount=1 WHERE id=@pid", new { pid }); return "Diskon sparepart 15% aktif untuk servis berikutnya."; }
        if (kind == "CV") { HrSvc.Refresh(pid, false); return "Daftar CV supir diperbarui gratis."; }
        var t = FleetSvc.One(truckId);
        if (t == null || t.PlayerId != pid || t.Status != "KAROSERI" || t.BodyReadyAt == null) return "Truk tidak sedang di karoseri.";
        var now = DateTime.UtcNow; var rem = t.BodyReadyAt.Value - now;
        c.Execute("UPDATE trucks SET body_ready_at=@r WHERE id=@truckId", new { r = now + rem / 2, truckId });
        return "Pengerjaan karoseri dipercepat 50%.";
    }
}