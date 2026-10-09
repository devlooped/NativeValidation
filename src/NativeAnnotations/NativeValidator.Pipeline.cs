using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Devlooped
{
    public static partial class NativeValidator
    {
        /// <summary>
        /// Tests whether <paramref name="instance"/> is valid. Only <see cref="RequiredAttribute"/> rules run.
        /// </summary>
        public static bool TryValidateObject(object instance, ValidationContext validationContext, ICollection<ValidationResult>? validationResults) =>
            TryValidateObject(instance, validationContext, validationResults, validateAllProperties: false);

        /// <summary>
        /// Tests whether <paramref name="instance"/> is valid.
        /// <paramref name="validateAllProperties"/> false runs only required property rules.
        /// Property failures skip type-level rules and <see cref="IValidatableObject"/>.
        /// A null <paramref name="validationResults"/> stops at the first failure.
        /// </summary>
        public static bool TryValidateObject(object instance, ValidationContext validationContext, ICollection<ValidationResult>? validationResults, bool validateAllProperties)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            if (validationContext == null)
                throw new ArgumentNullException(nameof(validationContext));
            if (!ReferenceEquals(instance, validationContext.ObjectInstance))
                throw new ArgumentException("The instance must match ValidationContext.ObjectInstance.", nameof(instance));

            var failure = default(Failure);
            return TryValidateObject(instance, validationContext, validationResults, validateAllProperties, ref failure);
        }

        /// <summary>Throws <see cref="ValidationException"/> when <paramref name="instance"/> fails a required rule.</summary>
        public static void ValidateObject(object instance, ValidationContext validationContext) =>
            ValidateObject(instance, validationContext, validateAllProperties: false);

        /// <summary>Throws <see cref="ValidationException"/> for the first failure on <paramref name="instance"/>.</summary>
        public static void ValidateObject(object instance, ValidationContext validationContext, bool validateAllProperties)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            if (validationContext == null)
                throw new ArgumentNullException(nameof(validationContext));
            if (!ReferenceEquals(instance, validationContext.ObjectInstance))
                throw new ArgumentException("The instance must match ValidationContext.ObjectInstance.", nameof(instance));

            var results = new List<ValidationResult>();
            var failure = default(Failure);
            if (!TryValidateObject(instance, validationContext, results, validateAllProperties, ref failure))
                throw new ValidationException(failure.Result!, validatingAttribute: null, failure.Value);
        }

        /// <summary>
        /// Tests <paramref name="value"/> against the rules registered for <see cref="ValidationContext.MemberName"/>.
        /// An unknown member succeeds. A value of the wrong type throws <see cref="ArgumentException"/>.
        /// </summary>
        public static bool TryValidateProperty(object? value, ValidationContext validationContext, ICollection<ValidationResult>? validationResults)
        {
            if (validationContext == null)
                throw new ArgumentNullException(nameof(validationContext));
            if (string.IsNullOrEmpty(validationContext.MemberName))
                throw new ArgumentException("ValidationContext.MemberName must name the property to validate.", nameof(validationContext));

            var property = NativeValidation.FindProperty(validationContext.ObjectType, validationContext.MemberName);
            if (property == null)
                return true;

            if (!CanBeAssigned(property.PropertyType, value))
                throw new ArgumentException($"The value for property '{property.Name}' is not of type {property.PropertyType}.", nameof(value));

            var failure = default(Failure);
            return Run(property.Rules, property.HasRequired, requiredOnly: false, value, validationContext.ObjectInstance, validationResults == null, validationResults, ref failure);
        }

        /// <summary>Throws <see cref="ValidationException"/> when <paramref name="value"/> fails the named property.</summary>
        public static void ValidateProperty(object? value, ValidationContext validationContext)
        {
            var results = new List<ValidationResult>();
            var failure = default(Failure);
            if (validationContext == null)
                throw new ArgumentNullException(nameof(validationContext));
            if (string.IsNullOrEmpty(validationContext.MemberName))
                throw new ArgumentException("ValidationContext.MemberName must name the property to validate.", nameof(validationContext));

            var property = NativeValidation.FindProperty(validationContext.ObjectType, validationContext.MemberName);
            if (property == null)
                return;

            if (!CanBeAssigned(property.PropertyType, value))
                throw new ArgumentException($"The value for property '{property.Name}' is not of type {property.PropertyType}.", nameof(value));

            if (!Run(property.Rules, property.HasRequired, requiredOnly: false, value, validationContext.ObjectInstance, breakOnFirst: false, results, ref failure))
                throw new ValidationException(failure.Result!, validatingAttribute: null, failure.Value);
        }

        /// <summary>
        /// Tests the arguments of <paramref name="method"/>. A null <paramref name="validationResults"/> stops at the first failure.
        /// </summary>
        public static bool TryValidate(object? instance, MethodBase method, ICollection<ValidationResult>? validationResults, params object?[] arguments)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            var failure = default(Failure);
            return TryValidate(instance, method, validationResults, ref failure, arguments);
        }

        /// <summary>Throws <see cref="ValidationException"/> for the first argument that fails a rule registered for <paramref name="method"/>.</summary>
        public static void Validate(object? instance, MethodBase method, params object?[] arguments)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            var results = new List<ValidationResult>();
            var failure = default(Failure);
            if (!TryValidate(instance, method, results, ref failure, arguments))
                throw new ValidationException(failure.Result!, validatingAttribute: null, failure.Value);
        }

        static bool TryValidateObject(object instance, ValidationContext validationContext, ICollection<ValidationResult>? validationResults, bool validateAllProperties, ref Failure failure)
        {
            var breakOnFirst = validationResults == null;
            var propertyFailed = false;
            foreach (var property in NativeValidation.PropertiesOf(validationContext.ObjectType))
            {
                if (property.Read == null)
                    continue;
                if (!validateAllProperties && !property.HasRequired)
                    continue;

                var value = property.Read(instance);
                if (!Run(property.Rules, property.HasRequired, requiredOnly: !validateAllProperties, value, instance, breakOnFirst, validationResults, ref failure))
                {
                    propertyFailed = true;
                    if (breakOnFirst)
                        return false;
                }
            }

            if (propertyFailed)
                return false;

            if (NativeValidation.TryGetTypeRules(validationContext.ObjectType, out var hasRequired, out var rules) &&
                !Run(rules, hasRequired, requiredOnly: false, instance, instance, breakOnFirst, validationResults, ref failure))
                return false;

            if (instance is IValidatableObject validatable)
            {
                var produced = validatable.Validate(validationContext);
                if (produced != null)
                {
                    foreach (var result in produced)
                    {
                        if (result == ValidationResult.Success)
                            continue;

                        validationResults?.Add(result);
                        failure.Record(result, instance);
                        if (breakOnFirst)
                            return false;
                        propertyFailed = true;
                    }
                }
            }

            return !propertyFailed;
        }

        static bool TryValidate(object? instance, MethodBase method, ICollection<ValidationResult>? validationResults, ref Failure failure, object?[] arguments)
        {
            if (!NativeValidation.TryGetSlots(method, out var slots))
                return true;

            arguments ??= Array.Empty<object?>();
            var breakOnFirst = validationResults == null;
            var valid = true;
            foreach (var slot in slots)
            {
                if ((uint)slot.Index >= (uint)arguments.Length)
                    continue;

                if (!Run(slot.Rules, slot.HasRequired, requiredOnly: false, arguments[slot.Index], instance, breakOnFirst, validationResults, ref failure))
                {
                    valid = false;
                    if (breakOnFirst)
                        return false;
                }
            }

            return valid;
        }

        static bool Run(NativeValidationRule[] rules, bool hasRequired, bool requiredOnly, object? value, object? instance, bool breakOnFirst, ICollection<ValidationResult>? results, ref Failure failure)
        {
            var start = 0;
            if (hasRequired && rules.Length > 0)
            {
                if (rules[0](value, instance) is { } required)
                {
                    results?.Add(required);
                    failure.Record(required, value);
                    return false;
                }

                if (requiredOnly)
                    return true;

                start = 1;
            }
            else if (requiredOnly)
            {
                return true;
            }

            var valid = true;
            for (var i = start; i < rules.Length; i++)
            {
                if (rules[i](value, instance) is not { } result)
                    continue;

                results?.Add(result);
                failure.Record(result, value);
                valid = false;
                if (breakOnFirst)
                    return false;
            }

            return valid;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Property types are the typeof values the generator emitted.")]
        static bool CanBeAssigned(Type destinationType, object? value)
        {
            if (value == null)
                return !destinationType.IsValueType || Nullable.GetUnderlyingType(destinationType) != null;

            return destinationType.IsInstanceOfType(value);
        }

        struct Failure
        {
            public ValidationResult? Result;
            public object? Value;

            public void Record(ValidationResult result, object? value)
            {
                if (Result != null)
                    return;

                Result = result;
                Value = value;
            }
        }
    }
}