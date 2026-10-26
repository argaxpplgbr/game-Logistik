using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Nusantara.Database;

namespace Nusantara.Services;

public class SlotInfo
{
    public int No;
    public string FilePath = "";
    public bool Exists, Broken, Bankrupt;
    public string Problem = "", Company = "", When = "", SavedLocal = "";
    public int Tier, Trucks;
    public long Cash;
    public bool HasBackup;
}

class MetaRow { public string CompanyName { get; set; } = ""; public string UpdatedAtUtc { get; set; } = ""; }
class StateRow { public double SimMinutes { get; set; } public double Speed { get; set; } = 1; public long RngSeed { get; set; } }
class PlayerRow { public long Cash { get; set; } public int Tier { get; set; } public bool Bankrupt { get; set; } public DateTime CreatedAt { get; set; } }

// Satu slot = satu file SQLite di folder saves/ (slot1.db ... slot10.db).
// Slot terisolasi total: katalog, perusahaan, kas, jam game, semuanya ada di file itu saja.
public static class SaveSlots
{
    public const int MaxSlots = 10;
    static readonly object saveGate = new();

    public static int CurrentNo { get; private set; }
    public static int CurrentPlayerId { get; private set; }

    public static string Dir
    {
        get
        {
            var d = Environment.GetEnvironmentVariable("NUSANTARA_SAVE_DIR");
            return string.IsNullOrWhiteSpace(d) ? Path.Combine(Directory.GetCurrentDirectory(), "saves") : d;
        }
    }

    public static string PathOf(int no) => Path.Combine(Dir, $"slot{no}.db");

    static string UtcText() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    static void TryDelete(string f)
    {
        try { if (File.Exists(f)) File.Delete(f); } catch { /* diabaikan */ }
    }

