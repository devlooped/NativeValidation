using Microsoft.CodeAnalysis;

namespace Devlooped.DataAnnotations
{
    /// <summary>
    /// Emits a module initializer that registers data-annotation rules.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class NativeValidationGenerator : IIncrementalGenerator
    {
        /// <inheritdoc/>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(context.CompilationProvider, static (source, compilation) =>
            {
                if (compilation.GetTypeByMetadataName("Devlooped.DataAnnotations.NativeValidation") == null)
                    return;

                NativeValidationCatalog.Emit(source, compilation);
            });
        }
    }
}
