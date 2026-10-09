using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Devlooped.Tests
{
    public class NativeValidatorTests
    {
        [Fact]
        public void RequiredMatchesTheAttribute()
        {
            var attribute = new RequiredAttribute();
            Assert.Equal(attribute.FormatErrorMessage("Email"), Fail(NativeValidator.Required(null, "Email", null, false, null)));
            Assert.Equal(attribute.FormatErrorMessage("Email"), Fail(NativeValidator.Required("   ", "Email", null, false, null)));
            Assert.Null(NativeValidator.Required("   ", "Email", null, true, null));
            Assert.Null(NativeValidator.Required("ada@example.com", "Email", null, false, null));
            Assert.Equal(new RequiredAttribute { ErrorMessage = "Need {0}" }.FormatErrorMessage("Email"), Fail(NativeValidator.Required(null, "Email", null, false, "Need {0}")));
        }

        [Fact]
        public void StringLengthMatchesTheAttribute()
        {
            var attribute = new StringLengthAttribute(4) { MinimumLength = 2 };
            Assert.Null(NativeValidator.StringLength(null, 2, 4, "Name", null, null));
            Assert.Null(NativeValidator.StringLength("abcd", 2, 4, "Name", null, null));
            Assert.Equal(attribute.FormatErrorMessage("Name"), Fail(NativeValidator.StringLength("a", 2, 4, "Name", null, null)));
            Assert.Equal(attribute.FormatErrorMessage("Name"), Fail(NativeValidator.StringLength("abcde", 2, 4, "Name", null, null)));
            Assert.Contains("System.Int32", Fail(NativeValidator.StringLength(1, 0, 4, "Name", null, null)), StringComparison.Ordinal);
        }

        [Fact]
        public void LengthAttributesMatchCollectionsAndStrings()
        {
            Assert.Null(NativeValidator.MinLength(null, 1, "Tags", null, null));
            Assert.Null(NativeValidator.MinLength("ab", 2, "Tags", null, null));
            Assert.NotNull(NativeValidator.MinLength(new[] { 1 }, 2, "Tags", null, null));
            Assert.Null(NativeValidator.MaxLength(new List<int> { 1, 2 }, 2, "Tags", null, null));
            Assert.Null(NativeValidator.MaxLength("abcdef", -1, "Tags", null, null));
            Assert.NotNull(NativeValidator.Length("abcd", 2, 3, "Tags", null, null));
            Assert.Null(NativeValidator.Length(new[] { 1, 2 }, 1, 2, "Tags", null, null));
            Assert.Equal(
                new MinLengthAttribute(2).FormatErrorMessage("Tags"),
                Fail(NativeValidator.MinLength("", 2, "Tags", null, null)));
            Assert.Contains("System.Int32", Fail(NativeValidator.MaxLength(1, 2, "Tags", null, null)), StringComparison.Ordinal);
            Assert.Null(NativeValidator.MaxLength(new Bag(1), 2, "Tags", null, null, static value => ((Bag)value).Count));
        }

        [Fact]
        public void RangeMatchesTheAttribute()
        {
            var attribute = new RangeAttribute(1, 10) { MinimumIsExclusive = true };
            Assert.Null(NativeValidator.Range(null, 1, 10, true, false, "Age", null, null));
            Assert.Null(NativeValidator.Range("", 1, 10, false, false, "Age", null, null));
            Assert.Null(NativeValidator.Range(2, 1, 10, true, false, "Age", null, null));
            Assert.Equal(attribute.FormatErrorMessage("Age"), Fail(NativeValidator.Range(1, 1, 10, true, false, "Age", null, null)));
            Assert.Null(NativeValidator.Range("4", 1, 10, false, false, "Age", null, null));
            Assert.NotNull(NativeValidator.Range("nope", 1, 10, false, false, "Age", null, null));

            var dates = new RangeAttribute(typeof(DateTime), "2020-01-01", "2020-01-03") { ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true };
            var parsed = NativeValidator.Range(
                "2020-01-02",
                "2020-01-01",
                "2020-01-03",
                static (string text, IFormatProvider? provider, out DateTime result) => DateTime.TryParse(text, provider, DateTimeStyles.None, out result),
                true,
                true,
                false,
                false,
                "When",
                null,
                null);
            Assert.Null(parsed);
            Assert.Equal(dates.FormatErrorMessage("When"), Fail(NativeValidator.Range(
                "2020-02-01",
                "2020-01-01",
                "2020-01-03",
                static (string text, IFormatProvider? provider, out DateTime result) => DateTime.TryParse(text, provider, DateTimeStyles.None, out result),
                true,
                true,
                false,
                false,
                "When",
                null,
                null)));
        }

        [Fact]
        public void PatternRequiresAFullMatch()
        {
            var pattern = new Regex("^a+$", RegexOptions.None, TimeSpan.FromSeconds(1));
            Assert.Null(NativeValidator.Pattern(null, pattern, "^a+$", "Code", null, null));
            Assert.Null(NativeValidator.Pattern("", pattern, "^a+$", "Code", null, null));
            Assert.Null(NativeValidator.Pattern("aaa", pattern, "^a+$", "Code", null, null));
            Assert.Equal(
                new RegularExpressionAttribute("^a+$").FormatErrorMessage("Code"),
                Fail(NativeValidator.Pattern("ab", pattern, "^a+$", "Code", null, null)));
        }

        [Fact]
        public void BuiltInFormatsMatchTheAttributes()
        {
            Assert.Equal(new EmailAddressAttribute().IsValid("a@b.co"), NativeValidator.EmailAddress("a@b.co", "Email", null, null) == null);
            Assert.Equal(new EmailAddressAttribute().IsValid("a@b.co\n"), NativeValidator.EmailAddress("a@b.co\n", "Email", null, null) == null);
            Assert.Equal(new EmailAddressAttribute().IsValid("ab.co"), NativeValidator.EmailAddress("ab.co", "Email", null, null) == null);
            Assert.Equal(new PhoneAttribute().IsValid("425-555-0100 x12"), NativeValidator.Phone("425-555-0100 x12", "Phone", null, null) == null);
            Assert.Equal(new PhoneAttribute().IsValid("nope"), NativeValidator.Phone("nope", "Phone", null, null) == null);
            Assert.Equal(new CreditCardAttribute().IsValid("4111111111111111"), NativeValidator.CreditCard("4111111111111111", "Card", null, null) == null);
            Assert.Equal(new CreditCardAttribute().IsValid("4111-1111-1111-1112"), NativeValidator.CreditCard("4111-1111-1111-1112", "Card", null, null) == null);
            Assert.Equal(new UrlAttribute().IsValid("https://example.com"), NativeValidator.Url("https://example.com", "Site", null, null) == null);
            Assert.Equal(new UrlAttribute().IsValid("mailto:a@b.co"), NativeValidator.Url("mailto:a@b.co", "Site", null, null) == null);
            Assert.Equal(new UrlAttribute().IsValid(new Uri("ftp://files.example.com")), NativeValidator.Url(new Uri("ftp://files.example.com"), "Site", null, null) == null);
            Assert.Equal(new Base64StringAttribute().IsValid("YQ=="), NativeValidator.Base64String("YQ==", "Token", null, null) == null);
            Assert.Equal(new Base64StringAttribute().IsValid("@@@@"), NativeValidator.Base64String("@@@@", "Token", null, null) == null);
            Assert.Equal(new FileExtensionsAttribute().IsValid("photo.PNG"), NativeValidator.FileExtensions("photo.PNG", null, "File", null, null) == null);
            Assert.Equal(new FileExtensionsAttribute { Extensions = "pdf" }.IsValid("notes.pdf"), NativeValidator.FileExtensions("notes.pdf", "pdf", "File", null, null) == null);
            Assert.Equal(new FileExtensionsAttribute().IsValid("notes.pdf"), NativeValidator.FileExtensions("notes.pdf", null, "File", null, null) == null);
        }

        [Fact]
        public void EnumAllowedDeniedAndCompare()
        {
            var defined = new EnumDataTypeAttribute(typeof(DayOfWeek));
            Assert.Equal(defined.IsValid(null), NativeValidator.Enum<DayOfWeek>(null, false, "Day", null, null) == null);
            Assert.Equal(defined.IsValid(""), NativeValidator.Enum<DayOfWeek>("", false, "Day", null, null) == null);
            Assert.Equal(defined.IsValid(DayOfWeek.Monday), NativeValidator.Enum<DayOfWeek>(DayOfWeek.Monday, false, "Day", null, null) == null);
            Assert.Equal(defined.IsValid(9), NativeValidator.Enum<DayOfWeek>(9, false, "Day", null, null) == null);
            Assert.Equal(defined.IsValid("Monday"), NativeValidator.Enum<DayOfWeek>("Monday", false, "Day", null, null) == null);

            var flags = new EnumDataTypeAttribute(typeof(ConsoleModifiers));
            Assert.Equal(flags.IsValid(ConsoleModifiers.Alt | ConsoleModifiers.Shift), NativeValidator.Enum<ConsoleModifiers>(ConsoleModifiers.Alt | ConsoleModifiers.Shift, true, "Mods", null, null) == null);
            Assert.Equal(flags.IsValid((ConsoleModifiers)99), NativeValidator.Enum<ConsoleModifiers>((ConsoleModifiers)99, true, "Mods", null, null) == null);

            Assert.Null(NativeValidator.Allowed("open", "Status", null, null, "open", "closed"));
            Assert.Null(NativeValidator.Allowed(null, "Status", null, null, "open", null));
            Assert.NotNull(NativeValidator.Allowed(null, "Status", null, null, "open"));
            Assert.NotNull(NativeValidator.Denied("no", "Status", null, null, "no"));
            Assert.Null(NativeValidator.Denied(null, "Status", null, null, "no"));
            Assert.Null(NativeValidator.Compare("a", "a", "Confirm", null, "Password", null));
            Assert.Equal(
                new CompareAttribute("Password").FormatErrorMessage("Confirm"),
                Fail(NativeValidator.Compare("a", "b", "Confirm", null, "Password", null)));
        }

        [Fact]
        public void CustomResultUsesTheMethodMessage()
        {
            Assert.Null(NativeValidator.CustomResult(ValidationResult.Success, "Name", null, null));
            Assert.Equal("Name is not valid.", Fail(NativeValidator.CustomResult(new ValidationResult(null), "Name", null, null)));
            Assert.Equal("Bad Name", Fail(NativeValidator.CustomResult(new ValidationResult("Bad {0}"), "Name", null, null)));
            Assert.Contains("string", NativeValidator.ConversionFailed(1, "string", "Checks", "Name", "Name").ErrorMessage, StringComparison.Ordinal);
        }

        [Fact]
        public void SignatureMatchesRuntimeTypes()
        {
            Assert.Equal("System.Int32", NativeValidationSignature.Format(typeof(int)));
            Assert.Equal("System.Int32&", NativeValidationSignature.Format(typeof(int).MakeByRefType()));
            Assert.Equal("System.Int32[]", NativeValidationSignature.Format(typeof(int[])));
            Assert.Equal("System.Int32[,]", NativeValidationSignature.Format(typeof(int[,])));
            Assert.Equal("System.Int32[][]", NativeValidationSignature.Format(typeof(int[][])));
            Assert.Equal("System.Nullable<System.Int32>", NativeValidationSignature.Format(typeof(int?)));
            Assert.Equal("System.Collections.Generic.Dictionary<System.String,System.Int32>", NativeValidationSignature.Format(typeof(Dictionary<string, int>)));
            Assert.Equal("Devlooped.Tests.NativeValidatorTests.Outer<System.Int32>.Inner<System.String>", NativeValidationSignature.Format(typeof(Outer<int>.Inner<string>)));

            var method = typeof(NativeValidatorTests).GetMethod(nameof(Generic))!.MakeGenericMethod(typeof(int));
            Assert.Equal("!!0,System.Int32", NativeValidationSignature.Format(method));
            Assert.Equal("!!0", NativeValidationSignature.MethodTypeParameter(0));
            Assert.Equal("System.Int32,System.String", NativeValidationSignature.Join(NativeValidationSignature.Format(typeof(int)), NativeValidationSignature.Format(typeof(string))));
        }

        public void Generic<T>(T value, int id)
        {
        }

        static string Fail(ValidationResult? result) => result!.ErrorMessage!;

        public class Outer<T>
        {
            public class Inner<U>
            {
            }
        }

        sealed class Bag : IEnumerable
        {
            public Bag(int count) => Count = count;

            public int Count { get; }

            public IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
        }
    }
}
