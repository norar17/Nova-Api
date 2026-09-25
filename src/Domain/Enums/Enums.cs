namespace ECommerce.Domain.Enums;

public enum OrderStatus
{
    Pending = 0,
    PaymentProcessing = 1,
    Paid = 2,
    Shipped = 3,
    OutForDelivery = 4,
    Delivered = 5,
    Cancelled = 6,
    Refunded = 7,
    Failed = 8
}

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Refunded = 3,
    PartiallyRefunded = 4
}

public enum PaymentProvider
{
    Stripe = 0
}

public enum DiscountType
{
    Percentage = 0,
    FixedAmount = 1
}

public enum CouponStatus
{
    Active = 0,
    Expired = 1,
    Disabled = 2
}

public enum AddressType
{
    Shipping = 0,
    Billing = 1
}

public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    Restored = 3,
    LoggedIn = 4,
    LoggedOut = 5,
    PasswordChanged = 6,
    RoleChanged = 7
}

public enum NotificationType
{
    OrderPlaced = 0,
    OrderShipped = 1,
    OrderDelivered = 2,
    PaymentSucceeded = 3,
    PaymentFailed = 4,
    PriceDrop = 5,
    BackInStock = 6,
    System = 7
}

public enum StoreStatus
{
    Pending = 0,
    Approved = 1,
    Suspended = 2,
    Rejected = 3
}
