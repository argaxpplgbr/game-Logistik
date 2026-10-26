using System.Collections.Concurrent;

namespace Nusantara.Services;


public static class SimTicker
{
    public static readonly object Gate = new();
    static readonly ConcurrentQueue<string> messages = new();
    static Timer? timer;
    static int running;          // 0 = idle, 1 = tick sedang berjalan
    static int pid;

    public static bool IsStarted => timer != null;
    public static string? LastError { get; private set; }

    // Hook untuk pengujian: menggantikan WorldSvc.Tick.
    public static Func<int, List<string>> TickAction = WorldSvc.Tick;

    public static void Start(int playerId, TimeSpan? interval = null)
    {
        Stop();
        pid = playerId;
        var every = interval ?? TimeSpan.FromSeconds(1);
        timer = new Timer(_ => TickOnce(), null, every, every);
    }

    public static void Stop()
    {
        var t = Interlocked.Exchange(ref timer, null);
        t?.Dispose();
        // Tunggu tick yang sedang jalan selesai sebelum database dilepas.
        var spin = new SpinWait();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref running) == 1 && DateTime.UtcNow < deadline) spin.SpinOnce();
    }

    public static void TickOnce()
    {
        if (!SimClock.Loaded || SimClock.Paused) return;
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return;   // tick sebelumnya belum selesai
        try
        {
            lock (Gate)
            {
                foreach (var m in TickAction(pid)) messages.Enqueue(m);
            }
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            messages.Enqueue("Kesalahan saat memproses waktu game: " + ex.Message);
        }
        finally { Volatile.Write(ref running, 0); }
    }

    public static List<string> Drain()
    {
        var list = new List<string>();
        while (messages.TryDequeue(out var m)) list.Add(m);
        return list;
    }
}