using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HandlerLocator.Tests
{
    internal static class CompilationHelper
    {
        public static CSharpCompilation Compile(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var references = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            };
            return CSharpCompilation.Create(
                "TestAssembly",
                new[] { tree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        public static INamedTypeSymbol GetType(this CSharpCompilation compilation, string metadataName)
        {
            var type = compilation.GetTypeByMetadataName(metadataName);
            if (type is null)
            {
                throw new System.ArgumentException($"Type '{metadataName}' not found in compiled source.");
            }
            return type;
        }

        public static INamedTypeSymbol GetTypeArgument(this CSharpCompilation compilation, string ownerMetadataName, int index = 0)
        {
            return (INamedTypeSymbol)compilation.GetType(ownerMetadataName).TypeArguments[index];
        }
    }
}
