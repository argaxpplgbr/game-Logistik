namespace Nusantara.Models;

public class Player
{
    public int Id { get; set; } public string Name { get; set; } = ""; public DateTime CreatedAt { get; set; }
    public long Cash { get; set; } public int RepDelivery { get; set; } public int RepBrand { get; set; }
    public int DeliveriesOk { get; set; } public int Tier { get; set; } public string PoolType { get; set; } = "NONE";
    public bool Depot { get; set; } public bool Warehouse { get; set; } public bool Manager { get; set; } public bool B3Cert { get; set; }
    public int Mechanics { get; set; } public int LastDay { get; set; } public int NextMonthDay { get; set; }
    public long Arrears { get; set; } public double PremiumPenalty { get; set; } = 1.0;
    public bool Bankrupt { get; set; } public bool AdDiscount { get; set; }
    public int MgrServiceTh { get; set; } public double MgrUjMult { get; set; } public double MgrMinMargin { get; set; }
    public int SocialLastDay { get; set; } public string PoolSkin { get; set; } = "";
    public int Reputation => RepDelivery + RepBrand;
    public double Stars => Math.Min(5, Reputation / 100.0);
    public string Legal => Tier == 1 ? "Perorangan" : Tier == 2 ? "CV" : "PT";
}

public class TruckModel
{
    public int Id { get; set; } public string Name { get; set; } = ""; public int Tier { get; set; } public long Price { get; set; }
    public double KmPerL { get; set; } public int MaxLoad { get; set; } public int Axles { get; set; }
    public string Sim { get; set; } = ""; public string FuelReq { get; set; } = ""; public string? FactoryBody { get; set; }
    public bool IsEuro { get; set; }
}

public class BodyType { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public long Price { get; set; } public int Days { get; set; } public int WeightKg { get; set; } }

public class CargoType
{
    public string Code { get; set; } = ""; public string Name { get; set; } = ""; public int MinTier { get; set; }
    public string Bodies { get; set; } = ""; public double Rate { get; set; } public int MinKg { get; set; } public int MaxKg { get; set; }
    public bool B3 { get; set; }
}

public class Truck
{
    public int Id { get; set; } public int PlayerId { get; set; } public int CatalogId { get; set; }
    public string? BodyCode { get; set; } public string Status { get; set; } = ""; public DateTime? BodyReadyAt { get; set; }
    public int OilPct { get; set; } public int BrakePct { get; set; } public int TirePct { get; set; }
    public int FilterPct { get; set; } public int EnginePct { get; set; }
    public string FuelPref { get; set; } = "BIO"; public string Insurance { get; set; } = "NONE"; public bool CargoIns { get; set; }
    public long PurchasePrice { get; set; } public int BoughtDay { get; set; } public int? DriverId { get; set; } public bool IsUsed { get; set; }
    public string Livery { get; set; } = ""; public string Horn { get; set; } = ""; public string Led { get; set; } = ""; public string Sticker { get; set; } = "";
    // hasil join
    public string ModelName { get; set; } = ""; public int Tier { get; set; } public double KmPerL { get; set; }
    public int MaxLoad { get; set; } public int Axles { get; set; } public string Sim { get; set; } = "";
    public string FuelReq { get; set; } = ""; public bool IsEuro { get; set; } public int BodyWeight { get; set; }
    public int Payload => MaxLoad - BodyWeight;
}

public class UsedListing
{
    public int Id { get; set; } public int PlayerId { get; set; } public int CatalogId { get; set; } public string BodyCode { get; set; } = "";
    public long Price { get; set; } public int AgeDays { get; set; } public int OilPct { get; set; } public int BrakePct { get; set; }
    public int TirePct { get; set; } public int FilterPct { get; set; } public int EnginePct { get; set; }
    public string ModelName { get; set; } = ""; public int Tier { get; set; } public string FuelReq { get; set; } = "";
}

