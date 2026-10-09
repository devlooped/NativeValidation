# System.ComponentModel.DataAnnotations and Native AOT / trimming

Findings from primary sources only: [learn.microsoft.com](https://learn.microsoft.com), `dotnet/runtime` and `dotnet/aspnetcore` source and issues, and the .NET libraries "what's new" docs. Source links point at `main` as read on 2026-10-09 unless a commit is in the URL. This note does not treat secondary blogs as the source of a claim.

## 1. Why reflection-based validation is incompatible

The trimmer only follows code it can see statically. Reflection hides the target until runtime, so members the app never names in IL can be removed. Microsoft's trim-analysis doc states that directly: the trimmer "starts from known entry points" and "struggles with dynamic operations where the target of an operation isn't known until runtime," and `GetType().GetProperties()` is one of the examples it cannot follow. `RequiresUnreferencedCode` is the annotation for a pattern that "cannot be made statically analyzable"; it suppresses warnings inside the member and warns at every call site ([Understanding trim analysis](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-concepts)).

Native AOT publish requires trimming. The deployment overview lists "Requires trimming, which has limitations" and "No runtime code generation, for example, `System.Reflection.Emit`" ([Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)). ASP.NET Core's Native AOT page says the same thing in product terms: "any unused code is trimmed. As a result, an app can't use unbounded reflection at runtime" ([ASP.NET Core support for Native AOT](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot)).

`Validator` discovers rules by reflecting. `TryValidateObject` and `TryValidateProperty` are annotated `[RequiresUnreferencedCode]`. Property values come from `TypeDescriptor.GetProperties(instance.GetType())`; the store then loads attributes with `TypeDescriptor.GetAttributes` ([Validator.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs), [ValidationAttributeStore.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationAttributeStore.cs)). `TypeDescriptor.GetProperties(Type)` is itself `[RequiresUnreferencedCode("PropertyDescriptor's PropertyType cannot be statically discovered.")]` and demands `DynamicallyAccessedMembers(All)` on the type. `GetProperties(object)` appends "The Type of component cannot be statically discovered." ([PropertyDescriptor.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.TypeConverter/src/System/ComponentModel/PropertyDescriptor.cs), [TypeDescriptor.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.TypeConverter/src/System/ComponentModel/TypeDescriptor.cs)). A runtime issue shows the practical failure: after trimming, `TypeDescriptor.GetProperties(type)` can return an empty collection (property count 0) when the type was not preserved for reflection ([dotnet/runtime#101202](https://github.com/dotnet/runtime/issues/101202)).

`ValidationAttribute.IsValid` is a different case. The virtual methods are the extension point (`GetValidationResult` calls `IsValid`), and they are not annotated `RequiresUnreferencedCode` ([ValidationAttribute.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationAttribute.cs)). A runtime trimming owner said why they will not mark the virtual method wholesale: "we generally try to avoid marking virtual methods with RequiresUnreferencedCode because it becomes viral quickly. In this case, `ValidationAttribute.IsValid` isn't inherently problematic, so marking it unsafe would exclude some legitimate derived attribute definitions" ([dotnet/runtime#112111](https://github.com/dotnet/runtime/issues/112111), Steve Bomer). Virtual dispatch of a preserved override is compatible. What is incompatible is discovering which attribute to call, and the implementations that reflect inside `IsValid` (`MinLength` / `MaxLength` / `Length`, `Compare`, and `Range(Type, string, string)`).

`TryValidateValue` is the exception on `Validator`: the caller passes the attribute list, and that method has no `RequiresUnreferencedCode` ([Validator.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs)). The options source generator uses it for that reason (section 4).

## 2. What the trimmer and AOT analyzer warn about

[IL2026](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-warnings/il2026) fires when code calls a member with `RequiresUnreferencedCodeAttribute`. The documented shape is: "Using method '…' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code," followed by the attribute's message.

[IL3050](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3050) fires for `RequiresDynamicCodeAttribute`, which means the member may need to generate code at runtime. A code search of `src/libraries/System.ComponentModel.Annotations` for `RequiresDynamicCode` returns no hits. DataAnnotations warnings on these APIs are IL2026. Native AOT still reports them, because Native AOT publish runs trim analysis ([Introduction to AOT warnings](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings)). The options-validation article's mention of IL2025 and IL3050 is about configuration binding under `<PublishAot>true</PublishAot>`, and the documented mitigation there is `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`, not a DataAnnotations annotation ([Compile-time options validation source generation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator)).

### Messages on DataAnnotations APIs

Quoted from runtime source.

| API | Message |
| --- | --- |
| `ValidationContext(object)`, `ValidationContext(object, IDictionary)`, `ValidationContext(object, IServiceProvider, IDictionary)`; `Validator.TryValidateObject` / `ValidateObject` and the async twins | "Constructing a ValidationContext without a display name is not trim-safe because it uses reflection to discover the type of the instance being validated in order to resolve the DisplayNameAttribute when a display name is not provided." Constant `ValidationContext.InstanceTypeNotStaticallyDiscovered`. The source comment says "DisplayNameAttribute"; the lookup is `DisplayAttribute` via `ValidationAttributeStore`. |
| `Validator.TryValidateProperty` / `ValidateProperty` and the async twins; `ValidationAttributeStore` type/property lookups | "The Type of validationContext.ObjectType cannot be statically discovered." |
| `MinLengthAttribute`, `MaxLengthAttribute`, `LengthAttribute` constructors | "Uses reflection to get the 'Count' property on types that don't implement ICollection. This 'Count' property may be trimmed. Ensure it is preserved." (`CountPropertyHelper.RequiresUnreferencedCodeMessage`, defined in [MaxLengthAttribute.cs](https://github.com/dotnet/runtime/blob/5535e31a712343a63f5d7d796cd874e563e5ac14/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/MaxLengthAttribute.cs)) |
| `CompareAttribute(string)` | "The property referenced by 'otherProperty' may be trimmed. Ensure it is preserved." |
| `RangeAttribute(Type, string, string)` | "Generic TypeConverters may require the generic types to be annotated. For example, NullableConverter requires the underlying type to be DynamicallyAccessedMembers All." The `int` and `double` constructors are not annotated. |
| `DataAnnotationValidateOptions<TOptions>` constructor | "The implementation of Validate method on this type will walk through all properties of the passed in options object, and its type cannot be statically analyzed so its members may be trimmed." ([DataAnnotationValidateOptions.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Options.DataAnnotations/src/DataAnnotationValidateOptions.cs)) |
| `AssociatedMetadataTypeTypeDescriptor.GetProperties` | "PropertyDescriptor's PropertyType cannot be statically discovered. The public parameterless constructor or the 'Default' static field may be trimmed from the Attribute's Type." |

A recorded IL2026 from the runtime repo, for the length attributes:

> Program.cs(10,6): warning IL2026: Using member 'System.ComponentModel.DataAnnotations.MinLengthAttribute.MinLengthAttribute(Int32)' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code. Uses reflection to get the 'Count' property on types that don't implement ICollection. This 'Count' property may be trimmed. Ensure it is preserved

([dotnet/runtime#112111](https://github.com/dotnet/runtime/issues/112111); the same text appears on the options generator before the attribute replacements in [dotnet/runtime#92327](https://github.com/dotnet/runtime/issues/92327))

`Count` lookup is `value.GetType().GetRuntimeProperty("Count")` when the value is not an `ICollection` ([MaxLengthAttribute.cs](https://github.com/dotnet/runtime/blob/5535e31a712343a63f5d7d796cd874e563e5ac14/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/MaxLengthAttribute.cs)). `CompareAttribute.IsValid` calls `validationContext.ObjectType.GetRuntimeProperty(OtherProperty)` and then `CustomAttributeExtensions.GetCustomAttributes` to find a `DisplayAttribute` on that property ([CompareAttribute.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/CompareAttribute.cs)). The `Type` `RangeAttribute` constructor calls `TypeDescriptor.GetConverter(OperandType)` and suppresses IL2026 inside the helper because the constructor already carries the attribute ([RangeAttribute.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/RangeAttribute.cs)).

The ASP.NET team filed the same gap against model validation: "Not all `ValidationAttributes` exposed by System.ComponentModel are native AoT compatible. Some use unbounded reflection to determine types and available APIs on them," naming `RangeAttribute` and `MaxLengthAttribute` ([dotnet/aspnetcore#61221](https://github.com/dotnet/aspnetcore/issues/61221), Safia Abdalla). That issue is still open.

## 3. Is there an official AOT-safe replacement in the BCL?

`System.ComponentModel.DataAnnotations.Validator` is still the reflection helper. The .NET 10 API page for `TryValidateObject` lists `RequiresUnreferencedCodeAttribute` on the method ([Validator.TryValidateObject](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.dataannotations.validator.tryvalidateobject?view=net-10.0)). The .NET 11 libraries notes add asynchronous validation (`AsyncValidationAttribute`, `IAsyncValidatableObject`, `Validator.TryValidateObjectAsync` / `TryValidatePropertyAsync` / `TryValidateValueAsync`) and do not describe those entry points as trim-safe ([What's new in .NET libraries for .NET 11](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/libraries)). In source, `TryValidateObjectAsync` and `TryValidatePropertyAsync` carry the same `RequiresUnreferencedCode` messages as the synchronous methods ([Validator.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs)).

What the BCL did add, in .NET 10, is a constructor that avoids the display-name reflection:

> The `ValidationContext` class, used during options validation, includes a new constructor overload that explicitly accepts the `displayName` parameter: `ValidationContext(Object, String, IServiceProvider, IDictionary<Object,Object>)`. The display name ensures AOT safety and enables its use in native builds without warnings.

([What's new in .NET libraries for .NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries))

The source comment on that constructor: "This constructor is trim-safe because it does not use reflection to resolve the Type of the instance to support setting the DisplayName" ([ValidationContext.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationContext.cs)). It does not discover attributes. `TryValidateObject` remains annotated.

`ValidateDataAnnotations()` is the reflection path. It registers `DataAnnotationValidateOptions<T>`, whose constructor is `RequiresUnreferencedCode` as quoted above ([DataAnnotationValidateOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.options.dataannotationvalidateoptions-1)).

The known-incompatibilities article lists reflection-based serializers and tells you to rewrite them with source generation. It does not name `System.ComponentModel.DataAnnotations` or offer a BCL substitute ([Known trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)).

An ASP.NET owner, asked whether a shared generator would live on `System.ComponentModel.Annotations`, wrote in 2024: "We've only had a few conversations about what this might look like and haven't committed to a full design yet" ([dotnet/aspnetcore#46349](https://github.com/dotnet/aspnetcore/issues/46349), Safia Abdalla). Issue #61221 is the open request to make the attributes themselves AOT-safe.

## 4. Workarounds documented by first-party sources

Primary sources document two source generators and one trim-safe constructor. They do not document a hand-written `if` replacement for `Validator`, and this note does not add one.

### Options validation source generator (.NET 8)

Shipped to "reduce startup overhead and improve the validation feature set" by generating `IValidateOptions<T>` from data-annotation attributes ([What's new in .NET 8 runtime](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/runtime)). The current article says the generated code "is optimized for performance and doesn't rely on reflection. It's also AOT-compatible." It replaces `RangeAttribute`, `MaxLengthAttribute`, `MinLengthAttribute`, and `LengthAttribute` with generated types such as `__SourceGen__RangeAttribute` "because the `RangeAttribute` relies on reflection for validation" ([Compile-time options validation source generation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator)). The same page tells custom-attribute authors to "refrain from using reflection for validation" and to "craft strongly typed code that doesn't rely on reflection."

The generator still calls `Validator.TryValidateValue` and `new ValidationContext(options)`. The sample output suppresses IL2026 with justification "The created ValidationContext object is used in a way that never call reflection", then assigns `MemberName` and `DisplayName` before validation ([same article](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator)). That matches the runtime fix for [dotnet/runtime#92327](https://github.com/dotnet/runtime/issues/92327) in [dotnet/runtime#93088](https://github.com/dotnet/runtime/pull/93088): the context is safe only when the reflection path in `DisplayName` does not run, and the reflecting built-in attributes are replaced.

Documented limits of this generator:

- It implements `IValidateOptions<T>`. Calling `ValidateDataAnnotations()` is explicitly not required, and the generator is not a replacement for `Validator.TryValidateObject` ([options validation generator](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator)).
- `[MinLength]` (and the other replaced attributes) still exist on user source. The runtime team observed that the generator's private copies do not remove IL2026 from the attribute the user wrote: "even when using the options validation source generator — even though the generator generates its own attribute types, the original attribute still exists in user code." Tarek Mahmoud Sayed's reply: "For now, it is easy for the users to suppress this warning" ([dotnet/runtime#112111](https://github.com/dotnet/runtime/issues/112111)).
- `SYSLIB1201`: cannot use `ValidateObjectMembersAttribute` or `ValidateEnumeratedItemsAttribute` on members whose type is an open generic.
- `SYSLIB1206`: cannot validate private fields or properties.
- `SYSLIB1211`: unsupported circular references.
- `SYSLIB1214`: cannot validate constants or static fields or properties.
- `SYSLIB1217`: the length attributes "only applicable to properties of type string, array, or `ICollection`."

([SYSLIB diagnostics for options validation source generation](https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib1201-1219))

By default, data-annotation options validation "only validates the properties of the options class itself. It doesn't recursively validate nested objects or items in collections" unless you add `ValidateObjectMembersAttribute` and `ValidateEnumeratedItemsAttribute` ([Options pattern in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)).

### Microsoft.Extensions.Validation (.NET 10)

A Roslyn source generator for Blazor and Minimal APIs. Rules are still declared with data annotations and `IValidatableObject`. "The API isn't supported for MVC or Razor Pages" ([Validation in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/validation?view=aspnetcore-10.0)). The ASP.NET Native AOT compatibility table marks MVC as not supported ([ASP.NET Core support for Native AOT](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0)). Safia Abdalla, on the design issue: "There's no plan to rewrite MVC's validation APIs to use these new abstractions" ([dotnet/aspnetcore#46349](https://github.com/dotnet/aspnetcore/issues/46349)).

Documented limits:

- Metadata is generated only for the assembly that calls `AddValidation`. Types in another assembly need that assembly to call `AddValidation` too.
- The generator cannot see model types declared in `.razor` files, because "a source generator can't inspect another generator's output."
- Types the generator cannot see from an endpoint signature need `[ValidatableType]`. In .NET 10 that attribute is experimental (`ASP0029`) in plain class libraries.
- Without generated metadata, Blazor's `DataAnnotationsValidator` "falls back to `System.ComponentModel.DataAnnotations.Validator`, which validates top-level properties only." Minimal APIs run no automatic validation.
- Built-in attributes that are `RequiresUnreferencedCode` are still the ones the generator consumes. Issue #61221 is the open work to stop doing that unsafely.

([Validation in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/validation?view=aspnetcore-10.0), [dotnet/aspnetcore#61221](https://github.com/dotnet/aspnetcore/issues/61221))

### Setting the display name yourself

If code suppresses IL2026 on `new ValidationContext(instance)` and then expects `[Display]` to supply the name, Native AOT drops the property metadata and the message falls back to the property name. Eric Erhardt: "the Native AOT compiler is trimming the property metadata from the `Todo` class. There is a warning that this could happen, which is being suppressed." `PublishTrimmed` at the time kept metadata for properties referenced by calls; `PublishAot` did not, unless something reflected on the type. Preserving `PublicProperties` on the generic argument made that repro work. Michal Strehovsky: behavioral differences are expected when a warning was suppressed, and "it is only valid to suppress a warning if there are annotations or code that ensure the reflected-on members are visible targets of reflection. It is not sufficient that the member was simply a target of a call, field or property access" ([dotnet/runtime#84324](https://github.com/dotnet/runtime/issues/84324), quoting [Prepare .NET libraries for trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)).

The .NET 10 constructor that takes `displayName` is the supported way to skip that lookup (section 3). The options generator assigns `DisplayName` in generated code and suppresses the warning on the older constructor.

## 5. ErrorMessage, resource lookup, and DisplayAttribute

`ValidationAttribute` stores three mutually exclusive sources for the message ([ValidationAttribute.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationAttribute.cs)):

- `ErrorMessage` is a literal. `SetupResourceAccessor` caches `() => localErrorMessage`. No reflection.
- `ErrorMessageResourceName` plus `ErrorMessageResourceType` call `Type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)`. The property must be a static `string` with a public or assembly getter. `ErrorMessageResourceType` is `[DynamicallyAccessedMembers(PublicProperties | NonPublicProperties)]`, so a statically visible resource type keeps those properties. Setting both `ErrorMessage` and `ErrorMessageResourceName`, or only one of the resource pair, throws `InvalidOperationException`.
- `FormatErrorMessage` runs `string.Format` with the display name as `{0}`. Derived attributes override `FormatMessage` when they need more arguments (`Range` uses `{0}`, `{1}`, `{2}`; `Compare` uses `{0}` and `{1}`).

`DisplayAttribute` does not validate. `Name`, `ShortName`, `Description`, `Prompt`, and `GroupName` are either literals or resource keys. `GetName()` goes through `LocalizableString.GetLocalizableValue()`, which calls `_resourceType.GetRuntimeProperty(_propertyValue)` and requires a public static `string` getter. `ResourceType` is `[DynamicallyAccessedMembers(PublicProperties)]` ([DisplayAttribute.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/DisplayAttribute.cs), [LocalizableString.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/LocalizableString.cs)).

`ValidationContext.DisplayName`, when unset, calls `GetDisplayName()`, which asks `ValidationAttributeStore` for the `DisplayAttribute` on the type or property (`TypeDescriptor`) and then `displayAttribute.GetName()`. That is why the constructors that omit `displayName` are `RequiresUnreferencedCode`, and why the getter suppresses IL2026 with "Constructors that trigger this codepath are marked with RequiresUnreferencedCode" ([ValidationContext.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationContext.cs)). Issue #84324 is the Native AOT consequence: the `Display` name disappears when property metadata was trimmed.

`Microsoft.Extensions.Validation` documents a separate localization path. Attributes that set `ErrorMessageResourceType` or `DisplayAttribute.ResourceType` "perform their own resource lookup and aren't processed by the `Microsoft.Extensions.Validation` localizer" ([Validation in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/validation?view=aspnetcore-10.0)).

## 6. CustomValidationAttribute

`CustomValidationAttribute` resolves the target at runtime ([CustomValidationAttribute.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/CustomValidationAttribute.cs)):

- The constructor takes `Type validatorType` annotated `[DynamicallyAccessedMembers(PublicMethods)]`, plus a method name string. It is not `RequiresUnreferencedCode`. When the `Type` argument is a compile-time `typeof`, the trimmer keeps that type's public methods ([Understanding trim analysis](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-concepts)).
- `ValidateMethodParameter` calls `ValidatorType.GetMethods(BindingFlags.Public | BindingFlags.Static)` and `SingleOrDefault` on an ordinal name match. The method must return `ValidationResult` (or a derived type) and have either one parameter or a second parameter of type `ValidationContext`. The value parameter must not be `byref`.
- `IsValid` may `Convert.ChangeType` into the first parameter's type, then `MethodInfo.Invoke`. `TargetInvocationException` is unwrapped.

`SingleOrDefault` means two public static methods with the same name make the attribute malformed. The lookup is by name, not by a closed signature the trimmer can see as a direct call. A `Type` that is not statically known fails the `DynamicallyAccessedMembers` contract (IL2072-class warnings), which is the analyzable form of the same reflection, rather than an unconditional `RequiresUnreferencedCode` on the attribute.

## 7. Open generics, methods and parameters, IValidatableObject

### Open generics

The options generator refuses open generic member types: `SYSLIB1201`, "Can't use `ValidateObjectMembersAttribute` or `ValidateEnumeratedItemsAttribute` on fields or properties with open generic types" ([SYSLIB1201–1219](https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib1201-1219)).

Native AOT does not generate generic instantiations on demand. "Generic parameters substituted with struct type arguments have specialized code generated for each instantiation. … In Native AOT, all instantiations are pre-generated" ([Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)). `Type.MakeGenericType` on a type the compiler cannot see is the IL3050 example ([IL3050](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3050)). `Validator` itself does not call `MakeGenericType`. It does call `GetGenericTypeDefinition()` when checking whether a null value fits `Nullable<>` ([Validator.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs)).

`Microsoft.Extensions.Validation` only generates metadata for types it can see at compile time. "In some cases, not all of the types that are part of the object graph can be determined at compile time"; the documented escape is `[ValidatableType]` on a root the generator should include ([Validation in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/validation?view=aspnetcore-10.0)).

### Methods and parameters

`Validator` validates objects, properties, and a caller-supplied value. The public surface is `TryValidateObject`, `TryValidateProperty`, and `TryValidateValue`, plus the throwing and async forms. There is no method-validation API. `GetPropertyValues` enumerates `TypeDescriptor.GetProperties` and the source comment says "Ignores indexed properties" ([Validator.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs)). Official remarks: `TryValidateObject` evaluates attributes on the object type and `Required` properties; with `validateAllProperties: true` it also evaluates immediate properties, and "does not recursively validate properties of the objects returned by the properties" ([TryValidateObject](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.dataannotations.validator.tryvalidateobject?view=net-10.0)).

`CustomValidationAttribute` and `DisplayAttribute` may be applied to methods and parameters (`AttributeTargets` includes `Method` and `Parameter`). `Validator` never reads those targets; it only asks `TypeDescriptor` for properties and type-level attributes. Parameter validation in the docs is the ASP.NET generator: for each Minimal API parameter it validates attributes on the parameter, then the value ([Validation in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/validation?view=aspnetcore-10.0)). `ValidatableParameterInfo` is the generated description of that parameter ([Microsoft.Extensions.Validation namespace](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.validation)).

### IValidatableObject

`IValidatableObject.Validate(ValidationContext)` is an interface method the type implements directly. The interface is not annotated for trimming ([IValidatableObject.Validate](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.dataannotations.ivalidatableobject.validate?view=net-10.0)).

`Validator.GetObjectValidationErrors` runs three steps and stops at the first step that produces errors:

1. Property attributes (`Required` only, unless `validateAllProperties` is true).
2. Validation attributes on the type, from `ValidationAttributeStore.GetTypeValidationAttributes`.
3. If the instance is `IValidatableObject`, `validatable.Validate(validationContext)`.

([Validator.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs))

Steps 1 and 2 are why the method is `RequiresUnreferencedCode`. The `is IValidatableObject` test is an ordinary cast. The ASP.NET generator uses the same order: property attributes, then type attributes, then `IValidatableObject`, and it skips later steps when an earlier step fails ([Validation in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/validation?view=aspnetcore-10.0)).

## Source index

- [Understanding trim analysis](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-concepts)
- [IL2026](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-warnings/il2026)
- [IL3050](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3050)
- [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Introduction to AOT warnings](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings)
- [Prepare .NET libraries for trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)
- [Known trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)
- [Compile-time options validation source generation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator)
- [Options pattern in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)
- [SYSLIB1201–1219](https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib1201-1219)
- [What's new in .NET 8 runtime](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/runtime) (options validation source generator)
- [What's new in .NET libraries for .NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries) (AOT-safe `ValidationContext` constructor)
- [What's new in .NET libraries for .NET 11](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/libraries) (async DataAnnotations)
- [Validator.TryValidateObject](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.dataannotations.validator.tryvalidateobject?view=net-10.0)
- [Validation in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/validation?view=aspnetcore-10.0)
- [ASP.NET Core support for Native AOT](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot)
- [Validator.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs)
- [ValidationContext.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationContext.cs)
- [ValidationAttribute.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationAttribute.cs)
- [ValidationAttributeStore.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationAttributeStore.cs)
- [CustomValidationAttribute.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/CustomValidationAttribute.cs)
- [DisplayAttribute.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/DisplayAttribute.cs)
- [LocalizableString.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/LocalizableString.cs)
- [CompareAttribute.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/CompareAttribute.cs)
- [RangeAttribute.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/RangeAttribute.cs)
- [MaxLengthAttribute.cs](https://github.com/dotnet/runtime/blob/5535e31a712343a63f5d7d796cd874e563e5ac14/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/MaxLengthAttribute.cs) (`CountPropertyHelper`)
- [dotnet/runtime#84324](https://github.com/dotnet/runtime/issues/84324)
- [dotnet/runtime#92327](https://github.com/dotnet/runtime/issues/92327)
- [dotnet/runtime#93088](https://github.com/dotnet/runtime/pull/93088)
- [dotnet/runtime#101202](https://github.com/dotnet/runtime/issues/101202)
- [dotnet/runtime#112111](https://github.com/dotnet/runtime/issues/112111)
- [dotnet/aspnetcore#46349](https://github.com/dotnet/aspnetcore/issues/46349)
- [dotnet/aspnetcore#61221](https://github.com/dotnet/aspnetcore/issues/61221)
