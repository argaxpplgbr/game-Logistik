using Dapper;
using Nusantara;
using Nusantara.Database;
using Nusantara.Menus;
using Nusantara.Services;

DefaultTypeMap.MatchNamesWithUnderscores = true;
Console.OutputEncoding = System.Text.Encoding.UTF8;

Db.Init();

// Kalau jendela ditutup / Ctrl+C: jam game disimpan dan slot ditutup rapi.
AppDomain.CurrentDomain.ProcessExit += (_, _) => SaveSlots.Shutdown();

while (true)
{
    int pid;
    try { pid = SlotMenu.Run(); }
    catch (Exception ex)
    {
        Console.WriteLine("Gagal memulai: " + ex.Message);
        break;
    }

    if (pid < 0) break;

    try { GameLoop.Play(pid); }
    finally { SaveSlots.Shutdown(); }
}

Console.WriteLine("Sampai jumpa!");