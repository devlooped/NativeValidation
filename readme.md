![Icon](img/icon-32.png) NativeValidation
============

[![Version](https://img.shields.io/nuget/vpre/Devlooped.DataAnnotations.NativeValidation.svg?color=royalblue)](https://www.nuget.org/packages/Devlooped.DataAnnotations.NativeValidation)
[![Downloads](https://img.shields.io/nuget/dt/Devlooped.DataAnnotations.NativeValidation.svg?color=darkmagenta)](https://www.nuget.org/packages/Devlooped.DataAnnotations.NativeValidation)
[![EULA](https://img.shields.io/badge/EULA-OSMF-blue?labelColor=black&color=C9FF30)](https://github.com/devlooped/oss/blob/main/osmfeula.txt)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/devlooped/oss/blob/main/license.txt)

<!-- include https://github.com/devlooped/.github/raw/main/osmf.md -->
## Open Source Maintenance Fee

To ensure the long-term sustainability of this project, users of this package who generate 
revenue must pay an [Open Source Maintenance Fee](https://opensourcemaintenancefee.org). 
While the source code is freely available under the terms of the [License](license.txt), 
this package and other aspects of the project require [adherence to the Maintenance Fee](osmfeula.txt).

To pay the Maintenance Fee, [become a Sponsor](https://github.com/sponsors/devlooped) at the proper 
OSMF tier. A single fee covers all of [Devlooped packages](https://www.nuget.org/profiles/Devlooped).

<!-- https://github.com/devlooped/.github/raw/main/osmf.md -->
<!-- #content -->
## Why

`System.ComponentModel.DataAnnotations` finds attributes and property values by reflection. Native AOT and trimming keep a member only when static analysis can see a use, so `Validator` warns on a trimmed or AOT publish and can miss the properties it was meant to check. The framework has no AOT-safe replacement for object validation. The generators that ship with .NET cover options classes, Minimal APIs, and Blazor. A library or console app that validates its own objects sits outside them, and `Validator` has no API for method arguments or constructors.

This package is that layer. The attributes stay on the members. A source generator turns each rule into a direct call with the attribute arguments inlined, so the published app keeps the check, the bounds, the display name, and any custom validator method.

## What

`Devlooped.DataAnnotations.NativeValidation` is AOT-safe validation for `System.ComponentModel.DataAnnotations` attributes. Reference the package. The APIs live in the `Devlooped.DataAnnotations` namespace. A source generator reads the attributes during compilation, emits the checks, and registers them from a module initializer. Generated code does not instantiate validation attributes and does not reflect over members.

`NativeValidator` follows `Validator`:

- `TryValidateObject` / `ValidateObject` and `TryValidateProperty` / `ValidateProperty` take a `ValidationContext`. `Try*` returns false and appends each `ValidationResult`. `Validate*` throws `ValidationException` for the first failure. A null results collection stops at the first failure. `TryValidateObject` without the bool checks required properties only.
- `TryValidate` / `Validate(instance, method, arguments)` run the rules registered for a method or constructor. Pass the `MethodBase` and the arguments in parameter order. A member with no registered rules succeeds.
- `Required`, `StringLength`, and the other per-check methods are the `IsValid` layer. A bad value returns `ValidationResult`, including a value of the wrong type. Malformed bounds throw `InvalidOperationException`.

Property failures skip type-level rules and `IValidatableObject`. Object validation is not recursive, matching `Validator`. Walk nested objects in the caller.

Pass a display name into `ValidationContext`. The overloads that omit it are the ones marked `RequiresUnreferencedCode`. Built-in checks use the display name the generator copied from `[Display(Name = ...)]` or `[DisplayName]`.

```csharp
using Devlooped.DataAnnotations;

public class Account
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email address")]
    public string? Email { get; set; }

    public void Rename([Required, StringLength(8, MinimumLength = 2)] string? name, [Range(1, 10)] int attempt) { }
}

var results = new List<ValidationResult>();
var context = new ValidationContext(account, "Account", null, null);
var valid = NativeValidator.TryValidateObject(account, context, results, validateAllProperties: true);

NativeValidator.ValidateProperty(
    " ",
    new ValidationContext(account, "Email address", null, null) { MemberName = nameof(Account.Email) });

var rename = typeof(Account).GetMethod(nameof(Account.Rename))!;
NativeValidator.TryValidate(account, rename, results, "ada", 2);
NativeValidator.Validate(account, rename, "a", 1);
```

`Type.GetMethod` has its own trim annotation. Object and property validation do not need it. Argument validation matches the `MethodBase` you already hold.

The same checks are available when the attribute arguments are already in hand:

```csharp
NativeValidator.Required(value, "Email", "Email address", allowEmptyStrings: false, errorMessage: null);
```

`[MinLength]`, `[MaxLength]`, `[Length]`, `[Range(typeof(T), ...)]`, and `[Compare]` still reference constructors the BCL marks `RequiresUnreferencedCode`, so the trim analyzer warns at the declaration. The generator reads those arguments at compile time and never runs the constructors. Suppress IL2026 on the model when every check goes through `NativeValidator`. The [sample order](https://github.com/devlooped/NativeValidation/blob/main/src/Sample/Order.cs) does this. `src/Sample` publishes with `PublishAot`, and its build runs the trim and AOT analyzers over the generated registrations.

## How

`NativeValidation.Generator` ships in the package at `analyzers/dotnet/cs`. It emits `Devlooped.Generated.NativeValidationRegistrations` when the compilation can see `Devlooped.DataAnnotations.NativeValidation`. A module initializer calls one register method per closed type.

Each annotated member is stored under its declaring type, metadata name (`set_Email`, `Rename`, `.ctor`), and [`NativeValidationSignature`](https://github.com/devlooped/NativeValidation/blob/main/src/NativeValidation/NativeValidationSignature.cs) (comma-separated parameter types). Object validation uses a second table. `RegisterProperties` stores the property name, type, and a getter lambda, so the value is read by a direct property access. Type-level attributes go through `RegisterType`. Interface implementations and overrides contribute attributes declared on the interface or base member. `[MetadataType]` buddy classes are included. After the generated rules pass, `IValidatableObject.Validate` is an interface call.

The enforced attributes are `Required`, `StringLength`, `MinLength`, `MaxLength`, `Length`, `Range`, `RegularExpression`, `Compare`, `EmailAddress`, `Phone`, `CreditCard`, `Url`, `EnumDataType`, `AllowedValues`, `DeniedValues`, `Base64String`, `FileExtensions`, and `CustomValidation`.

Each one becomes a call into `NativeValidator` with the arguments inlined:

- Length checks take a `Count` lambda when the value type has a public `Count` property. `string` and `ICollection` use their own length.
- `Range(typeof(T), minimum, maximum)` parses with `TryParse` on the closed operand type, including `DateOnly` and enums.
- `Compare` reads the other property with a direct cast to its declaring type.
- `RegularExpression` compiles one static `Regex` for the pattern and timeout.
- `CustomValidation` calls the public static method directly and builds a trim-safe `ValidationContext` when that method takes one.
- `[Display(Name = ...)]` and `[DisplayName]` are copied into the call. `ErrorMessage` is copied as a string. `ErrorMessageResourceName` reads that public static string property.

`[DataType]` is a UI hint and is not a check. `EmailAddress`, `Phone`, `CreditCard`, and `Url` are.

Any other `ValidationAttribute` subclass warns as **NVA001** and is not enforced. Virtual `IsValid` is not itself trim-annotated. The generator inlines the built-in checks whose implementations reflect, and it does not construct an arbitrary attribute to call `IsValid`. **NVA002** reports a supported attribute the generator had to skip: a `Range` with no closed operand type, a `CustomValidation` method that is not a single public static `ValidationResult` method, a resource property it cannot read, an `out` parameter, a ref struct, a pointer, and the same class of problem.

Static members are skipped. Instance properties are registered through the setter, including non-virtual members. Methods and constructors are registered the same way.

### Open generics

The options generator reports `SYSLIB1201` for open generic member types. ASP.NET's generator includes a type it cannot see from a signature only when that type carries `[ValidatableType]`. This generator registers a type when an annotated member's declaring type does not depend on a type parameter. Members declared on open `Box<T>` are skipped, because that declaration has no closed type to emit. Naming `Box<int>` in ordinary source, including `new Box<int>()`, does not register it either. A closed type that appears only at run time (`MakeGenericType`, or a factory the walk cannot see) is the same gap.

[`Validated`](https://github.com/devlooped/NativeValidation/blob/main/src/NativeValidation/ValidateAttribute.cs) on a generic factory registers every closed type argument and the closed return type at each call site in the compilation. `Create<int>()` on `[Validated] static Box<T> Create<T>()` registers `Box<int>`. A wrapper that only writes `Create<T>()` does not close `T`. Mark the wrapper `[Validated]` as well. The generator substitutes call-site arguments through `[Validated]` methods, up to eight levels, and registers the constructed return type plus its base types and interfaces.

```csharp
public class Box<T>
{
    [Required]
    public T? Value { get; set; }
}

public static class Boxes
{
    [Validated]
    public static Box<T> Create<T>() => new();

    [Validated]
    public static object Make<T>() => Create<T>();
}

// Make<int>() registers Box<int>.
```

`[assembly: Validate<T>]` registers one closed type when no factory call in the compilation closes it. Repeat the attribute for each closed type. Use it when the closed type is only built from a run-time `Type`.

```csharp
[assembly: Validate<Box<string>>]
```

## .NET DataAnnotations Issues

What follows is the state of the art in .NET as of October 9, 2026.

[`Validator`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs) discovers attributes and properties by reflection. Native AOT and trimming keep a member only when static analysis can see a use, so the BCL marks those entry points [`RequiresUnreferencedCode`](https://learn.microsoft.com/dotnet/core/deploying/trimming/trimming-concepts#understanding-requiresunreferencedcode). Calling them in a trimmed or AOT publish reports [IL2026](https://learn.microsoft.com/dotnet/core/deploying/trimming/trim-warnings/il2026).

`TryValidateProperty` is annotated *"The Type of validationContext.ObjectType cannot be statically discovered."* `TryValidateObject` carries the same annotation as the [`ValidationContext`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationContext.cs) constructors that omit a display name:

> Constructing a ValidationContext without a display name is not trim-safe because it uses reflection to discover the type of the instance being validated in order to resolve the DisplayNameAttribute when a display name is not provided.

The constructor that takes the display name is trim-safe. .NET 10 added that overload so a caller can name the display string without the warning. Attribute discovery is a separate path. [`ValidationAttributeStore`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationAttributeStore.cs) loads properties with `TypeDescriptor.GetProperties` and attributes with `TypeDescriptor.GetAttributes`. `TypeDescriptor.GetProperties` is annotated because a `PropertyDescriptor`'s `PropertyType` cannot be statically discovered, and after trimming that call can return no properties ([dotnet/runtime#101202](https://github.com/dotnet/runtime/issues/101202)). [`TryValidateObject`](https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validator.tryvalidateobject?view=net-10.0) on the .NET 10 API is still `RequiresUnreferencedCode`. The BCL has no AOT-safe replacement for it. A shared generator on `System.ComponentModel.Annotations` was [discussed in 2024](https://github.com/dotnet/aspnetcore/issues/46349) and was not a committed design.

Several attribute constructors are trim-unsafe on their own, so the warning appears on the model even when application code never calls `Validator`:

- [`MinLengthAttribute`](https://github.com/dotnet/runtime/issues/112111), `MaxLengthAttribute`, and `LengthAttribute` reflect for a `Count` property on types that do not implement `ICollection`. Trimming can remove that property.
- [`RangeAttribute(Type, string, string)`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/RangeAttribute.cs) looks up a `TypeConverter`. Its annotation calls out generic converters such as `NullableConverter`, which need `DynamicallyAccessedMembers`.
- [`CompareAttribute`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/CompareAttribute.cs) is annotated *"The property referenced by 'otherProperty' may be trimmed."* `IsValid` calls `GetRuntimeProperty` and `GetValue`.

[`CustomValidationAttribute`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/CustomValidationAttribute.cs) keeps the validator type's public methods through `DynamicallyAccessedMembers`, then `IsValid` finds the method with `GetMethods` and calls it through `MethodInfo.Invoke`.

The platform generators cover the hosts that own them. The [options validation generator](https://learn.microsoft.com/dotnet/core/extensions/options-validation-generator) emits `IValidateOptions<T>` for a partial class marked `[OptionsValidator]`. The generated `Validate` method still calls `Validator.TryValidateValue` and `new ValidationContext`, and it replaces `Range`, `MinLength`, `MaxLength`, and `Length` with generated attribute subclasses because those types rely on reflection. It suppresses IL2026 on the context with the justification that the object never takes the reflection path. [ASP.NET Core validation](https://learn.microsoft.com/aspnet/core/fundamentals/validation) generates metadata for Minimal APIs and Blazor in the assembly that calls `AddValidation`. It does not cover MVC or Razor Pages. Without that metadata, Blazor's `DataAnnotationsValidator` falls back to `Validator`, and that fallback checks top-level properties only. The attributes those generators still consume are the open gap: [not every `ValidationAttribute` is Native AOT compatible](https://github.com/dotnet/aspnetcore/issues/61221). [Library trimming guidance](https://learn.microsoft.com/dotnet/core/deploying/trimming/prepare-libraries-for-trimming) treats this class of API the way it treats reflection-based serializers: keep the declarative surface, and generate the code that runs.

A Native AOT library or console app that validates its own objects, method arguments, or constructors sits outside those generators. `Validator` has no method or constructor API. This package is that layer. The attributes stay on the members. Each check becomes a direct call with the attribute arguments inlined, so the trimmer sees the check, the bounds, the display name, and the custom validator method.

<!-- #content -->
---
<!-- include https://raw.githubusercontent.com/devlooped/sponsors/main/footer.md -->
# Sponsors 

<!-- sponsors.md -->
[![Clarius Org](https://avatars.githubusercontent.com/u/71888636?v=4&s=39 "Clarius Org")](https://github.com/clarius)
[![MFB Technologies, Inc.](https://avatars.githubusercontent.com/u/87181630?v=4&s=39 "MFB Technologies, Inc.")](https://github.com/MFB-Technologies-Inc)
[![SandRock](https://avatars.githubusercontent.com/u/321868?u=99e50a714276c43ae820632f1da88cb71632ec97&v=4&s=39 "SandRock")](https://github.com/sandrock)
[![DRIVE.NET, Inc.](https://avatars.githubusercontent.com/u/15047123?v=4&s=39 "DRIVE.NET, Inc.")](https://github.com/drivenet)
[![Keith Pickford](https://avatars.githubusercontent.com/u/16598898?u=64416b80caf7092a885f60bb31612270bffc9598&v=4&s=39 "Keith Pickford")](https://github.com/Keflon)
[![Thomas Bolon](https://avatars.githubusercontent.com/u/127185?u=7f50babfc888675e37feb80851a4e9708f573386&v=4&s=39 "Thomas Bolon")](https://github.com/tbolon)
[![Reuben Swartz](https://avatars.githubusercontent.com/u/724704?u=2076fe336f9f6ad678009f1595cbea434b0c5a41&v=4&s=39 "Reuben Swartz")](https://github.com/rbnswartz)
[![Jacob Foshee](https://avatars.githubusercontent.com/u/480334?v=4&s=39 "Jacob Foshee")](https://github.com/jfoshee)
[![](https://avatars.githubusercontent.com/u/33566379?u=bf62e2b46435a267fa246a64537870fd2449410f&v=4&s=39 "")](https://github.com/Mrxx99)
[![Eric Johnson](https://avatars.githubusercontent.com/u/26369281?u=41b560c2bc493149b32d384b960e0948c78767ab&v=4&s=39 "Eric Johnson")](https://github.com/eajhnsn1)
[![Jonathan ](https://avatars.githubusercontent.com/u/5510103?u=98dcfbef3f32de629d30f1f418a095bf09e14891&v=4&s=39 "Jonathan ")](https://github.com/Jonathan-Hickey)
[![Ken Bonny](https://avatars.githubusercontent.com/u/6417376?u=569af445b6f387917029ffb5129e9cf9f6f68421&v=4&s=39 "Ken Bonny")](https://github.com/KenBonny)
[![Simon Cropp](https://avatars.githubusercontent.com/u/122666?v=4&s=39 "Simon Cropp")](https://github.com/SimonCropp)
[![agileworks-eu](https://avatars.githubusercontent.com/u/5989304?v=4&s=39 "agileworks-eu")](https://github.com/agileworks-eu)
[![Zheyu Shen](https://avatars.githubusercontent.com/u/4067473?v=4&s=39 "Zheyu Shen")](https://github.com/arsdragonfly)
[![Vezel](https://avatars.githubusercontent.com/u/87844133?v=4&s=39 "Vezel")](https://github.com/vezel-dev)
[![ChilliCream](https://avatars.githubusercontent.com/u/16239022?v=4&s=39 "ChilliCream")](https://github.com/ChilliCream)
[![4OTC](https://avatars.githubusercontent.com/u/68428092?v=4&s=39 "4OTC")](https://github.com/4OTC)
[![domischell](https://avatars.githubusercontent.com/u/66068846?u=0a5c5e2e7d90f15ea657bc660f175605935c5bea&v=4&s=39 "domischell")](https://github.com/DominicSchell)
[![Adrian Alonso](https://avatars.githubusercontent.com/u/2027083?u=129cf516d99f5cb2fd0f4a0787a069f3446b7522&v=4&s=39 "Adrian Alonso")](https://github.com/adalon)
[![torutek](https://avatars.githubusercontent.com/u/33917059?v=4&s=39 "torutek")](https://github.com/torutek)
[![Ryan McCaffery](https://avatars.githubusercontent.com/u/16667079?u=c0daa64bb5c1b572130e05ae2b6f609ecc912d4d&v=4&s=39 "Ryan McCaffery")](https://github.com/mccaffers)
[![Seika Logiciel](https://avatars.githubusercontent.com/u/2564602?v=4&s=39 "Seika Logiciel")](https://github.com/SeikaLogiciel)
[![Andrew Grant](https://avatars.githubusercontent.com/devlooped-user?s=39 "Andrew Grant")](https://github.com/wizardness)
[![eska-gmbh](https://avatars.githubusercontent.com/devlooped-team?s=39 "eska-gmbh")](https://github.com/eska-gmbh)
[![Geodata AS](https://avatars.githubusercontent.com/u/5946299?v=4&s=39 "Geodata AS")](https://github.com/geodata-no)


<!-- sponsors.md -->
[![Sponsor this project](https://avatars.githubusercontent.com/devlooped-sponsor?s=118 "Sponsor this project")](https://github.com/sponsors/devlooped)

[Learn more about GitHub Sponsors](https://github.com/sponsors)

<!-- https://raw.githubusercontent.com/devlooped/sponsors/main/footer.md -->
