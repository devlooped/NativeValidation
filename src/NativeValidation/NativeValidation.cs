using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Devlooped.DataAnnotations
{
    /// <summary>
    /// One parameter of a member that has data-annotation rules.
    /// </summary>
    public sealed class NativeValidationSlot
    {
        /// <summary>Creates a slot for the parameter at <paramref name="index"/>.</summary>
        /// <param name="hasRequired">True when <paramref name="rules"/> starts with <see cref="RequiredAttribute"/>.</param>
        public NativeValidationSlot(int index, bool hasRequired, params NativeValidationRule[] rules)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (rules == null || rules.Length == 0)
                throw new ArgumentException("At least one rule is required.", nameof(rules));

            Index = index;
            HasRequired = hasRequired;
            Rules = rules;
        }

        /// <summary>Parameter position. Property setters validate the value parameter.</summary>
        public int Index { get; }

        /// <summary>True when <see cref="Rules"/> starts with the check for <see cref="RequiredAttribute"/>.</summary>
        public bool HasRequired { get; }

        /// <summary>Rules for this parameter. A required rule, when present, is first.</summary>
        public NativeValidationRule[] Rules { get; }
    }

    /// <summary>
    /// A property whose validation rules were registered by the source generator.
    /// <see cref="Read"/> is a direct property access, so object validation does not reflect.
    /// </summary>
    public sealed class NativeValidationProperty
    {
        /// <summary>Creates a property registration.</summary>
        public NativeValidationProperty(string name, Type propertyType, bool hasRequired, Func<object?, object?>? read, params NativeValidationRule[] rules)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A property name is required.", nameof(name));
            if (propertyType == null)
                throw new ArgumentNullException(nameof(propertyType));
            if (rules == null || rules.Length == 0)
                throw new ArgumentException("At least one rule is required.", nameof(rules));

            Name = name;
            PropertyType = propertyType;
            HasRequired = hasRequired;
            Read = read;
            Rules = rules;
        }

        /// <summary>Property name used by <see cref="ValidationContext.MemberName"/>.</summary>
        public string Name { get; }

        /// <summary>Property type, used to reject a value that cannot be assigned.</summary>
        public Type PropertyType { get; }

        /// <summary>True when <see cref="Rules"/> starts with the check for <see cref="RequiredAttribute"/>.</summary>
        public bool HasRequired { get; }

        /// <summary>Reads the property from an instance. Null when the getter is not accessible to generated code.</summary>
        public Func<object?, object?>? Read { get; }

        /// <summary>Rules for the property value. A required rule, when present, is first.</summary>
        public NativeValidationRule[] Rules { get; }
    }

    /// <summary>
    /// Validates one argument. Return <see langword="null"/> when the value is acceptable.
    /// <paramref name="instance"/> is the object the member was called on.
    /// </summary>
    public delegate ValidationResult? NativeValidationRule(object? value, object? instance);

    /// <summary>
    /// Registrations of members and parameters that carry validation attributes.
    /// The source generator fills this from a module initializer. Lookups key a member by its
    /// declaring type, metadata name, and <see cref="NativeValidationSignature"/>.
    /// </summary>
    public static class NativeValidation
    {
        static readonly NativeValidationSlot[] None = Array.Empty<NativeValidationSlot>();
        static readonly ConcurrentDictionary<Key, NativeValidationSlot[]> members = new();
        static readonly ConcurrentDictionary<RuntimeMethodHandle, NativeValidationSlot[]> resolved = new();
        static readonly ConcurrentDictionary<RuntimeTypeHandle, NativeValidationProperty[]> properties = new();
        static readonly ConcurrentDictionary<RuntimeTypeHandle, TypeRules> typeRules = new();

        /// <summary>
        /// Registers the rules for one member. A later registration for the same member replaces the earlier one.
        /// </summary>
        /// <param name="declaringType">The interface or class that declares the member.</param>
        /// <param name="member">Metadata name, such as <c>set_Email</c>, <c>Save</c>, or <c>.ctor</c>.</param>
        /// <param name="signature"><see cref="NativeValidationSignature"/> of the member.</param>
        /// <param name="slots">Parameters that have rules.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Register(Type declaringType, string member, string signature, params NativeValidationSlot[] slots)
        {
            if (declaringType == null)
                throw new ArgumentNullException(nameof(declaringType));
            if (member == null)
                throw new ArgumentNullException(nameof(member));
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (slots == null || slots.Length == 0)
                throw new ArgumentException("At least one slot is required.", nameof(slots));

            members[new Key(declaringType.TypeHandle, member, signature)] = slots;
            resolved.Clear();
        }

        /// <summary>Registers the properties of one type. A later registration replaces the earlier one.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void RegisterProperties(Type declaringType, params NativeValidationProperty[] items)
        {
            if (declaringType == null)
                throw new ArgumentNullException(nameof(declaringType));
            if (items == null || items.Length == 0)
                throw new ArgumentException("At least one property is required.", nameof(items));

            properties[declaringType.TypeHandle] = items;
        }

        /// <summary>Registers validation rules declared on the type itself.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void RegisterType(Type declaringType, bool hasRequired, params NativeValidationRule[] rules)
        {
            if (declaringType == null)
                throw new ArgumentNullException(nameof(declaringType));
            if (rules == null || rules.Length == 0)
                throw new ArgumentException("At least one rule is required.", nameof(rules));

            typeRules[declaringType.TypeHandle] = new TypeRules(hasRequired, rules);
        }

        /// <summary>True when <paramref name="method"/> has registered validation rules.</summary>
        public static bool AppliesTo(MethodBase method) => method != null && TryResolve(method, out _);

        internal static bool TryGetSlots(MethodBase method, out NativeValidationSlot[] slots) => TryResolve(method, out slots);

        internal static NativeValidationProperty[] PropertiesOf(Type type)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<NativeValidationProperty>();
            foreach (var contract in Contracts(type))
            {
                if (!properties.TryGetValue(contract.TypeHandle, out var items))
                    continue;

                foreach (var property in items)
                {
                    if (seen.Add(property.Name))
                        list.Add(property);
                }
            }

            return list.ToArray();
        }

        internal static NativeValidationProperty? FindProperty(Type type, string name)
        {
            foreach (var contract in Contracts(type))
            {
                if (!properties.TryGetValue(contract.TypeHandle, out var items))
                    continue;

                foreach (var property in items)
                {
                    if (property.Name == name)
                        return property;
                }
            }

            return null;
        }

        internal static bool TryGetTypeRules(Type type, out bool hasRequired, out NativeValidationRule[] rules)
        {
            foreach (var contract in Contracts(type))
            {
                if (typeRules.TryGetValue(contract.TypeHandle, out var found))
                {
                    hasRequired = found.HasRequired;
                    rules = found.Rules;
                    return true;
                }
            }

            hasRequired = false;
            rules = Array.Empty<NativeValidationRule>();
            return false;
        }

        static bool TryResolve(MethodBase method, out NativeValidationSlot[] slots)
        {
            if (!resolved.TryGetValue(method.MethodHandle, out slots!))
            {
                slots = Find(method) ?? None;
                resolved.TryAdd(method.MethodHandle, slots);
            }

            return slots.Length != 0;
        }

        static NativeValidationSlot[]? Find(MethodBase method)
        {
            var name = SimpleName(method.Name);
            var signature = NativeValidationSignature.Format(method);
            var declaring = method.DeclaringType;
            if (declaring == null)
                return null;

            foreach (var contract in Contracts(declaring))
            {
                if (members.TryGetValue(new Key(contract.TypeHandle, name, signature), out var slots))
                    return slots;
            }

            return null;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Validation looks up the method the caller already has, so its type and interfaces are kept.")]
        static Type[] Contracts(Type type)
        {
            var count = 1;
            for (var current = type.BaseType; current != null && current != typeof(object); current = current.BaseType)
                count++;

            var interfaces = type.GetInterfaces();
            var ordered = new Type[interfaces.Length];
            Array.Copy(interfaces, ordered, interfaces.Length);
            Array.Sort(ordered, static (left, right) => InterfaceDepth(right).CompareTo(InterfaceDepth(left)));

            var contracts = new Type[count + ordered.Length];
            var index = 0;
            contracts[index++] = type;
            for (var current = type.BaseType; current != null && current != typeof(object); current = current.BaseType)
                contracts[index++] = current;
            Array.Copy(ordered, 0, contracts, index, ordered.Length);
            return contracts;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Validation looks up the method the caller already has, so its type and interfaces are kept.")]
        static int InterfaceDepth(Type type)
        {
            var depth = 0;
            foreach (var parent in type.GetInterfaces())
                depth = Math.Max(depth, InterfaceDepth(parent) + 1);
            return depth;
        }

        static string SimpleName(string name)
        {
            var dot = name.LastIndexOf('.');
            return dot < 0 ? name : name.Substring(dot + 1);
        }

        readonly record struct Key(RuntimeTypeHandle Type, string Member, string Signature);

        readonly record struct TypeRules(bool HasRequired, NativeValidationRule[] Rules);
    }
}
