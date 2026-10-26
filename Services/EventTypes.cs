namespace Nusantara.Services;

// Satu makna = satu nama. Pakai konstanta ini, bukan string literal.
public static class EventTypes
{
    public const string CompanyFounded = "COMPANY_FOUNDED";
    public const string TruckBought = "TRUCK_BOUGHT";
    public const string TruckServiced = "TRUCK_SERVICED";
    public const string BodyInstallStarted = "BODY_INSTALL_STARTED";
    public const string DriverHired = "DRIVER_HIRED";
    public const string DriverFired = "DRIVER_FIRED";
    public const string ContractAccepted = "CONTRACT_ACCEPTED";
    public const string TripDispatched = "TRIP_DISPATCHED";
    public const string TripCompleted = "TRIP_COMPLETED";
    public const string MonthClosed = "MONTH_CLOSED";
    public const string LoanTaken = "LOAN_TAKEN";
    public const string TruckSeized = "TRUCK_SEIZED";
    public const string TierUpgraded = "TIER_UPGRADED";
    public const string WorldEvent = "WORLD_EVENT";
    public const string BidPlaced = "BID_PLACED";
    public const string ContractAwarded = "CONTRACT_AWARDED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        CompanyFounded, TruckBought, TruckServiced, BodyInstallStarted, DriverHired, DriverFired,
        ContractAccepted, TripDispatched, TripCompleted, MonthClosed, LoanTaken, TruckSeized,
        TierUpgraded, WorldEvent
    };
}