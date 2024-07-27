using System.Collections.Concurrent;
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
            var allHandlers = new ConcurrentBag<IdentifiedHandler>();
            if (_workingDocument is null)
            {
                return new List<IdentifiedHandler>();
            }
            SyntaxNode syntaxRoot = await _workingDocument.GetSyntaxRootAsync();
            SyntaxNode syntaxNode = syntaxRoot.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(_linePosition, 0), true, true);

            // Get candidateType information
            SemanticModel semanticModel = await _workingDocument.GetSemanticModelAsync();

            ITypeSymbol symbol = GetTypeInfo(semanticModel, syntaxNode);
            if (symbol is null)
                return allHandlers;

            Parallel.ForEach(_solution.Projects, project =>
            {
                Parallel.ForEach(project.Documents, async document => //  (var document in project.Documents)
                {
                    SyntaxNode root = await document.GetSyntaxRootAsync();
                    SemanticModel model = await document.GetSemanticModelAsync();
                    var methodDeclarations = root.DescendantNodes()
                                                 .OfType<MethodDeclarationSyntax>();

                    Parallel.ForEach(methodDeclarations, method =>
                    {
                        var parameters = method.ParameterList.Parameters;
                        foreach (var parameter in parameters)
                        {
                            var parameterType = model.GetTypeInfo(parameter.Type).Type;

                            if (parameterType != null && IsSymbolMatch(symbol, parameterType))
                            {
                                var accessibility = method.Modifiers;
                                if (accessibility.Any(SyntaxKind.PublicKeyword) || accessibility.Any(SyntaxKind.ProtectedKeyword))
                                {
                                    var lineSpan = method.SyntaxTree.GetLineSpan(method.Span);
                                    var classDeclaration = method.AncestorsAndSelf()
                                    .OfType<ClassDeclarationSyntax>()
                                    .FirstOrDefault();

                                    // Add method to the handlers list
                                    var identifiedHandler = new IdentifiedHandler
                                    {
                                        TypeToFind = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                        ClassName = classDeclaration.Identifier.Text ?? "Unknown",
                                        AsArgument = parameterType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                        MethodName = method.Identifier.Text,
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
                    });
                });
            });

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

        private static bool IsSymbolMatch(ITypeSymbol symbol, ITypeSymbol parameterType)
        {
            if (IsInherited(symbol, parameterType))
            {
                return true;
            }

            if (symbol is INamedTypeSymbol subject && parameterType.OriginalDefinition is INamedTypeSymbol item)
            {
                if (AreEqual(subject, item.OriginalDefinition))
                {
                    return true;
                }
            }

            if (AreEqual(symbol.OriginalDefinition, parameterType.OriginalDefinition))
            {
                if (IsGenericType(symbol.OriginalDefinition, out var _) || IsInterface(symbol.OriginalDefinition, out var _))
                {
                    return false;
                }
                return true;
            }

            if (symbol.OriginalDefinition.ToDisplayString() == parameterType.OriginalDefinition.ToDisplayString())
            {
                return true;
            }

            return false;
        }

        private static bool ArgumentsAreTheSame(INamedTypeSymbol left, INamedTypeSymbol right)
        {
            if (left.TypeArguments.Length != right.TypeArguments.Length)
                return false;

            bool AllMatch = true;
            for (int i = 0; i < left.TypeArguments.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(left.TypeArguments[i], right.TypeArguments[i]))
                {
                    AllMatch = false;
                    break;
                }
            }
            return AllMatch;
        }

        private static bool IsGenericType(ITypeSymbol symbol, out INamedTypeSymbol genericSymbol)
        {
            if (symbol is INamedTypeSymbol namedSymbol && namedSymbol.IsGenericType)
            {
                genericSymbol = namedSymbol;
                return true;
            }
            genericSymbol = null;
            return false;
        }

        private static bool IsInterface(ITypeSymbol symbol, out INamedTypeSymbol interfaceSymbol)
        {
            if (symbol is INamedTypeSymbol interfaceToMatch && interfaceToMatch.TypeKind == TypeKind.Interface)
            {
                interfaceSymbol = interfaceToMatch;
                return true;
            }
            interfaceSymbol = null;
            return false;
        }

        private static bool IsInherited(ITypeSymbol typeToMatch, ITypeSymbol candidateType)
        {
            // We are looking at a Generic candidateType
            if (IsGenericType(typeToMatch, out var genericTypeToMatch))
            {
                if (AreEqual(typeToMatch.OriginalDefinition, candidateType.OriginalDefinition))
                {
                    if (IsGenericType(candidateType, out var genericCandidateType))
                    {
                        if (ArgumentsAreTheSame(genericTypeToMatch, genericCandidateType))
                        {
                            return true;
                        }
                        else if (SymbolEqualityComparer.Default.Equals(genericTypeToMatch, genericCandidateType))
                        {
                            if (ArgumentsAreTheSame(genericTypeToMatch, genericCandidateType))
                            {
                                return true;
                            }
                            return false;
                        }
                    }
                }
            }

            // check if we're matching against an interface
            if (IsInterface(typeToMatch, out var interfaceToMatch) && IsInterface(candidateType, out var candidateInterface))
            {
                // Check the tree on the candidate
                if (ImplementsInterface(typeToMatch, candidateType))
                {
                    return ArgumentsAreTheSame(interfaceToMatch, candidateInterface);
                }
                else if (AreEqual(interfaceToMatch.OriginalDefinition, candidateInterface.OriginalDefinition))
                {
                    for (int i = 0; i < candidateInterface.AllInterfaces.Length; i++)
                    {
                        if (AreEqual(candidateInterface, interfaceToMatch))
                        {
                            if (ArgumentsAreTheSame(candidateInterface, interfaceToMatch))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            if (typeToMatch.Interfaces.Length > 0 && IsInterface(candidateType, out var ci))
            {
                for (int i = 0; i < typeToMatch.Interfaces.Length; i++)
                {
                    if (AreEqual(typeToMatch.Interfaces[i].OriginalDefinition, ci.OriginalDefinition))
                    {
                        return true;
                    }
                }
            }

            if (typeToMatch is INamedTypeSymbol symbolToMatch && symbolToMatch.TypeKind is TypeKind.Class)
            {
                if (HasGenericArguments(symbolToMatch) && candidateType is INamedTypeSymbol strangeType)
                {
                    if (AreEqual(symbolToMatch.OriginalDefinition, strangeType.OriginalDefinition))
                    {
                        return ArgumentsAreTheSame(symbolToMatch, strangeType);
                    }
                }
                var baseType = typeToMatch.BaseType;
                while (baseType != null)
                {
                    if (baseType.SpecialType == SpecialType.System_Object)
                    {
                        return false;
                    }

                    if (SymbolEqualityComparer.Default.Equals(baseType, candidateType))
                    {
                        return true;
                    }

                    if (SymbolEqualityComparer.Default.Equals(baseType, candidateType.OriginalDefinition))
                    {
                        return true;
                    }

                    baseType = baseType.BaseType;
                    if (baseType != null)
                    {
                        if ((baseType.TypeKind != TypeKind.Class && baseType.TypeKind != TypeKind.Interface) || baseType.BaseType == null)
                        {
                            break;
                        }
                    }
                }
            }
            return false;
        }

        private static bool AreEqual(INamedTypeSymbol left, INamedTypeSymbol right)
        {
            if (left == null || right == null)
            {
                return false;
            }
            return SymbolEqualityComparer.Default.Equals(left.WithNullableAnnotation(NullableAnnotation.NotAnnotated), right.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
        }

        private static bool AreEqual(ITypeSymbol left, ITypeSymbol right)
        {
            if (left is null || right is null)
            {
                return false;
            }

            var nonNullableLeft = left.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            var nonNullableRight = right.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

            return SymbolEqualityComparer.Default.Equals(x: nonNullableLeft, y: nonNullableRight);
        }

        private static bool HasGenericArguments(INamedTypeSymbol symbol)
            => symbol.IsGenericType && symbol.TypeArguments.Length > 0;

        private static bool ImplementsInterface(ITypeSymbol symbol, ITypeSymbol type)
        {
            if (IsInterface(type, out var candidate))
            {
                if (AreEqual(symbol, candidate))
                {
                    return true;
                }
                if (AreEqual(symbol.OriginalDefinition, candidate))
                {

                }
                if (AreEqual(symbol.OriginalDefinition, candidate.OriginalDefinition))
                {
                    // If both are generic, compare candidateType arguments
                    if (symbol is INamedTypeSymbol namedSymbol && candidate is INamedTypeSymbol namedIface)
                    {
                        if (namedSymbol.TypeArguments.Length == namedIface.TypeArguments.Length)
                        {
                            for (int i = 0; i < namedSymbol.TypeArguments.Length; i++)
                            {
                                if (symbol.TypeKind == TypeKind.Interface)
                                {
                                    return AreEqual(symbol.OriginalDefinition, candidate.OriginalDefinition);
                                }

                                if (!ArgumentsAreTheSame(namedSymbol, namedIface))
                                {
                                    return false;
                                }
                            }
                            return true;
                        }
                    }
                    else
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}