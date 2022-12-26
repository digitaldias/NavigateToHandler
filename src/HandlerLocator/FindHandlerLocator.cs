using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace HandlerLocator
{
    public sealed class FindHandlerLocator
    {
        private readonly Solution _solution;
        private readonly Document _workingDocument;
        private readonly string _activeDocumentPath;
        private readonly int _linePosition;

        public FindHandlerLocator(Solution solution, Document workingDocument, string activeDocumentPath, int linePosition)
        {
            _solution = solution;
            _workingDocument = workingDocument;
            _activeDocumentPath = activeDocumentPath;
            _linePosition = linePosition;
        }

        public async Task<IdentifiedHandler> FindFirstHandler()
        {
            var syntaxRoot = await _workingDocument.GetSyntaxRootAsync();
            var syntaxNode = syntaxRoot.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(_linePosition, 0), true, true);

            // Get type information
            var semanticModel = await _workingDocument.GetSemanticModelAsync();

            var symbol = GetTypeInfo(semanticModel, syntaxNode);

            var references = await SymbolFinder.FindReferencesAsync(symbol, _solution);
            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    var tree = await location.Document.GetSyntaxTreeAsync();
                    var root = await tree.GetRootAsync();
                    var allMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                    var publicMethods = allMethods.Where(publicMethod =>
                        publicMethod.Modifiers.Any(modifier => modifier.Text.Equals("public")) &&
                        publicMethod.ParameterList.Parameters.Any(parameter => parameter.ToFullString().Contains(symbol.Name)));

                    if (!publicMethods.Any())
                        continue;

                    return new IdentifiedHandler
                    {
                        SourceFile = location.Document.FilePath,
                        LineNumber = publicMethods.First().SpanStart
                    };
                }
            }

            return null;
        }

        public async Task<IEnumerable<IdentifiedHandler>> FindAllHandlers()
        {
            var allHandlers = new List<IdentifiedHandler>();
            var syntaxRoot = await _workingDocument.GetSyntaxRootAsync();
            var syntaxNode = syntaxRoot.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(_linePosition, 0), true, true);

            // Get type information
            var semanticModel = await _workingDocument.GetSemanticModelAsync();

            var symbol = GetTypeInfo(semanticModel, syntaxNode);

            var references = await SymbolFinder.FindReferencesAsync(symbol, _solution);
            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    var tree = await location.Document.GetSyntaxTreeAsync();
                    var root = await tree.GetRootAsync();
                    var allMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                    var publicMethods = allMethods.Where(publicMethod =>
                        publicMethod.Modifiers.Any(modifier => modifier.Text.Equals("public")) &&
                        publicMethod.ParameterList.Parameters.Any(parameter => parameter.ToFullString().Contains(symbol.Name)));

                    if (!publicMethods.Any())
                        continue;

                    allHandlers.Add(new IdentifiedHandler
                    {
                        SourceFile = location.Document.FilePath,
                        LineNumber = publicMethods.First().SpanStart
                    });
                }
            }

            return allHandlers;
        }

        private ITypeSymbol GetTypeInfo(SemanticModel semanticModel, SyntaxNode syntaxNode)
        {
            if (syntaxNode is VariableDeclaratorSyntax variableDeclarator)
            {
                return semanticModel.GetTypeInfo(((VariableDeclarationSyntax) variableDeclarator.Parent).Type).Type;
            }

            if (syntaxNode is IdentifierNameSyntax identifierName)
            {
                return semanticModel.GetTypeInfo(identifierName.Parent) is TypeInfo typeInfo && typeInfo.Type is null
                    ? semanticModel.GetTypeInfo(syntaxNode).Type
                    : typeInfo.Type;
            }

            if (syntaxNode is TypeDeclarationSyntax typeDeclarationSyntax)
            {
                return semanticModel.GetDeclaredSymbol(typeDeclarationSyntax);
            }

            if (syntaxNode is ConstructorDeclarationSyntax constructorDeclarationSyntax)
            {
                return semanticModel.GetDeclaredSymbol(constructorDeclarationSyntax).ContainingType;
            }

            return semanticModel.GetTypeInfo(syntaxNode).Type;
        }
    }
}