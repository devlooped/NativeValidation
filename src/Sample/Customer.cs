using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Devlooped.Sample;

[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "NativeAnnotations inlines CompareAttribute at compile time and reads the other property directly.")]
public class Customer
{
    [Required]
    [StringLength(60, MinimumLength = 2)]
    [Display(Name = "Full name")]
    public string? Name { get; set; }

    [Required]
    [EmailAddress]
    public string? Email { get; set; }

    [Required]
    [Compare(nameof(Email))]
    [Display(Name = "Confirm email")]
    public string? ConfirmEmail { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 8)]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$")]
    public string? Password { get; set; }

    [Required]
    [Compare(nameof(Password))]
    [Display(Name = "Confirm password")]
    public string? ConfirmPassword { get; set; }

    [Phone]
    public string? Phone { get; set; }

    [Url]
    public string? Website { get; set; }

    [Required]
    public Address? Home { get; set; }

    public CustomerPreferences? Preferences { get; set; }

    [Base64String]
    public string? Avatar { get; set; }

    [FileExtensions(Extensions = "png,jpg,jpeg")]
    public string? Photo { get; set; }

    [EnumDataType(typeof(NotificationChannels))]
    public NotificationChannels Channels { get; set; }
}

[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "NativeAnnotations inlines RangeAttribute at compile time and does not run its type-converter constructor.")]
public class CustomerPreferences
{
    [Required]
    [AllowedValues("en", "es", "pt")]
    public string? Language { get; set; }

    [DeniedValues("XXX")]
    [StringLength(3, MinimumLength = 3)]
    public string? Currency { get; set; }

    [Range(typeof(TimeOnly), "08:00", "18:00")]
    public TimeOnly? QuietHoursStart { get; set; }
}

[Flags]
public enum NotificationChannels
{
    None = 0,
    Email = 1,
    Sms = 2,
    Push = 4,
}
