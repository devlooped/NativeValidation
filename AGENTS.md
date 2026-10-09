# NativeAnnotations

AOT-safe `System.ComponentModel.DataAnnotations` validation, extracted from the Stunts data-annotation behavior so it can run without a proxy pipeline.

## API

Namespace `Devlooped`.

- `NativeValidator` is the caller-facing surface, shaped like `System.ComponentModel.DataAnnotations.Validator`. `TryValidateObject` / `ValidateObject` and `TryValidateProperty` / `ValidateProperty` use a `ValidationContext`. `Try*` returns bool and appends `ValidationResult`s. `Validate*` throws `ValidationException` for the first failure. `TryValidateObject` defaults to required properties only.
- The per-check methods (`Required`, `StringLength`, and the rest) are the `IsValid` layer. A bad value returns `ValidationResult`, including a value of the wrong type. Malformed bounds still throw `InvalidOperationException`.
- `TryValidate` / `Validate(instance, method, arguments)` run the rules registered for a method or constructor. `Validator` has no equivalent.
- `NativeValidation.Register`, `RegisterProperties`, and `RegisterType` are the generator entries. Lookups key a member by declaring type, metadata name (`set_Email`, `Save`, `.ctor`), and `NativeValidationSignature`. Property getters are emitted so object validation does not reflect.
- `ValidatedAttribute` on a generic factory, and `[assembly: Validate<T>]`, close open generic targets the syntax walk cannot see.

## Generator

`NativeAnnotations.Generator` ships inside the package at `analyzers/dotnet/cs`. It emits `Devlooped.Generated.NativeValidationRegistrations` when the compilation can see `Devlooped.NativeValidation`.

Supported attributes match the built-in set enforced by `NativeValidator`, including `CustomValidationAttribute` as a direct static call. Any other `ValidationAttribute` subclass warns as `NVA001` and is not enforced. `NVA002` reports a supported attribute the generator had to skip.

Instance properties (via the setter), methods, and constructors are registered, including non-virtual members. Static members, `out` parameters, ref structs, and pointers are skipped.

## Tests

`src/Tests` references the generator as an analyzer, so runtime tests execute the module initializer. Closure tests drive `NativeValidationGenerator` against an in-memory compilation.

`src/Sample` is an AOT-compatible console app with a large annotated order model. Its build runs the trim and AOT analyzers over the generated registrations.
