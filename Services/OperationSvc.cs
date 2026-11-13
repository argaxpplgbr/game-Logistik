
using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

public sealed class MaintenanceEntry
{
    public int TruckId { get; set; }
    public string Components { get; set; } = "";
    public long Cost { get; set; }
    public int GameDay { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class TrainingEntry
{
    public int DriverId { get; set; }
    public string Skill { get; set; } = "";
    public int Gain { get; set; }
    public long Cost { get; set; }
    public int GameDay { get; set; }
}

public static class OperationsSvc
{
    public static string TrainDriver(int playerId, int driverId, string skill)
    {
        skill = (skill ?? "").Trim().ToUpperInvariant();
        if (skill is not ("SPEED" or "ECO" or "HONESTY"))
            return "Jenis pelatihan harus SPEED, ECO, atau HONESTY.";

        var player = PlayerSvc.Get(playerId);
        using var c = Db.Open();
        using var tx = c.BeginTransaction();

        var driver = c.QueryFirstOrDefault<Driver>(@"SELECT * FROM drivers
            WHERE id=@driverId AND player_id=@playerId AND status='IDLE'",
            new { driverId, playerId }, tx);

        if (driver == null) return "Supir tidak ditemukan atau sedang bertugas.";

        int current = skill switch
        {
            "SPEED" => driver.Speed,
            "ECO" => driver.Eco,
            _ => driver.Honesty
        };

        if (current >= 10) return "Kemampuan sudah mencapai batas 10/10.";

        int gain = 1;
        long cost = 450_000L + current * 250_000L;

        if (!Db.Spend(c, tx, playerId, cost, "PELATIHAN_SUPIR",
            $"Pelatihan {skill} untuk {driver.Name}"))
            return $"Kas tidak cukup. Biaya pelatihan {Rules.Rp(cost)}.";

        string column = skill switch
        {
            "SPEED" => "speed",
            "ECO" => "eco",
            _ => "honesty"
        };

        if (c.Execute($@"UPDATE drivers
            SET {column}=MIN(10,{column}+@gain)
            WHERE id=@driverId AND player_id=@playerId AND status='IDLE'",
            new { gain, driverId, playerId }, tx) != 1)
            return "Status supir berubah. Pelatihan dibatalkan.";

        int day = PlayerSvc.Day(player);

        c.Execute(@"INSERT INTO driver_training_history
            (player_id,driver_id,skill,gain,cost,created_at,game_day)
            VALUES(@playerId,@driverId,@skill,@gain,@cost,@at,@day)",
            new { playerId, driverId, skill, gain, cost, at = SimClock.Now, day }, tx);

        Events.Append(c, tx, EventTypes.DriverTrained,
            new { driver = driver.Name, skill, gain, cost }, playerId);

        tx.Commit();

        return $"Pelatihan {skill} selesai untuk {driver.Name}. " +
               $"Kemampuan {current}/10 → {current + gain}/10. " +
               $"Biaya {Rules.Rp(cost)}.";
    }

    public static List<MaintenanceEntry> MaintenanceHistory(int playerId, int limit = 20)
    {
        using var c = Db.Open();
        return c.Query<MaintenanceEntry>(@"SELECT
            truck_id AS TruckId,
            components AS Components,
            cost AS Cost,
            game_day AS GameDay,
            created_at AS CreatedAt
            FROM maintenance_history
            WHERE player_id=@playerId
            ORDER BY id DESC LIMIT @limit",
            new { playerId, limit = Math.Clamp(limit, 1, 100) }).ToList();
    }

    public static List<TrainingEntry> TrainingHistory(int playerId, int limit = 20)
    {
        using var c = Db.Open();
        return c.Query<TrainingEntry>(@"SELECT
            driver_id AS DriverId,
            skill AS Skill,
            gain AS Gain,
            cost AS Cost,
            game_day AS GameDay
            FROM driver_training_history
            WHERE player_id=@playerId
            ORDER BY id DESC LIMIT @limit",
            new { playerId, limit = Math.Clamp(limit, 1, 100) }).ToList();
    }
}
