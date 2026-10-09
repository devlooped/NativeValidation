![Icon](img/icon-32.png) NativeValidation
============

[![Version](https://img.shields.io/nuget/vpre/Devlooped.DataAnnotations.NativeValidation.svg?color=royalblue)](https://www.nuget.org/packages/Devlooped.DataAnnotations.NativeValidation)
[![Downloads](https://img.shields.io/nuget/dt/Devlooped.DataAnnotations.NativeValidation.svg?color=darkmagenta)](https://www.nuget.org/packages/Devlooped.DataAnnotations.NativeValidation)
[![EULA](https://img.shields.io/badge/EULA-OSMF-blue?labelColor=black&color=C9FF30)](https://github.com/devlooped/oss/blob/main/osmfeula.txt)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/devlooped/oss/blob/main/license.txt)

<!-- include https://github.com/devlooped/.github/raw/main/osmf.md -->
<!-- #content -->
## Usage

Reference the `Devlooped.DataAnnotations.NativeValidation` package. The APIs live in the `Devlooped` namespace. A source generator turns `System.ComponentModel.DataAnnotations` attributes on your members into AOT-safe checks and registers them from a module initializer. The generated code does not instantiate validation attributes and does not reflect over members.

```csharp
public class Account
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email address")]
    public string? Email { get; set; }

    public void Rename([Required, StringLength(8, MinimumLength = 2)] string? name, [Range(1, 10)] int attempt) { }
}

var results = new List<ValidationResult>();
var valid = NativeValidator.TryValidateObject(account, new ValidationContext(account), results, validateAllProperties: true);
NativeValidator.ValidateProperty(" ", new ValidationContext(account) { MemberName = nameof(Account.Email) });
```

`TryValidateObject` and `TryValidateProperty` return `false` and append each `ValidationResult`. The matching `ValidateObject` and `ValidateProperty` methods throw `ValidationException` for the first failure. A null results collection stops at that failure. `TryValidateObject` without the bool checks required properties only, matching `Validator`. The same checks are available directly when you already have the attribute arguments:

```csharp
NativeValidator.Required(value, "Email", "Email address", allowEmptyStrings: false, errorMessage: null);
```

Closed generic types are registered from a `[Validated]` factory call, or with `[assembly: Validate<Box<int>>]` when the closed type is only known at run time.
<!-- #content -->
---
<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->
