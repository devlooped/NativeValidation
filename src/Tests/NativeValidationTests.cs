using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Devlooped.Tests
{
    public class NativeValidationTests
    {
        [Fact]
        public void RejectsAnInvalidProperty()
        {
            var account = new Account();
            var results = new List<ValidationResult>();

            Assert.False(NativeValidator.TryValidateProperty("   ", Context(account, nameof(Account.Email)), results));

            var error = Assert.Single(results);
            Assert.Contains("Email address", error.ErrorMessage, StringComparison.Ordinal);
            Assert.Equal("Email", Assert.Single(error.MemberNames));
            var thrown = Assert.Throws<ValidationException>(() => NativeValidator.ValidateProperty("   ", Context(account, nameof(Account.Email))));
            Assert.Equal("Email", Assert.Single(thrown.ValidationResult.MemberNames));
        }

        [Fact]
        public void RequiredSuppressesLaterAttributes()
        {
            var account = new Account();
            var results = new List<ValidationResult>();

            Assert.False(NativeValidator.TryValidateProperty("   ", Context(account, nameof(Account.Email)), results));

            Assert.Single(results);
            Assert.Contains("required", results[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AcceptsAValidPropertyAndOptionalNull()
        {
            var account = new Account();

            Assert.True(NativeValidator.TryValidateProperty("ada@example.com", Context(account, nameof(Account.Email)), null));
            Assert.True(NativeValidator.TryValidateProperty(null, Context(account, nameof(Account.Code)), null));
        }

        [Fact]
        public void WrongPropertyTypeThrows()
        {
            var account = new Account();

            Assert.Throws<ArgumentException>(() => NativeValidator.TryValidateProperty(1, Context(account, nameof(Account.Email)), null));
        }

        [Fact]
        public void RequiredOnlySkipsOtherPropertyRules()
        {
            var account = new Account { Email = "not-an-email", Code = "12345" };
            var results = new List<ValidationResult>();

            Assert.True(NativeValidator.TryValidateObject(account, new ValidationContext(account), results));
            Assert.Empty(results);

            Assert.False(NativeValidator.TryValidateObject(account, new ValidationContext(account), results, validateAllProperties: true));
            Assert.Contains(results, result => result.ErrorMessage!.Contains("e-mail", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(results, result => result.ErrorMessage!.Contains("maximum length", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CompareReadsTheOtherProperty()
        {
            var account = new Account { Email = "ada@example.com", Password = "secret", Confirm = "other" };
            var results = new List<ValidationResult>();

            Assert.False(NativeValidator.TryValidateObject(account, new ValidationContext(account), results, validateAllProperties: true));
            Assert.Contains(results, result => result.MemberNames.Contains("Confirm"));

            account.Confirm = "secret";
            results.Clear();
            Assert.True(NativeValidator.TryValidateObject(account, new ValidationContext(account), results, validateAllProperties: true));
        }

        [Fact]
        public void RejectsMethodArguments()
        {
            var account = new Account();
            var rename = typeof(Account).GetMethod(nameof(Account.Rename))!;
            var pay = typeof(Account).GetMethod(nameof(Account.Pay))!;
            var label = typeof(Account).GetMethod(nameof(Account.Label))!;
            var results = new List<ValidationResult>();

            Assert.False(NativeValidator.TryValidate(account, rename, results, new object?[] { null, 1 }));
            Assert.False(NativeValidator.TryValidate(account, rename, null, "a", 1));
            Assert.False(NativeValidator.TryValidate(account, rename, null, "ada", 11));
            Assert.True(NativeValidator.TryValidate(account, rename, null, "ada", 2));
            Assert.True(NativeValidator.TryValidate(account, pay, null, "4111111111111111"));
            Assert.False(NativeValidator.TryValidate(account, label, null, " "));
            Assert.True(NativeValidator.TryValidate(account, label, null, "ada"));
            Assert.Throws<ValidationException>(() => NativeValidator.Validate(account, pay, "4111111111111112"));
        }

        [Fact]
        public void ClosedGenericUsesTheSubstitutedSignature()
        {
            var box = new Box<string>();

            Assert.False(NativeValidator.TryValidateObject(box, new ValidationContext(box), null));
            box.Value = "kept";
            Assert.True(NativeValidator.TryValidateObject(box, new ValidationContext(box), null));
        }

        [Fact]
        public void PropertyOnAClassIsChecked()
        {
            var profile = new Profile();
            var setter = Setter<Profile>(nameof(Profile.Name));

            Assert.False(NativeValidator.TryValidateProperty("", Context(profile, nameof(Profile.Name)), null));
            Assert.True(NativeValidator.TryValidateProperty("Ada", Context(profile, nameof(Profile.Name)), null));
            Assert.True(NativeValidation.AppliesTo(setter));
        }

        [Fact]
        public void InterfaceAttributesApplyToTheImplementation()
        {
            var named = new Named();
            var results = new List<ValidationResult>();

            Assert.False(NativeValidator.TryValidateProperty(null, Context(named, nameof(Named.Name)), results));
            Assert.Contains("required", Assert.Single(results).ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True(NativeValidator.TryValidateProperty("Ada", Context(named, nameof(Named.Name)), null));
        }

        [Fact]
        public void ValidatableObjectIsSkippedWhenAPropertyFails()
        {
            var noted = new Noted();
            var results = new List<ValidationResult>();

            Assert.False(NativeValidator.TryValidateObject(noted, new ValidationContext(noted), results));
            Assert.False(noted.Validated);
            Assert.Contains("required", Assert.Single(results).ErrorMessage, StringComparison.OrdinalIgnoreCase);

            noted.Name = "Ada";
            results.Clear();
            Assert.False(NativeValidator.TryValidateObject(noted, new ValidationContext(noted), results));
            Assert.True(noted.Validated);
            Assert.Contains("object", Assert.Single(results).ErrorMessage, StringComparison.Ordinal);
        }

        [Fact]
        public void TypeRulesRunWhenPropertiesPass()
        {
            var thing = new Thing();
            var results = new List<ValidationResult>();

            Assert.False(NativeValidator.TryValidateObject(thing, new ValidationContext(thing), results));
            Assert.Contains("blank", Assert.Single(results).ErrorMessage, StringComparison.Ordinal);

            thing.Name = "Ada";
            results.Clear();
            Assert.True(NativeValidator.TryValidateObject(thing, new ValidationContext(thing), results));
        }

        [Fact]
        public void ValidatedFactoryClosesItsReturnType()
        {
            var created = Labels.Make<string>();
            var labeled = Assert.IsType<Labeled<string>>(created);

            Assert.False(NativeValidator.TryValidateObject(labeled, new ValidationContext(labeled), null));
            labeled.Label = "kept";
            Assert.True(NativeValidator.TryValidateObject(labeled, new ValidationContext(labeled), null));
        }

        static ValidationContext Context(object instance, string member) =>
            new ValidationContext(instance) { MemberName = member };

        static MethodInfo Setter<T>(string property) => typeof(T).GetProperty(property)!.SetMethod!;
    }

    public class Account
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string? Email { get; set; }

        [MaxLength(4)]
        public string? Code { get; set; }

        [Compare(nameof(Password))]
        public string? Confirm { get; set; }

        public string? Password { get; set; }

        public void Rename([Required, StringLength(8, MinimumLength = 2)] string? name, [Range(1, 10)] int attempt)
        {
        }

        public void Pay([CreditCard] string? card)
        {
        }

        public void Label([CustomValidation(typeof(AccountChecks), nameof(AccountChecks.RejectBlank))] string? label)
        {
        }
    }

    public static class AccountChecks
    {
        public static ValidationResult RejectBlank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? new ValidationResult("blank") : ValidationResult.Success!;
    }

    public class Profile
    {
        [Required]
        public string? Name { get; set; }
    }

    public class Noted : IValidatableObject
    {
        public bool Validated { get; private set; }

        [Required]
        public string? Name { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            Validated = true;
            yield return new ValidationResult("object");
        }
    }

    [CustomValidation(typeof(ThingChecks), nameof(ThingChecks.Reject))]
    public class Thing
    {
        public string? Name { get; set; }
    }

    public static class ThingChecks
    {
        public static ValidationResult Reject(Thing value) =>
            string.IsNullOrEmpty(value.Name) ? new ValidationResult("blank", new[] { nameof(Thing.Name) }) : ValidationResult.Success!;
    }

    public interface INamed
    {
        [Required]
        [Display(Name = "Display name")]
        string? Name { get; set; }
    }

    public class Named : INamed
    {
        public string? Name { get; set; }
    }

    public class Box<T>
    {
        [Required]
        public T? Value { get; set; }
    }

    public class Labeled<T>
    {
        [Required]
        public T? Label { get; set; }
    }

    public static class Labels
    {
        [Validated]
        public static Labeled<T> Create<T>() where T : class => new();

        [Validated]
        public static object Make<T>() where T : class => Create<T>();
    }
}
