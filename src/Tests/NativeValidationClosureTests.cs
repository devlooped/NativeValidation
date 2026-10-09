using System.ComponentModel.DataAnnotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Devlooped.Tests
{
    public class NativeValidationClosureTests
    {
        [Fact]
        public void AssemblyValidateRegistersAClosedTypeWithNoFactoryCall()
        {
            var source = Generate(@"
[assembly: Devlooped.DataAnnotations.Validate<IOnlyValidated<int>>]
public interface IOnlyValidated<T>
{
    [System.ComponentModel.DataAnnotations.Required]
    T Value { get; set; }
}
");

            Assert.Contains("IOnlyValidated<int>", source);
            Assert.DoesNotContain("IOnlyValidated<string>", source);
        }

        [Fact]
        public void ValidatedFactoryRegistersTheClosedReturnType()
        {
            var source = Generate(@"
public interface ILabeled<T>
{
    [System.ComponentModel.DataAnnotations.Required]
    T Label { get; set; }
}
public static class Factory
{
    [Devlooped.DataAnnotations.Validated]
    public static ILabeled<T> Labeled<T>() => default;
}
class Program
{
    void Use() => Factory.Labeled<int>();
}
");

            Assert.Contains("ILabeled<int>", source);
            Assert.DoesNotContain("ILabeled<string>", source);
        }

        [Fact]
        public void ValidatedWrapperClosesTheFactoryItCalls()
        {
            var source = Generate(@"
public interface ILabeled<T>
{
    [System.ComponentModel.DataAnnotations.Required]
    T Label { get; set; }
}
public static class Factory
{
    [Devlooped.DataAnnotations.Validated]
    public static ILabeled<T> Labeled<T>() => default;

    [Devlooped.DataAnnotations.Validated]
    public static object Make<T>() => Labeled<T>();
}
class Program
{
    void Use() => Factory.Make<string>();
}
");

            Assert.Contains("ILabeled<string>", source);
            Assert.DoesNotContain("ILabeled<int>", source);
        }

        [Fact]
        public void ValidationAttributeOnAGenericMethodClosesTheReturnType()
        {
            var source = Generate(@"
public class ChecksAttribute : System.ComponentModel.DataAnnotations.ValidationAttribute { }
public interface ILabeled<T>
{
    [System.ComponentModel.DataAnnotations.Required]
    T Label { get; set; }
}
public static class Factory
{
    [Checks]
    public static ILabeled<T> Labeled<T>() => default;

    [Checks]
    public static object Make<T>() => Labeled<T>();
}
class Program
{
    void Use() => Factory.Make<int>();
}
");

            Assert.Contains("ILabeled<int>", source);
            Assert.DoesNotContain("ILabeled<string>", source);
        }

        [Fact]
        public void ValidationAttributeOnANonGenericMethodDoesNotCloseTypes()
        {
            var source = Generate(@"
public class ChecksAttribute : System.ComponentModel.DataAnnotations.ValidationAttribute { }
public interface ILabeled<T>
{
    [System.ComponentModel.DataAnnotations.Required]
    T Label { get; set; }
}
public static class Factory
{
    [Checks]
    public static ILabeled<int> Fixed() => default;

    [System.Obsolete]
    public static ILabeled<T> Labeled<T>() => default;
}
class Program
{
    void Use()
    {
        Factory.Fixed();
        Factory.Labeled<string>();
    }
}
");

            Assert.DoesNotContain("ILabeled<", source);
        }

        static string Generate(string source)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(System.IO.Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[]
                {
                    MetadataReference.CreateFromFile(typeof(RequiredAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(ValidateAttribute<>).Assembly.Location)
                });
            var compilation = CSharpCompilation.Create(
                "closure",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new NativeValidationGenerator().AsSourceGenerator());
            driver = driver.RunGenerators(compilation);
            return string.Join("\n", driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).Select(generated => generated.SourceText.ToString()));
        }
    }
}
