using System.Text.Json;
using Dapper;
using Nusantara.Database;
using Nusantara.Services;
using Xunit;

namespace Nusantara.Tests;

// Skenario wajib Dokumen 01 no. 3: perekrutan harus menghasilkan event perekrutan, bukan pemecatan.
[Collection("sqlite")]
public class EventTypeTests : IDisposable
{
    readonly string dir;
    readonly Func<long> oldStamp;

    public EventTypeTests()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        Db.Init();
        dir = Path.Combine(Path.GetTempPath(), "nusantara-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", dir);
        oldStamp = SimClock.Stamp;
        long fake = 1_000;
        SimClock.Stamp = () => fake;
    }

    public void Dispose()
    {
        SaveSlots.Shutdown();
        SimClock.Unload();
        SimClock.Stamp = oldStamp;
        Environment.SetEnvironmentVariable("NUSANTARA_SAVE_DIR", null);
        try { Directory.Delete(dir, true); } catch { /* sementara */ }
    }

    [Fact]
    public void Hiring_a_driver_records_DRIVER_HIRED_and_no_firing_news()
    {
        int pid = SaveSlots.Create(1, "PT Rekrut");
        HrSvc.Refresh(pid, charge: false);
        var cand = HrSvc.Candidates(pid).First();

        var result = HrSvc.Hire(pid, cand.Id, 100);
        Assert.Contains("diterima", result);

        using var c = Db.Open();
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM event_ledger WHERE type=@t", new { t = EventTypes.DriverHired }));
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM event_ledger WHERE type=@t", new { t = EventTypes.DriverFired }));
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM news_item WHERE headline LIKE '%diberhentikan%'"));
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM news_item WHERE headline LIKE '%bergabung%'"));
    }

    [Fact]
    public void Every_standard_event_type_is_unique_and_upper_snake_case()
    {
        Assert.Matches("^[A-Z]+(_[A-Z]+)*$", EventTypes.CompanyFounded);
        foreach (var t in EventTypes.All) Assert.Matches("^[A-Z]+(_[A-Z]+)*$", t);
        Assert.DoesNotContain("COMPANY_FOUNDER", EventTypes.All);
    }

    [Fact]
    public void Composer_has_a_template_for_every_standard_type_except_silent_ones()
    {
        var silent = new HashSet<string> { EventTypes.TruckServiced, EventTypes.BidPlaced };
        var empty = JsonDocument.Parse("{}").RootElement.Clone();
        foreach (var t in EventTypes.All)
        {
            var draft = NewsComposer.Compose(t, empty, 1);
            if (silent.Contains(t)) Assert.Null(draft);
            else Assert.True(draft != null, $"Tipe {t} tidak punya template berita.");
        }
    }
}