public class Driver
{
    public int Id { get; set; } public int PlayerId { get; set; } public string Name { get; set; } = ""; public string Sim { get; set; } = "";
    public int Speed { get; set; } public int Eco { get; set; } public int Honesty { get; set; } public int HonestyClaim { get; set; }
    public int ExpYears { get; set; } public int CrashCount { get; set; } public long SalaryExp { get; set; } public long Salary { get; set; }
    public double UjExp { get; set; } public double CommissionPct { get; set; } public int Morale { get; set; }
    public string Status { get; set; } = ""; public bool Interviewed { get; set; }
}

public class Contract
{
    public int Id { get; set; } public int PlayerId { get; set; } public string Kind { get; set; } = ""; public string Client { get; set; } = "";
    public string Origin { get; set; } = ""; public string Dest { get; set; } = ""; public string CargoCode { get; set; } = "";
    public int WeightKg { get; set; } public long Fee { get; set; } public int RepRequired { get; set; } public string Status { get; set; } = "";
    public int CreatedDay { get; set; } public int ExpireDay { get; set; } public int MonthsTotal { get; set; } public int MonthsDone { get; set; }
    public int TripsPerMonth { get; set; } public int TripsDoneMonth { get; set; } public long MonthlyPay { get; set; }
}

public class Trip
{
    public int Id { get; set; } public int PlayerId { get; set; } public int TruckId { get; set; } public int DriverId { get; set; }
    public int ContractId { get; set; } public string Path { get; set; } = ""; public double KmRoad { get; set; } public double KmFerry { get; set; }
    public int Steep { get; set; } public long UjGiven { get; set; } public long CostEst { get; set; } public long FuelCost { get; set; }
    public long Fee { get; set; } public int OverloadPct { get; set; } public string OdolPolicy { get; set; } = ""; public string Security { get; set; } = "";
    public string FuelMode { get; set; } = ""; public string FuelType { get; set; } = ""; public double UjMult { get; set; }
    public DateTime StartedAt { get; set; } public DateTime ArriveAt { get; set; } public string Status { get; set; } = "";
    public string Notes { get; set; } = ""; public string Result { get; set; } = "";
}

public class Node { public int Id { get; set; } public string Name { get; set; } = ""; public string Island { get; set; } = ""; }

public class Edge
{
    public int Id { get; set; } public int A { get; set; } public int B { get; set; } public string Name { get; set; } = "";
    public int Km { get; set; } public string Kind { get; set; } = ""; public bool Steep { get; set; } public int Rawan { get; set; }
    public bool Toll { get; set; } public bool Weigh { get; set; } public bool Pantura { get; set; } public long FerryFee { get; set; }
}

public class WorldEvent
{
    public int Id { get; set; } public int PlayerId { get; set; } public string Code { get; set; } = ""; public string Title { get; set; } = "";
    public int StartDay { get; set; } public int EndDay { get; set; } public double Magnitude { get; set; } public string Cargos { get; set; } = "";
}

public class Loan
{
    public int Id { get; set; } public int PlayerId { get; set; } public long Principal { get; set; } public long Remaining { get; set; }
    public long Installment { get; set; } public int MonthsLeft { get; set; } public int Missed { get; set; }
}

public class Cosmetic { public int Id { get; set; } public string Kind { get; set; } = ""; public string Name { get; set; } = ""; public int PriceIdr { get; set; } public long Owned { get; set; } }

public class Plan
{
    public string? Error;
    public Truck Truck = null!; public Driver Driver = null!; public Contract Contract = null!;
    public List<Edge> Path = new(); public List<string> Notes = new();
    public double RoadKm, FerryKm, Liters, Minutes; public int Steep, OverloadPct;
    public long FuelCost, Toll, Ferry, Meal, Reserve, Escort, Security, CostEst, UjStd, UjGiven;
    public string Policy = "DENDA", Sec = "NONE", FuelMode = "NORMAL", FuelType = "BIO"; public double UjMult = 1.0;
}