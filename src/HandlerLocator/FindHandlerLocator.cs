﻿using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
            List<IdentifiedHandler> allHandlers = new List<IdentifiedHandler>();
            SyntaxNode syntaxRoot = await _workingDocument.GetSyntaxRootAsync();
            SyntaxNode syntaxNode = syntaxRoot.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(_linePosition, 0), true, true);

            // Get type information
            SemanticModel semanticModel = await _workingDocument.GetSemanticModelAsync();

            ITypeSymbol symbol = GetTypeInfo(semanticModel, syntaxNode);
            if (symbol is null)
                return allHandlers;

            string regexPattern = $@"(^|\W){symbol.Name}($|\W)";
            IEnumerable<ReferencedSymbol> references = await SymbolFinder.FindReferencesAsync(symbol, _solution);

            foreach (ReferencedSymbol reference in references)
            {
                foreach (ReferenceLocation location in reference.Locations)
                {
                    SyntaxTree tree = await location.Document.GetSyntaxTreeAsync();
                    SyntaxNode root = await tree.GetRootAsync();
                    IEnumerable<MethodDeclarationSyntax> allMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                    List<MethodDeclarationSyntax> publicMethods = allMethods.Where(publicMethod =>
                        publicMethod.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.ProtectedKeyword)) &&
                        publicMethod.ParameterList.Parameters.Any(parameter => Regex.IsMatch(parameter.ToFullString(), regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled)))
                        .OrderBy(m => m.ToFullString())
                        .ToList();

                    if (!publicMethods.Any())
                        continue;

                    foreach (MethodDeclarationSyntax method in publicMethods)
                    {
                        FileLinePositionSpan lineSpan = method.GetLocation().GetLineSpan();
                        IdentifiedHandler candidate = new IdentifiedHandler
                        {
                            TypeToFind = symbol.Name,
                            TypeName = location.Document.Name.Replace(".cs", ""),
                            PublicMethod = method.Identifier.ToFullString(),
                            SourceFile = lineSpan.Path,
                            DisplaySourceFile = $"{lineSpan.Path}({lineSpan.StartLinePosition.Line + 1},{lineSpan.StartLinePosition.Character + 1})",
                            LineNumber = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            CaretPosition = method.Span.Start
                        };

                        if (allHandlers.Any(h => h.SourceFile == candidate.SourceFile && h.PublicMethod == candidate.PublicMethod && h.LineNumber == candidate.LineNumber))
                            continue;

                        allHandlers.Add(candidate);
                    }
                }
            }

            if (allHandlers.Any())
            {
                int longestPath = allHandlers.Max(h => h.DisplaySourceFile.Length);
                allHandlers.ForEach(handler =>
                {
                    int neededSpaces = longestPath - handler.DisplaySourceFile.Length;
                    handler.Fill = new string(' ', neededSpaces + 1);
                });
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