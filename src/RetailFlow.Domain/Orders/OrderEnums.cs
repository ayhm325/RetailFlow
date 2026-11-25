namespace RetailFlow.Domain.Orders;

public enum OrderStatus
{
    Pending = 1,
    Confirmed = 2,
    Completed = 3,
    Rejected = 4,
    Cancelled = 5
}

public enum FulfillmentMode
{
    Delivery = 1,
    Pickup = 2
}

public enum FulfillmentStatus
{
    Unfulfilled = 1,
    Preparing = 2,
    OutForDelivery = 3,
    ReadyForPickup = 4,
    Delivered = 5,
    PickedUp = 6,
    Failed = 7,
    Cancelled = 8
}

public enum PaymentStatus
{
    Pending = 1,
    Collected = 2,
    Voided = 3
}

public enum CheckoutRequestStatus
{
    InProgress = 1,
    Completed = 2,
    Failed = 3
}
