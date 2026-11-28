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

public enum InventoryReservationStatus
{
    Active = 1,
    ReturnPending = 2,
    Released = 3,
    Committed = 4,
    WrittenOff = 5
}

public enum OrderReservationSummary
{
    Active = 1,
    ReturnPending = 2,
    PartiallyResolved = 3,
    Settled = 4
}

public enum FulfillmentAttemptStatus
{
    Prepared = 1,
    Dispatched = 2,
    Delivered = 3,
    Failed = 4,
    Abandoned = 5,
    Cancelled = 6
}

public enum ReturnDisposition
{
    NotRequired = 0,
    Pending = 1,
    Restockable = 2,
    Damaged = 3,
    Lost = 4
}

public enum DeliveryFailureCategory
{
    Temporary = 1,
    RequiresCorrection = 2,
    NonRecoverable = 3
}

public enum DeliveryFailureReasonCode
{
    CustomerUnavailable = 1,
    NoAnswer = 2,
    RescheduleRequested = 3,
    IncompleteAddress = 4,
    WrongPhoneNumber = 5,
    AccessRestricted = 6,
    CustomerRefused = 7,
    FraudSuspected = 8,
    RestrictedArea = 9,
    DamagedInTransit = 10,
    LostInTransit = 11,
    LegacyUnknown = 99
}
