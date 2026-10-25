using System.Diagnostics;
using System.Globalization;

namespace Nusantara.Services;

// Jam simulasi. Maju HANYA selama game terbuka dan tidak dijeda.
// Tidak memakai jam dinding, jadi tidak ada catch-up saat game ditutup lalu dibuka lagi.
// Satuan waktu sama dengan sistem lama (1 hari game = Rules.DayMinutes menit, sudah memperhitungkan DevSpeed),
// sehingga seluruh balance yang sudah ada tetap berlaku. Speed hanyalah pengali tambahan (1x/2x/5x).
public static class SimClock
{
    public static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    public static readonly double[] SpeedSteps = { 1, 2, 5 };

    const string Fmt = "yyyy-MM-dd HH:mm:ss.fff";
    static readonly object gate = new();

    // Sumber waktu monotonic. Bisa diganti di unit test supaya deterministik.
    public static Func<long> Stamp = Stopwatch.GetTimestamp;

    static double baseMinutes;
    static long startStamp;
    static bool loaded;
    static bool paused;
    static double speed = 1;

    public static bool Loaded { get { lock (gate) return loaded; } }
    public static bool Paused { get { lock (gate) return paused; } }
    public static double Speed { get { lock (gate) return speed; } }
    public static double Minutes { get { lock (gate) return Calc(); } }
    public static DateTime Now => Epoch.AddMinutes(Minutes);

    static double Calc()
    {
        if (!loaded || paused) return baseMinutes;
        return baseMinutes + Stopwatch.GetElapsedTime(startStamp, Stamp()).TotalMinutes * speed;
    }

    static void Fold()
    {
        baseMinutes = Calc();
        startStamp = Stamp();
    }

    public static void Load(double minutes, double spd)
    {
        lock (gate)
        {
            baseMinutes = Math.Max(0, minutes);
            speed = spd > 0 ? spd : 1;
            paused = false;
            startStamp = Stamp();
            loaded = true;
        }
    }

    public static void Unload()
    {
        lock (gate)
        {
            if (!loaded) return;
            Fold();
            loaded = false;
        }
    }

    public static void Pause()
    {
        lock (gate)
        {
            if (!loaded || paused) return;
            Fold();
            paused = true;
        }
    }

    public static void Resume()
    {
        lock (gate)
        {
            if (!loaded || !paused) return;
            startStamp = Stamp();
            paused = false;
        }
    }

    public static void SetSpeed(double s)
    {
        lock (gate)
        {
            if (s <= 0) return;
            Fold();
            speed = s;
        }
    }

    // Pindah ke kecepatan berikutnya: 1x -> 2x -> 5x -> 1x
    public static double NextSpeed()
    {
        double cur = Speed;
        int i = Array.FindIndex(SpeedSteps, x => Math.Abs(x - cur) < 1e-9);
        double next = SpeedSteps[(i + 1) % SpeedSteps.Length];
        SetSpeed(next);
        return next;
    }

    // ---- Konversi ke/dari teks database ----
    public static string ToDb(DateTime d) => d.ToString(Fmt, CultureInfo.InvariantCulture);
    public static DateTime FromDb(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None);

    // ---- Tampilan hari & jam ----
    public static double DaysSince(DateTime createdAt, double simMinutes)
    {
        double d = (Epoch.AddMinutes(simMinutes) - createdAt).TotalMinutes / Rules.DayMinutes;
        return d < 0 ? 0 : d;
    }

    public static string Describe(DateTime createdAt, double simMinutes)
    {
        double days = DaysSince(createdAt, simMinutes);
        int day = (int)days;
        int minuteOfDay = (int)((days - day) * 1440);
        return $"Hari {day} • {minuteOfDay / 60:00}:{minuteOfDay % 60:00}";
    }

    public static string Describe(DateTime createdAt) => Describe(createdAt, Minutes);
}