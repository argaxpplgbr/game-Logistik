using Dapper;
using Nusantara.Database;
using Nusantara.Services;

namespace Nusantara.Menus;

public static class CompanyChatMenu
{
    private sealed class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
    }

    public static void Run(int playerId)
    {
        while (true)
        {
            Ui.Title("Komunikasi Antar Perusahaan");

            Ui.Options(new[]
            {
                "Lihat pesan masuk",
                "Kirim pesan",
                "Lihat hubungan perusahaan",
                "Lihat perjanjian"
            });

            switch (Ui.Ask("Pilih: "))
            {
                case "1":
                    ShowInbox(playerId);
                    break;
                case "2":
                    Send(playerId);
                    break;
                case "3":
                    ShowRelations(playerId);
                    break;
                case "4":
                    ShowAgreements(playerId);
                    break;
                case "0":
                    return;
            }
        }
    }

    private static void ShowInbox(int playerId)
    {
        Ui.Title("Kotak Masuk");

        var messages = AiSocialSvc.Inbox(playerId, 100);

        if (messages.Count == 0)
        {
            Ui.Empty("Belum ada pesan.");
            Ui.Pause();
            return;
        }

        foreach (var m in messages)
        {
            string sender = AiSocialSvc.CompanyName(m.SenderId);

            Console.WriteLine();
            Ui.Line();
            Console.WriteLine($"Dari      : {sender}");
            Console.WriteLine($"Jenis     : {m.MessageType}");
            Console.WriteLine($"Hari      : {m.Day}");
            Console.WriteLine($"Status    : {m.Status}");
            Console.WriteLine($"Pesan     : {m.Body}");

            if (!string.IsNullOrWhiteSpace(m.Reply))
                Console.WriteLine($"Balasan   : {m.Reply}");

            if (m.Status != "PENDING")
                continue;

            Console.WriteLine();
            bool accept = Ui.Yes("Terima pesan/tawaran ini? (y/n): ");
            string reply = Ui.Ask("Balasan: ");

            bool result = AiSocialSvc.Respond(
                playerId,
                m.Id,
                accept,
                reply,
                CompanySvc.WorldDay());

            Console.WriteLine(result
                ? "Pesan berhasil diproses."
                : "Pesan sudah tidak tersedia.");
        }

        Ui.Pause();
    }

    private static void Send(int playerId)
    {
        Ui.Title("Kirim Pesan");

        List<CompanyOption> companies;

        using (var c = Db.Open())
        {
            companies = c.Query<CompanyOption>("""
SELECT
    id AS Id,
    name AS Name,
    kind AS Kind
FROM players
WHERE id<>@playerId
AND bankrupt=0
ORDER BY kind, name
""", new { playerId }).ToList();
        }

        if (companies.Count == 0)
        {
            Ui.Empty("Tidak ada perusahaan lain.");
            Ui.Pause();
            return;
        }

        Console.WriteLine("Daftar perusahaan:");

        for (int i = 0; i < companies.Count; i++)
        {
            var company = companies[i];
            Console.WriteLine($"  {i + 1}. {company.Name} ({company.Kind})");
        }

        int selected = Ui.Num("\nPilih perusahaan: ", -1);

        if (selected < 1 || selected > companies.Count)
            return;

        var target = companies[selected - 1];

        Console.WriteLine("\nJenis pesan:");
        Console.WriteLine("  1. Percakapan biasa");
        Console.WriteLine("  2. Kerja sama bisnis");
        Console.WriteLine("  3. Penawaran aliansi");
        Console.WriteLine("  4. Kerja sama rahasia (simulasi)");
        Console.WriteLine("  5. Tantangan persaingan");
        Console.WriteLine("  6. Tawaran gencatan");
        Console.WriteLine("  7. Ancaman dalam game");

        int type = Ui.Num("\nPilih jenis pesan: ", -1);

        string messageType = type switch
        {
            1 => "CHAT",
            2 => "COOPERATION",
            3 => "ALLIANCE_OFFER",
            4 => "COVERT_OFFER",
            5 => "COMPETITIVE_PROPOSAL",
            6 => "TRUCE_OFFER",
            7 => "THREAT",
            _ => ""
        };

        if (messageType == "")
        {
            Console.WriteLine("Jenis pesan tidak valid.");
            Ui.Pause();
            return;
        }

        string body = Ui.Ask("\nTulis pesan: ");

        if (string.IsNullOrWhiteSpace(body))
        {
            Console.WriteLine("Pesan tidak boleh kosong.");
            Ui.Pause();
            return;
        }

        try
        {
            long id = AiSocialSvc.SendMessage(
                playerId,
                target.Id,
                messageType,
                body,
                CompanySvc.WorldDay());

            Console.WriteLine($"\nPesan berhasil dikirim. ID pesan: {id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("\nGagal mengirim pesan: " + ex.Message);
        }

        Ui.Pause();
    }

    private static void ShowRelations(int playerId)
    {
        Ui.Title("Hubungan Perusahaan");

        var relations = AiSocialSvc.Relationships(playerId);

        if (relations.Count == 0)
        {
            Ui.Empty("Belum ada hubungan perusahaan.");
            Ui.Pause();
            return;
        }

        foreach (var r in relations)
        {
            Console.WriteLine();
            Console.WriteLine($"  {r.OtherCompanyName}");
            Console.WriteLine($"  Status       : {r.RelationStatus}");
            Console.WriteLine($"  Kepercayaan  : {r.Trust}/100");
            Console.WriteLine($"  Penghormatan : {r.Respect}/100");
            Console.WriteLine($"  Keluhan      : {r.Grievance}/100");
            Console.WriteLine($"  Interaksi    : hari {r.LastInteractionDay}");
        }

        Ui.Pause();
    }

    private static void ShowAgreements(int playerId)
    {
        Ui.Title("Perjanjian Perusahaan");

        var agreements = AiSocialSvc.Agreements(playerId);

        if (agreements.Count == 0)
        {
            Ui.Empty("Belum ada perjanjian.");
            Ui.Pause();
            return;
        }

        foreach (var a in agreements)
        {
            int other = a.CompanyA == playerId
                ? a.CompanyB
                : a.CompanyA;

            Console.WriteLine();
            Console.WriteLine($"  Mitra      : {AiSocialSvc.CompanyName(other)}");
            Console.WriteLine($"  Jenis      : {a.AgreementType}");
            Console.WriteLine($"  Status     : {a.Status}");
            Console.WriteLine($"  Dibuat     : hari {a.CreatedDay}");
            Console.WriteLine($"  Berakhir   : hari {a.ExpiresDay}");
            Console.WriteLine($"  Ketentuan  : {a.Terms}");
        }

        Ui.Pause();
    }
}