using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Devlooped.Sample;

[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "NativeAnnotations inlines length and range attributes at compile time and does not run their reflection-based constructors.")]
public class Order
{
    [Required]
    [StringLength(16, MinimumLength = 6)]
    [Display(Name = "Order number")]
    public string? Number { get; set; }

    [Required]
    public Customer? Buyer { get; set; }

    [Required]
    public Address? BillingAddress { get; set; }

    public bool ShipToBillingAddress { get; set; }

    [CustomValidation(typeof(OrderChecks), nameof(OrderChecks.Shipping))]
    public Address? ShippingAddress { get; set; }

    [Required]
    public PaymentCard? Payment { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(50)]
    public List<OrderLine>? Lines { get; set; }

    [Length(0, 500)]
    public string? Notes { get; set; }

    [Range(typeof(DateOnly), "2020-01-01", "2035-12-31")]
    public DateOnly PlacedOn { get; set; }

    [DeniedValues("EXPIRED", "INTERNAL")]
    [StringLength(16)]
    public string? Coupon { get; set; }

    [EnumDataType(typeof(OrderStatus))]
    public OrderStatus Status { get; set; }

    [Range(0, 10_000)]
    public int Shipping { get; set; }
}

public class OrderLine
{
    [Required]
    public Product? Item { get; set; }

    [Range(1, 999)]
    public int Quantity { get; set; }

    [Range(0.01, 100_000d)]
    public double UnitPrice { get; set; }

    [StringLength(40)]
    public string? GiftNote { get; set; }
}

public enum OrderStatus
{
    Draft,
    Placed,
    Paid,
    Shipped,
    Cancelled,
}

public static class OrderChecks
{
    public static ValidationResult Shipping(Address? address, ValidationContext context)
    {
        var order = (Order)context.ObjectInstance!;
        if (order.ShipToBillingAddress)
            return ValidationResult.Success!;

        if (address is { Line1.Length: > 0 })
            return ValidationResult.Success!;

        return new ValidationResult("A shipping address is required unless it matches billing.", [nameof(Order.ShippingAddress)]);
    }
}
