namespace Nusantara.Database;

// Migrasi v3 (BELUM terdaftar di Db.Migrations).
// Hanya MENAMBAH kolom dan tabel, tidak mengubah data lama, jadi aman untuk save lama.
public static class SchemaV3
{
    public const string Sql = """
    ALTER TABLE save_meta ADD COLUMN last_save_ok INTEGER NOT NULL DEFAULT 1;
    ALTER TABLE save_meta ADD COLUMN last_save_error TEXT NULL;
    ALTER TABLE save_meta ADD COLUMN last_save_utc TEXT NULL;

    -- Penanda pekerjaan berkala: (job, period) unik, jadi satu pekerjaan per periode hanya bisa diklaim sekali.
    CREATE TABLE sim_job_log (
    job TEXT NOT NULL,
    period INTEGER NOT NULL,
    claimed_day INTEGER NOT NULL,
    claimed_at_utc TEXT NOT NULL,
    PRIMARY KEY (job, period)
    );
    """;
}