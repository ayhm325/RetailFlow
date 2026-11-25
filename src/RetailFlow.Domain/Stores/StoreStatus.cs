namespace RetailFlow.Domain.Stores;

public enum StoreStatus : short
{
    PendingApproval = 1,
    Active = 2,
    Suspended = 3,
    Rejected = 4
}