    // ---- Nama perusahaan ----
    public static string? ValidateName(string raw, out string clean)
    {
        clean = new string((raw ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (clean.Length == 0) return "Nama perusahaan tidak boleh kosong.";
        if (clean.Length < 2) return "Nama perusahaan minimal 2 karakter.";
        if (clean.Length > 40) return "Nama perusahaan maksimal 40 karakter.";
        return null;
    }

    // ---- Daftar slot (dibaca tanpa mengubah apa pun) ----
    public static List<SlotInfo> List()
    {
        var res = new List<SlotInfo>();
        for (int n = 1; n <= MaxSlots; n++) res.Add(Inspect(n));
        return res;
    }

    static SlotInfo Inspect(int no)
    {
        var s = new SlotInfo { No = no, FilePath = PathOf(no) };
        if (!File.Exists(s.FilePath)) return s;
        s.Exists = true;
        s.HasBackup = File.Exists(s.FilePath + ".bak");
        try
        {
            using var c = new SqliteConnection(Db.BuildConnStr(s.FilePath, createIfMissing: false, pooling: false));
            c.Open();
            var meta = c.QueryFirstOrDefault<MetaRow>("SELECT company_name, updated_at_utc FROM save_meta WHERE id=1");
            var st = c.QueryFirstOrDefault<StateRow>("SELECT sim_minutes, speed, rng_seed FROM game_state WHERE id=1");
            var pl = c.QueryFirstOrDefault<PlayerRow>("SELECT cash, tier, bankrupt, created_at FROM players ORDER BY id LIMIT 1");
            if (meta == null || st == null || pl == null)
            {
                s.Broken = true; s.Problem = "data slot tidak lengkap";
                return s;
            }
            s.Company = meta.CompanyName;
            s.Cash = pl.Cash; s.Tier = pl.Tier; s.Bankrupt = pl.Bankrupt;
            s.Trucks = c.ExecuteScalar<int>("SELECT COUNT(*) FROM trucks WHERE status<>'TOTALLOSS'");
            s.When = SimClock.Describe(pl.CreatedAt, st.SimMinutes);
            if (DateTime.TryParse(meta.UpdatedAtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var u))
                s.SavedLocal = u.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        }
        catch (Exception ex)
        {
            s.Broken = true; s.Problem = ex.Message;
        }
        return s;
    }

    // ---- Buat slot baru. Mengembalikan id perusahaan pemain. ----
    public static int Create(int no, string company)
    {
        var err = ValidateName(company, out var name);
        if (err != null) throw new InvalidOperationException(err);
        if (no < 1 || no > MaxSlots) throw new InvalidOperationException("Nomor slot tidak valid.");

        Directory.CreateDirectory(Dir);
        var path = PathOf(no);
        if (File.Exists(path)) throw new InvalidOperationException($"Slot {no} sudah terisi.");

        try
        {
            Db.UseFile(path);
            Db.EnableWal();
            Db.Migrate();
            using (var c = Db.Open())
            {
                string now = UtcText();
                c.Execute(@"INSERT INTO save_meta(id,save_id,company_name,created_at_utc,updated_at_utc,schema_version,game_version)
                    VALUES(1,@sid,@name,@now,@now,@sv,@gv)",
                    new { sid = Guid.NewGuid().ToString("N"), name, now, sv = Db.SchemaVersion, gv = Db.GameVersion });
                c.Execute("INSERT INTO game_state(id,sim_minutes,speed,rng_seed,last_event_sequence) VALUES(1,0,1,@seed,0)",
                    new { seed = Random.Shared.Next() });
            }
            SimClock.Load(0, 1);
            var p = PlayerSvc.GetOrCreate(name);
            LedgerSvc.EnsureOpening(p.Id);
            using (var ce = Db.Open()) Events.Append(ce, null, EventTypes.CompanyFounded, new { company = p.Name, cash = p.Cash }, p.Id);
            CurrentNo = no; CurrentPlayerId = p.Id;
            Persist();
            return p.Id;
        }
        catch
        {
            // Jangan tinggalkan file setengah jadi.
            Db.Release();
            SimClock.Unload();
            TryDelete(path); TryDelete(path + "-wal"); TryDelete(path + "-shm");
            throw;
        }
    }

    // ---- Buka slot yang sudah ada. Mengembalikan id perusahaan pemain. ----
    public static int Open(int no)
    {
        var path = PathOf(no);
        if (!File.Exists(path)) throw new InvalidOperationException($"Slot {no} kosong.");
        Db.UseFile(path);
        try
        {
            var check = Db.QuickCheck();
            if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pemeriksaan integritas gagal: " + check);

            if (Db.PendingMigrations() > 0) Backup();   // cadangan dulu sebelum skema dinaikkan
            Db.Migrate();

            using var c = Db.Open();
            var st = c.QueryFirstOrDefault<StateRow>("SELECT sim_minutes, speed, rng_seed FROM game_state WHERE id=1")
                     ?? throw new InvalidDataException("Data jam game tidak ditemukan.");
            int pid = c.ExecuteScalar<int?>("SELECT id FROM players ORDER BY id LIMIT 1")
                      ?? throw new InvalidDataException("Data perusahaan tidak ditemukan.");

            // Jam dimulai dari nilai tersimpan. Waktu saat game tertutup TIDAK dihitung.
            SimClock.Load(st.SimMinutes, st.Speed);
            CurrentNo = no; CurrentPlayerId = pid;
            return pid;
        }
        catch
        {
            Db.Release();
            SimClock.Unload();
            throw;
        }
    }

    // ---- Simpan jam game ke slot aktif (data lain sudah tersimpan per transaksi). ----
    public static void Persist()
    {
        lock (saveGate)
        {
            if (!Db.HasSlot || !SimClock.Loaded) return;
            try
            {
                using var c = Db.Open();
                c.Execute("UPDATE game_state SET sim_minutes=@m, speed=@s WHERE id=1", new { m = SimClock.Minutes, s = SimClock.Speed });
                c.Execute("UPDATE save_meta SET updated_at_utc=@u WHERE id=1", new { u = UtcText() });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Peringatan: gagal menyimpan jam game: " + ex.Message);
            }
        }
    }

    // ---- Cadangan bergilir: satu file slotN.db.bak per slot (selalu ditimpa dengan yang terbaru). ----
    public static string Backup()
    {
        lock (saveGate)
        {
            if (!Db.HasSlot) return "Tidak ada slot aktif.";
            Persist();
            var bak = Db.FilePath + ".bak";
            var tmp = Db.FilePath + ".bak.tmp";
            try
            {
                TryDelete(tmp);
                using (var src = Db.Open())
                using (var dst = new SqliteConnection(Db.BuildConnStr(tmp, createIfMissing: true, pooling: false)))
                {
                    dst.Open();
                    src.BackupDatabase(dst);
                }
                File.Move(tmp, bak, overwrite: true);
                return "Tersimpan. Cadangan: " + Path.GetFileName(bak);
            }
            catch (Exception ex)
            {
                TryDelete(tmp);
                return "Gagal membuat cadangan: " + ex.Message;
            }
        }
    }

    // ---- Tutup slot aktif dengan rapi (aman dipanggil berkali-kali). ----
    public static void Shutdown()
    {
        lock (saveGate)
        {
            if (!Db.HasSlot) return;
            Persist();
            Backup();
            try
            {
                using var c = Db.Open();
                c.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
            }
            catch { /* tidak fatal */ }
            SimClock.Unload();
            Db.Release();
            CurrentNo = 0; CurrentPlayerId = 0;
        }
    }

    public static string Delete(int no)
    {
        if (Db.HasSlot && CurrentNo == no) return "Slot sedang dipakai.";
        var path = PathOf(no);
        if (!File.Exists(path)) return "Slot sudah kosong.";
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, path + "-wal", path + "-shm", path + ".bak", path + ".bak.tmp" }) TryDelete(f);
        return File.Exists(path) ? "Gagal menghapus file slot." : $"Slot {no} dihapus.";
    }

    public static string RestoreBackup(int no)
    {
        if (Db.HasSlot && CurrentNo == no) return "Slot sedang dipakai.";
        var path = PathOf(no);
        var bak = path + ".bak";
        if (!File.Exists(bak)) return "Tidak ada cadangan untuk slot ini.";
        try
        {
            SqliteConnection.ClearAllPools();
            TryDelete(path + "-wal"); TryDelete(path + "-shm");
            File.Copy(bak, path, overwrite: true);
            return $"Slot {no} dipulihkan dari cadangan terakhir.";
        }
        catch (Exception ex)
        {
            return "Gagal memulihkan: " + ex.Message;
        }
    }
}