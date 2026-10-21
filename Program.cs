using Dapper;
using Nusantara.Database;
using Nusantara.Menus;
using Nusantara.Services;

DefaultTypeMap.MatchNamesWithUnderscores = true;
Console.OutputEncoding = System.Text.Encoding.UTF8;


Console.Write("Nama perusahaan: ");
var nm = (Console.ReadLine() ?? "").Trim();
if (nm == "") nm = "Pemain";
int pid = PlayerSvc.GetOrCreate(nm).Id;

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
        case "0": return;
    }
}