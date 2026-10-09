using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Devlooped.Sample;

[MetadataType(typeof(ProductMetadata))]
public class Product
{
    public string? Sku { get; set; }

    public string? Name { get; set; }

    [Range(0, 1_000_000)]
    public int Stock { get; set; }

    [Url]
    public string? ImageUrl { get; set; }
}

public class ProductMetadata
{
    [Required]
    [RegularExpression("^[A-Z0-9-]{3,32}$")]
    [Display(Name = "SKU")]
    public string? Sku { get; set; }

    [Required]
    [StringLength(80, MinimumLength = 2)]
    public string? Name { get; set; }
}

public class Address
{
    [Required]
    [StringLength(80, MinimumLength = 3)]
    public string? Line1 { get; set; }

    [StringLength(80)]
    public string? Line2 { get; set; }

    [Required]
    [StringLength(40)]
    public string? City { get; set; }

    [Required]
    [StringLength(40)]
    public string? Region { get; set; }

    [Required]
    [RegularExpression(@"^[A-Z0-9][A-Z0-9 \-]{2,11}$")]
    [Display(Name = "Postal code")]
    public string? PostalCode { get; set; }

    [Required]
    [AllowedValues("US", "CA", "MX", "BR", "AR")]
    public string? Country { get; set; }
}

[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "NativeAnnotations inlines RangeAttribute at compile time and does not run its type-converter constructor.")]
public class PaymentCard
{
    [Required]
    [StringLength(40, MinimumLength = 2)]
    [Display(Name = "Name on card")]
    public string? Cardholder { get; set; }

    [Required]
    [CreditCard]
    [Display(Name = "Card number")]
    public string? Number { get; set; }

    [Required]
    [Range(typeof(DateOnly), "2024-01-01", "2040-12-31")]
    public DateOnly Expires { get; set; }

    [Required]
    [RegularExpression(@"^[0-9]{3,4}$")]
    public string? Cvv { get; set; }
}
