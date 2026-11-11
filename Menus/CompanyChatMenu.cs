
using Dapper;
using Nusantara.Database;
using Nusantara.Services;

namespace Nusantara.Menus;

public static class CompanyChatMenu
{
    private sealed record CompanyOption(int Id, string Name, string Kind);

    public static void Run(int playerId)
    {
        while (true)
        {
            Ui.Title("Komunikasi Antar Perusahaan");

            Console.WriteLine("  1. Lihat pesan masuk");
            Console.WriteLine("  2. Kirim pesan");
            Console.WriteLine("  0. Kembali");

            string choice = Ui.Ask("\nPilih: ");

            switch (choice)
            {
                case "1":
                    ShowInbox(playerId);
                    break;

                case "2":
                    Send(playerId);
                    break;

                case "0":
                    return;

                default:
                    Console.WriteLine("Pilihan tidak tersedia.");
                    break;
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

        using var c = Db.Open();

        foreach (var m in messages)
        {
            string sender = c.ExecuteScalar<string>(
                "SELECT name FROM players WHERE id=@id",
                new { id = m.SenderId }) ?? "Perusahaan tidak dikenal";

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

            if (string.IsNullOrWhiteSpace(reply))
            {
                reply = accept
                    ? "Kami menerima pesan Anda."
                    : "Kami belum dapat menerima tawaran tersebut.";
            }

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

            Console.WriteLine(
                $"  {i + 1}. {company.Name} ({company.Kind})");
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
        Console.WriteLine("  6. Ancaman dalam game");

        int type = Ui.Num("\nPilih jenis pesan: ", -1);

        string messageType = type switch
        {
            1 => "CHAT",
            2 => "COOPERATION",
            3 => "ALLIANCE_OFFER",
            4 => "COVERT_OFFER",
            5 => "COMPETITIVE_PROPOSAL",
            6 => "THREAT",
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

            Console.WriteLine(
                $"\nPesan berhasil dikirim. ID pesan: {id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("\nGagal mengirim pesan: " + ex.Message);
        }

        Ui.Pause();
    }
}
