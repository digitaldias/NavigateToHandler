using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
        private readonly int _linePosition;

        public FindHandlerLocator(Solution solution, Document workingDocument, int linePosition)
        {
            _solution = solution;
            _workingDocument = workingDocument;
            _linePosition = linePosition;
        }

        public async Task<IEnumerable<IdentifiedHandler>> FindAllHandlers()
        {
            try
            {
                return await FindAllHandlersTask();
            }
            catch
            {
                return new List<IdentifiedHandler>();
            }
        }

        public async Task<IEnumerable<IdentifiedHandler>> FindAllHandlersTask()
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
            var searchTarget = (Symbol: symbol, SymbolDefinition: symbolDefinition);

            var projectTasks = _solution.Projects.Select(async project =>
            {
                var documentTasks = project.Documents.Select(async document =>
                {
                    SyntaxNode root = await document.GetSyntaxRootAsync();
                    SemanticModel model = await document.GetSemanticModelAsync();
                    var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

                    var documentContext = (Model: model, Document: document);
                    var methodTasks = methodDeclarations.Select(methodDeclaration =>
                        CollectMatchingHandlerAsync(methodDeclaration, documentContext, searchTarget, allHandlers));
                    await Task.WhenAll(methodTasks);
                });
                await Task.WhenAll(documentTasks);
            });
            await Task.WhenAll(projectTasks);

            return allHandlers;
        }

        private async Task CollectMatchingHandlerAsync(
            MethodDeclarationSyntax methodDeclaration,
            (SemanticModel Model, Document Document) documentContext,
            (ITypeSymbol Symbol, ISymbol SymbolDefinition) searchTarget,
            ConcurrentBag<IdentifiedHandler> allHandlers)
        {
            foreach (var parameter in methodDeclaration.ParameterList.Parameters)
            {
                var parameterType = documentContext.Model.GetTypeInfo(parameter.Type).Type;
                if (parameterType is null || !await IsSymbolMatch(searchTarget.Symbol, searchTarget.SymbolDefinition, parameterType))
                {
                    continue;
                }

                // Checking Access Modifier, skip private, file and unknown access
                var methodAccess = GetMethodAccess(methodDeclaration);
                if (methodAccess == N2HMethodAccess.Private || methodAccess == N2HMethodAccess.File || methodAccess == N2HMethodAccess.Unknown)
                {
                    continue;
                }

                var match = (Symbol: searchTarget.Symbol, ParameterType: parameterType);
                allHandlers.Add(BuildIdentifiedHandler(methodDeclaration, documentContext.Document, match, methodAccess));
            }
        }

        private IdentifiedHandler BuildIdentifiedHandler(
            MethodDeclarationSyntax methodDeclaration,
            Document document,
            (ITypeSymbol Symbol, ITypeSymbol ParameterType) match,
            N2HMethodAccess methodAccess)
        {
            var lineSpan = methodDeclaration.SyntaxTree.GetLineSpan(methodDeclaration.Span);
            var classDeclaration = methodDeclaration.AncestorsAndSelf()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault();

            string className = "Unknown";
            string classType = "Unknown";
            if (classDeclaration != null)
            {
                className = classDeclaration.Identifier.Text;
                classType = "class";
            }
            else if (methodDeclaration.Parent is RecordDeclarationSyntax recordClass)
            {
                className = recordClass.Identifier.Text;
                classType = "record";
            }

            return new IdentifiedHandler
            {
                TypeToFind = match.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                ClassName = classDeclaration?.Identifier.Text ?? "Unknown",
                ClassType = classType,
                AsArgument = GetDisplayNameFor(match.ParameterType),
                MethodName = methodDeclaration.Identifier.Text + "(...)",
                MethodAccess = methodAccess,
                SourceFile = document.FilePath,
                DisplaySourceFile = $"{document.FilePath}({lineSpan.StartLinePosition.Line + 1},{lineSpan.StartLinePosition.Character + 1})",
                LineNumber = lineSpan.StartLinePosition.Line + 1,
                EndLineNumber = lineSpan.EndLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1,
                CaretPosition = methodDeclaration.Span.Start
            };
        }

        internal static N2HMethodAccess GetMethodAccess(MethodDeclarationSyntax method)
        {
            var modifiers = method.Modifiers;
            if (modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
            {
                return N2HMethodAccess.Public;
            }
            if (modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword)))
            {
                return N2HMethodAccess.Protected;
            }
            if (modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword)))
            {
                return N2HMethodAccess.Internal;
            }
            if (modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword)))
            {
                return N2HMethodAccess.Private;
            }
            if (modifiers.Any(m => m.IsKind(SyntaxKind.FileKeyword)))
            {
                return N2HMethodAccess.File;
            }
            return N2HMethodAccess.Unknown;
        }

        internal string GetDisplayNameFor(ITypeSymbol parameterSymbol)
        {
            var builder = new StringBuilder(parameterSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            if (parameterSymbol is INamedTypeSymbol symbol)
            {
                AppendBaseTypeDisplay(symbol, builder);
            }
            return builder.ToString();
        }

        internal void AppendBaseTypeDisplay(INamedTypeSymbol symbol, StringBuilder builder)
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

        private static readonly HashSet<SpecialType> BuiltInSpecialTypes = new HashSet<SpecialType>
        {
            SpecialType.System_Boolean,
            SpecialType.System_Byte,
            SpecialType.System_Char,
            SpecialType.System_Decimal,
            SpecialType.System_Double,
            SpecialType.System_Single,
            SpecialType.System_Int32,
            SpecialType.System_UInt32,
            SpecialType.System_Int64,
            SpecialType.System_UInt64,
            SpecialType.System_Int16,
            SpecialType.System_UInt16,
            SpecialType.System_String,
            SpecialType.System_Object,
        };

        public static bool IsBuiltInType(ITypeSymbol typeSymbol)
        {
            return BuiltInSpecialTypes.Contains(typeSymbol.SpecialType);
        }

        internal void AppendTypeDisplay(ITypeSymbol typeSymbol, StringBuilder builder)
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

        internal ITypeSymbol GetTypeInfo(SemanticModel semanticModel, SyntaxNode syntaxNode)
        {
            if (syntaxNode is InterfaceDeclarationSyntax interfaceDeclaration)
            {
                return semanticModel.GetDeclaredSymbol(interfaceDeclaration);
            }
            if (syntaxNode is IdentifierNameSyntax identifierName)
            {
                return GetTypeInfoForIdentifierName(semanticModel, identifierName);
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

        private static ITypeSymbol GetTypeInfoForIdentifierName(SemanticModel semanticModel, IdentifierNameSyntax identifierName)
        {
            TypeInfo foundType = semanticModel.GetTypeInfo(identifierName);
            if (foundType.Type != null)
                return foundType.Type;

            return semanticModel.GetTypeInfo(identifierName.Parent) is TypeInfo typeInfo && typeInfo.Type is null
                ? semanticModel.GetTypeInfo(identifierName).Type
                : typeInfo.Type;
        }

        internal async Task<bool> IsSymbolMatch(ITypeSymbol symbol, ISymbol symbolDefinition, ITypeSymbol parameterType)
        {
            if (TypeSymbolMatcher.IsInherited(symbol, parameterType) && symbol.TypeKind != TypeKind.Interface)
            {
                return true;
            }

            if (symbolDefinition is INamedTypeSymbol subject && await SymbolFinder.FindSourceDefinitionAsync(parameterType, _solution) is INamedTypeSymbol item)
            {
                if (TypeSymbolMatcher.AreEqual(subject, item))
                {
                    return true;
                }
            }

            var left = symbol is INamedTypeSymbol ? symbol as INamedTypeSymbol : null;
            var right = parameterType is INamedTypeSymbol ? parameterType as INamedTypeSymbol : null;

            if (left != null && right != null && left.TypeKind == TypeKind.Interface)
            {
                return TypeSymbolMatcher.ImplementsInterface(left, right);
            }

            return false;
        }

    }
}