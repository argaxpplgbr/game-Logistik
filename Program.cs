using Dapper;
using Nusantara.Database;
using Nusantara.Menus;
using Nusantara.Services;

DefaultTypeMap.MatchNamesWithUnderscores = true;
Console.OutputEncoding = System.Text.Encoding.UTF8;

try { Db.EnsureMigrations(); }
catch (Exception ex)
{
    Console.WriteLine("gagal terhubung ke database: " + ex.Message);
    Console.WriteLine("Cek variabel NUSANTARA_DB_CONN (atau placeholder di database) dan pastikan MySql menyala");
    return;
}

Console.Write("Nama perusahaan: ");
var nm = (Console.ReadLine() ?? "").Trim();
if (nm == "") nm = "Pemain";
int pid = PlayerSvc.GetOrCreate(nm).Id;
LedgerSvc.EnsureOpening(pid);   // saldo awal buku kas (sekali saja)

while (true)
{
    var msgs = WorldSvc.Tick(pid);
    var p = PlayerSvc.Get(pid);

    MainMenu.ShowEvents(msgs);

    if (p.Bankrupt)
    {
        Console.WriteLine("\n*** GAME OVER: perusahaan BANGKRUT. ***");
        return;
    }

    MainMenu.ShowDashboard(pid, p);

    switch (Ui.Ask("Pilih: "))
    {
        case "1": FleetMenu.Run(pid); break;
        case "2": DealerMenu.Run(pid); break;
        case "3": ServiceMenu.Run(pid); break;
        case "4": DriverMenu.Run(pid); break;
        case "5": ContractMenu.Run(pid); break;
        case "6": TripMenu.Run(pid); break;
        case "7": NewsMenu.Run(pid); break;
        case "8": BankMenu.Run(pid); break;
        case "9": FacilityMenu.Run(pid); break;
        case "10": ShopMenu.Run(pid); break;
        case "11": LedgerMenu.Run(pid); break;
        case "12": FinanceMenu.Run(pid); break;
        case "13": TutorialMenu.Run(pid); break;
        case "0": return;
    }
}