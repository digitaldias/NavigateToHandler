using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

            ISymbol symbolDefinition = await SymbolFinder.FindSourceDefinitionAsync(symbol, _solution);

            Parallel.ForEach(_solution.Projects, project =>
            {
                Parallel.ForEach(project.Documents, async document => //  (var document in project.Documents)
                {
                    SyntaxNode root = await document.GetSyntaxRootAsync();
                    SemanticModel model = await document.GetSemanticModelAsync();
                    var methodDeclarations = root.DescendantNodes()
                                                 .OfType<MethodDeclarationSyntax>();

                    Parallel.ForEach(methodDeclarations, async method =>
                    {
                        var parameters = method.ParameterList.Parameters;
                        foreach (var parameter in parameters)
                        {
                            var parameterType = model.GetTypeInfo(parameter.Type).Type;

                            if (parameterType != null && await IsSymbolMatch(symbol, symbolDefinition, parameterType))
                            {
                                var lineSpan = method.SyntaxTree.GetLineSpan(method.Span);
                                var className = "Unknown";
                                var classType = "Unknown";
                                var classDeclaration = method.AncestorsAndSelf()
                                    .OfType<ClassDeclarationSyntax>()
                                    .FirstOrDefault();

                                if (classDeclaration != null)
                                {
                                    className = classDeclaration.Identifier.Text;
                                    classType = "class";
                                }
                                else if (method.Parent is RecordDeclarationSyntax recordClass)
                                {
                                    className = recordClass.Identifier.Text;
                                    classType = "record";
                                }

                                var methodAccess = GetMethodAccess(method);
                                if (methodAccess == "private" || methodAccess == "file" || methodAccess == "unknown")
                                {
                                    continue;
                                }

                                // Add method to the handlers list
                                var identifiedHandler = new IdentifiedHandler
                                {
                                    TypeToFind = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                    ClassName = classDeclaration?.Identifier.Text ?? "Unknown",
                                    ClassType = classType,
                                    AsArgument = GetDisplayNameFor(parameterType),
                                    MethodName = method.Identifier.Text + "(...)",
                                    MethodAccess = methodAccess,
                                    SourceFile = document.FilePath,
                                    DisplaySourceFile = $"{document.FilePath}({lineSpan.StartLinePosition.Line + 1},{lineSpan.StartLinePosition.Character + 1})",
                                    LineNumber = lineSpan.StartLinePosition.Line + 1,
                                    Column = lineSpan.StartLinePosition.Character + 1,
                                    CaretPosition = method.Span.Start
                                };
                                allHandlers.Add(identifiedHandler);
                            }
                        }
                    });
                });
            });

            return allHandlers;
        }

        private static string GetMethodAccess(MethodDeclarationSyntax method)
        {
            var modifiers = method.Modifiers;
            if (modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
            {
                return "public";
            }
            if (modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword)))
            {
                return "protected";
            }
            if (modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword)))
            {
                return "internal";
            }
            if (modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword)))
            {
                return "private";
            }
            if (modifiers.Any(m => m.IsKind(SyntaxKind.FileKeyword)))
            {
                return "file";
            }
            return "unknown";
        }

        private string GetDisplayNameFor(ITypeSymbol parameterSymbol)
        {
            var builder = new StringBuilder(parameterSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            if (parameterSymbol is INamedTypeSymbol symbol)
            {
                AppendBaseTypeDisplay(symbol, builder);
            }
            return builder.ToString();
        }

        private void AppendBaseTypeDisplay(INamedTypeSymbol symbol, StringBuilder builder)
        {
            if (IsBuiltInType(symbol))
            {
                builder.Append(" as ");
                builder.Append(Enum.GetName(typeof(SpecialType), symbol.SpecialType));
            }
            if (symbol.BaseType != null && symbol.BaseType.IsGenericType)
            {
                var baseType = symbol.BaseType;
                builder.Append(" as ");
                AppendTypeDisplay(baseType, builder);
            }
        }

        public static bool IsBuiltInType(ITypeSymbol typeSymbol)
        {
            switch (typeSymbol.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_Char:
                case SpecialType.System_Decimal:
                case SpecialType.System_Double:
                case SpecialType.System_Single:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_String:
                case SpecialType.System_Object:
                    return true;
                case SpecialType.None:
                default:
                    return false;
            }
        }

        private void AppendTypeDisplay(ITypeSymbol typeSymbol, StringBuilder builder)
        {
            builder.Append(typeSymbol.Name);

            if (typeSymbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType)
            {
                builder.Append('<');
                for (int i = 0; i < namedTypeSymbol.TypeArguments.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }
                    AppendTypeDisplay(namedTypeSymbol.TypeArguments[i], builder);
                }
                builder.Append('>');
            }
        }

        private ITypeSymbol GetTypeInfo(SemanticModel semanticModel, SyntaxNode syntaxNode)
        {
            if (syntaxNode is InterfaceDeclarationSyntax interfaceDeclaration)
            {
                return semanticModel.GetDeclaredSymbol(interfaceDeclaration);
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

        private async Task<bool> IsSymbolMatch(ITypeSymbol symbol, ISymbol symbolDefinition, ITypeSymbol parameterType)
        {
            if (IsInherited(symbol, parameterType) && symbol.TypeKind != TypeKind.Interface)
            {
                return true;
            }

            if (symbolDefinition is INamedTypeSymbol subject && await SymbolFinder.FindSourceDefinitionAsync(parameterType, _solution) is INamedTypeSymbol item)
            {
                if (AreEqual(subject, item))
                {
                    return true;
                }
            }

            var left = symbol is INamedTypeSymbol ? symbol as INamedTypeSymbol : null;
            var right = parameterType is INamedTypeSymbol ? parameterType as INamedTypeSymbol : null;

            if (left != null && right != null && left.TypeKind == TypeKind.Interface)
            {
                return ImplementsInterface(left, right);
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

        private static bool IsInherited(ITypeSymbol typeToMatch, ITypeSymbol candidateType)
        {
            if (candidateType.SpecialType != SpecialType.None)
                return false;

            if ((typeToMatch is INamedTypeSymbol) == false || (candidateType is INamedTypeSymbol) == false)
                return false;

            var leftSide = typeToMatch as INamedTypeSymbol;
            var rightSide = candidateType as INamedTypeSymbol;

            if (leftSide.IsGenericType && rightSide.BaseType != null)
            {
                var baseType = rightSide.BaseType;

                if (baseType.IsGenericType)
                {
                    Debug.WriteLine($"Comparing {leftSide.Name} to {baseType.Name}");
                    var argumentsAreTheSame = ArgumentsAreTheSame(leftSide, baseType);
                    if (!argumentsAreTheSame && baseType.BaseType != null)
                    {
                        var testAgainstBaseType = ArgumentsAreTheSame(leftSide, baseType.OriginalDefinition);
                        return testAgainstBaseType;
                    }

                    return argumentsAreTheSame;
                }

                if (AreEqual(leftSide, rightSide))
                    return true;

                if (AreEqual(leftSide, baseType.OriginalDefinition))
                    return true;

                if (AreEqual(leftSide.OriginalDefinition, baseType.OriginalDefinition))
                    return true;
            }

            if (leftSide.TypeKind == TypeKind.Interface && rightSide.AllInterfaces.Any())
            {
                foreach (var iface in rightSide.AllInterfaces)
                {
                    var isMatch = AreEqual(leftSide, iface);
                    if (isMatch)
                    {
                        return ArgumentsAreTheSame(leftSide, iface);
                    }
                }
            }
            return false;
        }

        private static bool AreGenericTypesEqual(INamedTypeSymbol potentialParent, INamedTypeSymbol potentialChild)
        {
            if (SymbolEqualityComparer.Default.Equals(potentialParent.ConstructUnboundGenericType(), potentialChild.ConstructUnboundGenericType()))
            {
                for (int i = 0; i < potentialParent.TypeArguments.Length; i++)
                {
                    var parentArgument = potentialParent.TypeArguments[i];
                    var childArgument = potentialChild.TypeArguments[i];

                    // Handle covariance
                    if (potentialParent.TypeParameters[i].Variance == VarianceKind.Out)
                    {
                        if (!SymbolEqualityComparer.Default.Equals(parentArgument, childArgument) &&
                            !AreTypeArgumentsEqual(parentArgument, childArgument))
                        {
                            return false;
                        }
                    }
                    else if (!SymbolEqualityComparer.Default.Equals(parentArgument, childArgument))
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        private static bool AreTypeArgumentsEqual(ITypeSymbol parentArgument, ITypeSymbol childArgument)
        {
            // Direct equality check
            if (SymbolEqualityComparer.Default.Equals(parentArgument, childArgument))
            {
                return true;
            }

            // Handle type parameter symbols
            if (parentArgument is ITypeParameterSymbol parentTypeParam)
            {
                // Check if childArgument satisfies the constraints of parentTypeParam
                foreach (var constraint in parentTypeParam.ConstraintTypes)
                {
                    if (AreTypeArgumentsEqual(constraint, childArgument))
                    {
                        return true;
                    }
                }
            }

            // Handle named type symbols recursively
            if (parentArgument is INamedTypeSymbol parentNamed && childArgument is INamedTypeSymbol childNamed)
            {
                if (parentNamed.IsGenericType && childNamed.IsGenericType)
                {
                    return AreGenericTypesEqual(parentNamed, childNamed);
                }

                // Check all interfaces of the child type
                foreach (var iface in childNamed.AllInterfaces)
                {
                    if (AreTypeArgumentsEqual(parentArgument, iface))
                    {
                        return true;
                    }
                }

                // Check base type of the child
                if (childNamed.BaseType != null && AreTypeArgumentsEqual(parentNamed, childNamed.BaseType))
                {
                    return true;
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
            return SymbolEqualityComparer.Default.Equals(left, right);
        }

        private static bool ImplementsInterface(INamedTypeSymbol left, INamedTypeSymbol right)
        {
            // Check if 'right' is an interface
            if (right.TypeKind == TypeKind.Interface)
            {
                // Return true if there is an exact match
                if (SymbolEqualityComparer.Default.Equals(left, right))
                {
                    return true;
                }
            }

            // Check if 'right' or any of its base types implement 'left' with matching type arguments
            INamedTypeSymbol currentType = right;

            while (currentType != null)
            {
                // Check if the current type directly implements the interface with matching type arguments
                foreach (var interfaceType in currentType.AllInterfaces)
                {
                    if (ImplementsInterfaceWithMatchingTypeArguments(left, interfaceType))
                    {
                        return true;
                    }
                }

                // Move to the base type
                currentType = currentType.BaseType;
            }

            return false;
        }

        private static bool ImplementsInterfaceWithMatchingTypeArguments(INamedTypeSymbol left, INamedTypeSymbol implementedInterface)
        {
            // Check if both interfaces have the same original definition
            if (!AreEqual(left.OriginalDefinition, implementedInterface.OriginalDefinition))
            {
                return false;
            }

            // Check if both are generic and have the same number of type arguments
            if (left.IsGenericType && implementedInterface.IsGenericType)
            {
                var leftTypeArguments = left.TypeArguments;
                var implementedTypeArguments = implementedInterface.TypeArguments;

                if (leftTypeArguments.Length != implementedTypeArguments.Length)
                {
                    return false;
                }

                for (int i = 0; i < leftTypeArguments.Length; i++)
                {
                    var rightSideInterface = implementedTypeArguments[i];
                    if (TypeArgumentsMatch(leftTypeArguments[i], rightSideInterface))
                    {
                        return true;
                    }

                    if (rightSideInterface.BaseType != null && rightSideInterface.BaseType.Kind == SymbolKind.NamedType)
                    {
                        if (leftTypeArguments[i].TypeKind == TypeKind.TypeParameter)
                        {
                            return true;
                        }
                        foreach (var rightInterface in implementedTypeArguments[i].BaseType.AllInterfaces)
                        {
                            if (TypeArgumentsMatch(left, rightInterface))
                            {
                                return true;
                            }
                        }
                        if (TypeArgumentsMatch(leftTypeArguments[i], implementedTypeArguments[i].BaseType))
                        {
                            return true;
                        }
                    }
                }
            }

            // Recursively check base interfaces of the implemented interface
            foreach (var baseInterface in implementedInterface.AllInterfaces)
            {
                if (ImplementsInterfaceWithMatchingTypeArguments(left, baseInterface))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TypeArgumentsMatch(ITypeSymbol leftTypeArgument, ITypeSymbol implementedTypeArgument)
        {
            // Check if the type arguments are equal
            if (AreEqual(leftTypeArgument, implementedTypeArgument))
            {
                return true;
            }

            // If the type arguments are themselves generic, recursively check their type arguments
            if (leftTypeArgument is INamedTypeSymbol leftNamedType && implementedTypeArgument is INamedTypeSymbol implementedNamedType)
            {
                if (leftNamedType.IsGenericType && implementedNamedType.IsGenericType)
                {
                    var leftNestedTypeArguments = leftNamedType.TypeArguments;
                    var implementedNestedTypeArguments = implementedNamedType.TypeArguments;

                    if (leftNestedTypeArguments.Length != implementedNestedTypeArguments.Length)
                    {
                        return false;
                    }

                    for (int i = 0; i < leftNestedTypeArguments.Length; i++)
                    {
                        if (!TypeArgumentsMatch(leftNestedTypeArguments[i], implementedNestedTypeArguments[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }

            return false;
        }

    }
}