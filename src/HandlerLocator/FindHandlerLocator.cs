using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HandlerLocator
{
    public sealed class FindHandlerLocator
    {
        private readonly Solution _solution;
        private readonly Document _workingDocument;
        private readonly int _linePosition;

        public FindHandlerLocator(Solution solution, Document workingDocument, int linePosition)
        {
            _solution = solution;
            _workingDocument = workingDocument;
            _linePosition = linePosition;
        }

        public async Task<IEnumerable<IdentifiedHandler>> FindAllHandlers()
        {
            List<IdentifiedHandler> allHandlers = new List<IdentifiedHandler>();
            if (_workingDocument is null)
            {
                return new List<IdentifiedHandler>();
            }
            SyntaxNode syntaxRoot = await _workingDocument.GetSyntaxRootAsync();
            SyntaxNode syntaxNode = syntaxRoot.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(_linePosition, 0), true, true);

            // Get type information
            SemanticModel semanticModel = await _workingDocument.GetSemanticModelAsync();

            ITypeSymbol symbol = GetTypeInfo(semanticModel, syntaxNode);
            if (symbol is null)
                return allHandlers;

            var symbolName = symbol.ToDisplayString();

            foreach (var project in _solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    SyntaxNode root = await document.GetSyntaxRootAsync();
                    SemanticModel model = await document.GetSemanticModelAsync();
                    var methodDeclarations = root.DescendantNodes()
                                                 .OfType<MethodDeclarationSyntax>();

                    foreach (var method in methodDeclarations)
                    {

                        var parameters = method.ParameterList.Parameters;
                        foreach (var parameter in parameters)
                        {
                            var parameterType = model.GetTypeInfo(parameter.Type).Type;

                            if (parameterType != null && IsSymbolMatch(symbol, parameterType))
                            {
                                var accessibility = method.Modifiers;
                                if (accessibility.Any(SyntaxKind.PublicKeyword) ||
                                    accessibility.Any(SyntaxKind.ProtectedKeyword))
                                {
                                    var lineSpan = method.SyntaxTree.GetLineSpan(method.Span);
                                    var argName = parameterType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

                                    // Add method to the handlers list
                                    var identifiedHandler = new IdentifiedHandler
                                    {
                                        TypeToFind = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                        TypeName = document.Name.Replace(".cs", ""),
                                        AsArgument = argName,
                                        PublicMethod = method.Identifier.ToFullString(),
                                        SourceFile = document.FilePath,
                                        DisplaySourceFile = $"{document.FilePath}({lineSpan.StartLinePosition.Line + 1},{lineSpan.StartLinePosition.Character + 1})",
                                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                                        Column = lineSpan.StartLinePosition.Character + 1,
                                        CaretPosition = method.Span.Start
                                    };
                                    allHandlers.Add(identifiedHandler);
                                }
                            }
                        }
                    }
                }
            }

            return allHandlers;
        }

        private ITypeSymbol GetTypeInfo(SemanticModel semanticModel, SyntaxNode syntaxNode)
        {
            if (syntaxNode is IdentifierNameSyntax identifierName)
            {
                TypeInfo foundType = semanticModel.GetTypeInfo(syntaxNode);
                if (foundType.Type != null)
                    return foundType.Type;

                return semanticModel.GetTypeInfo(identifierName.Parent) is TypeInfo typeInfo && typeInfo.Type is null
                    ? semanticModel.GetTypeInfo(syntaxNode).Type
                    : typeInfo.Type;
            }

            if (syntaxNode is EnumDeclarationSyntax enumDeclaration)
            {
                return semanticModel.GetDeclaredSymbol(enumDeclaration);
            }

            if (syntaxNode is VariableDeclaratorSyntax variableDeclarator)
            {
                return semanticModel.GetTypeInfo(((VariableDeclarationSyntax)variableDeclarator.Parent).Type).Type;
            }

            if (syntaxNode is TypeDeclarationSyntax typeDeclarationSyntax)
            {
                return semanticModel.GetDeclaredSymbol(typeDeclarationSyntax);
            }

            if (syntaxNode is ConstructorDeclarationSyntax constructorDeclarationSyntax)
            {
                return semanticModel.GetDeclaredSymbol(constructorDeclarationSyntax).ContainingType;
            }

            if (syntaxNode is ParameterSyntax parameterSyntax)
            {
                ITypeSymbol typeInfo = semanticModel.GetDeclaredSymbol(parameterSyntax).Type;
                return typeInfo;
            }

            return semanticModel.GetTypeInfo(syntaxNode).Type;
        }

        private bool IsSymbolMatch(ITypeSymbol symbol, ITypeSymbol parameterType)
        {
            if (ImplementsInterface(symbol, parameterType) || IsInherited(symbol, parameterType))
            {
                return true;
            }

            if (SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, parameterType.OriginalDefinition))
            {
                return true;
            }

            if (symbol.OriginalDefinition.ToDisplayString() == parameterType.OriginalDefinition.ToDisplayString())
            {
                return true;
            }

            // Check if parameterType is a generic type and compare type arguments
            if (parameterType is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType)
            {
                foreach (var typeArgument in namedTypeSymbol.TypeArguments)
                {
                    if (IsSymbolMatch(symbol, typeArgument))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsInherited(ITypeSymbol symbol, ITypeSymbol type)
        {
            ITypeSymbol current = type;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, current.OriginalDefinition))
                {
                    return true;
                }
                current = current.BaseType;
            }
            return false;
        }

        private bool ImplementsInterface(ITypeSymbol symbol, ITypeSymbol type)
        {
            foreach (var iface in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, iface.OriginalDefinition))
                {
                    return true;
                }
            }
            return false;
        }

        private ITypeSymbol GetTypeInfo_Old(SemanticModel semanticModel, SyntaxNode syntaxNode)
        {
            if (syntaxNode is VariableDeclaratorSyntax variableDeclarator)
            {
                return semanticModel.GetTypeInfo(((VariableDeclarationSyntax)variableDeclarator.Parent).Type).Type;
            }

            if (syntaxNode is IdentifierNameSyntax identifierName)
            {
                TypeInfo foundType = semanticModel.GetTypeInfo(syntaxNode);
                if (foundType.Type != null)
                    return foundType.Type;

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

            if (syntaxNode is ParameterSyntax parameterSyntax)
            {
                ITypeSymbol typeInfo = semanticModel.GetDeclaredSymbol(parameterSyntax).Type;
                return typeInfo;
            }

            return semanticModel.GetTypeInfo(syntaxNode).Type;
        }
    }
}