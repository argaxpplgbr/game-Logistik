
using Nusantara.Services;

namespace Nusantara.Menus;

public static class CompetitorMenu
{
    public static void Run(int playerId)
    {
        while (true)
        {
            var companies = CompetitorIntelSvc.List(playerId);

            Ui.Title("Pusat Intelijen Kompetitor");

            if (companies.Count == 0)
            {
                Ui.Empty("Belum ada kompetitor AI.");
                Ui.Pause();
                return;
            }

            Console.WriteLine(
                "  PERUSAHAAN                    TIER       REP   TRUK   HUBUNGAN");

            for (int i = 0; i < companies.Count; i++)
            {
                var x = companies[i];

                Console.WriteLine(
                    $"  {i + 1,2}. {x.Name,-28} " +
                    $"{MarketConfig.TierName(x.Tier),-9} " +
                    $"{x.Reputation,4} " +
                    $"{x.Trucks,5}   " +
                    $"{RelationName(x.RelationStatus)}");
            }

            Console.WriteLine();
            Console.WriteLine("  0. Kembali");

            int pick = Ui.Num("Pilih perusahaan untuk melihat detail: ", 0);

            if (pick == 0)
                return;

            if (pick < 1 || pick > companies.Count)
            {
                Console.WriteLine("Pilihan tidak tersedia.");
                Ui.Pause();
                continue;
            }

            ShowDetails(playerId, companies[pick - 1]);
        }
    }

    private static void ShowDetails(
        int playerId,
        CompetitorOverview company)
    {
        Ui.Title("Profil Kompetitor");

        Console.WriteLine($"  Nama          : {company.Name}");
        Console.WriteLine($"  Kepribadian   : {company.Personality}");
        Console.WriteLine($"  Karakter      : {CompetitorIntelSvc.PersonalityDescription(company.Personality)}");
        Console.WriteLine($"  Status        : {(company.Bankrupt ? "BANGKRUT" : "AKTIF")}");
        Console.WriteLine($"  Tier          : {MarketConfig.TierName(company.Tier)}");
        Console.WriteLine($"  Reputasi      : {company.Reputation}");
        Console.WriteLine($"  Kondisi kas   : {CompetitorIntelSvc.CashBand(company.Cash)}");
        Console.WriteLine($"  Armada        : {company.Trucks} truk");
        Console.WriteLine($"  Supir         : {company.Drivers}");
        Console.WriteLine($"  Trip berjalan : {company.ActiveTrips}");
        Console.WriteLine($"  Kontrak aktif : {company.ActiveContracts}");

        Ui.Section("Hubungan dengan perusahaan kamu");

        Console.WriteLine($"  Status       : {RelationName(company.RelationStatus)}");
        Console.WriteLine($"  Kepercayaan  : {company.Trust}/100");
        Console.WriteLine($"  Penghormatan : {company.Respect}/100");
        Console.WriteLine($"  Keluhan      : {company.Grievance}/100");
        Console.WriteLine($"  Interaksi terakhir: hari {company.LastInteractionDay}");

        Ui.Section("Perjanjian aktif");

        var agreements = AiSocialSvc.Agreements(playerId)
            .Where(x =>
                (x.CompanyA == playerId && x.CompanyB == company.Id) ||
                (x.CompanyB == playerId && x.CompanyA == company.Id))
            .Where(x => x.Status == "ACTIVE")
            .ToList();

        if (agreements.Count == 0)
        {
            Ui.Empty("Belum ada perjanjian aktif.");
        }
        else
        {
            foreach (var a in agreements)
            {
                Console.WriteLine(
                    $"  {a.AgreementType} | berlaku sampai hari {a.ExpiresDay}");
            }
        }

        Ui.Section("Aktivitas keputusan AI");

        var activities = CompetitorIntelSvc.RecentActivity(company.Id, 8);

        if (activities.Count == 0)
        {
            Ui.Empty("Belum ada catatan keputusan.");
        }
        else
        {
            foreach (var a in activities)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"  Hari {a.Day} | {a.Action} | {a.Status}");

                Console.WriteLine("  " + a.Reason);

                if (!string.IsNullOrWhiteSpace(a.Detail))
                    Console.WriteLine("  Detail: " + a.Detail);
            }
        }

        Ui.Pause();
    }

    private static string RelationName(string status)
    {
        return status switch
        {
            "ALLY" => "SEKUTU",
            "HOSTILE" => "MUSUH",
            "NEGOTIATING" => "NEGOSIASI",
            "TRUCE" => "Gencatan",
            _ => "NETRAL"
        };
    }
}
