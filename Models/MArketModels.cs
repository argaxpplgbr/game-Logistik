namespace Nusantara.Models;

// Peluang kontrak di pasar bersama (tabel world_contracts).
public class WorldContract
{
    public int Id { get; set; } public string Kind { get; set; } = ""; public string Scope { get; set; } = "DOMESTIC";
    public int MinTier { get; set; } public string Client { get; set; } = ""; public string Origin { get; set; } = "";
    public string Dest { get; set; } = ""; public string CargoCode { get; set; } = ""; public int WeightKg { get; set; }
    public long Fee { get; set; } public int RepRequired { get; set; } public string VehicleClass { get; set; } = "";
    public double QuoteKm { get; set; } public long QuoteRate { get; set; } public double QuoteMult { get; set; } = 1.0;
    public int MonthsTotal { get; set; } public int TripsPerMonth { get; set; } public string Status { get; set; } = "";
    public int CreatedDay { get; set; } public int BidCloseDay { get; set; } public int ExpireDay { get; set; }
}

// Penawaran satu perusahaan untuk satu kontrak dunia (tabel contract_bids).
public class ContractBid
{
    public int Id { get; set; } public int WorldContractId { get; set; } public int CompanyId { get; set; }
    public long Price { get; set; } public int PlacedDay { get; set; } public string Status { get; set; } = "";
    public double? Score { get; set; } public string Note { get; set; } = "";
}