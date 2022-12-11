using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Text;

namespace NavigateToHandler
{
    [Command(PackageIds.MyCommand)]
    internal sealed class MyCommand : BaseCommand<MyCommand>
    {
        // Oy vey
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {

            var workspace = await VS.GetMefServiceAsync<VisualStudioWorkspace>();
            if (workspace is null)
                return;

            var documentView = await VS.Documents.GetActiveDocumentViewAsync();
            if (documentView is null)
                return;

            var documentId = workspace.CurrentSolution.GetDocumentIdsWithFilePath(documentView.FilePath).FirstOrDefault();
            if (documentId is null)
                return;

            // Get Roslyn document
            Document roslynDocument = workspace.CurrentSolution.GetDocument(documentId);

            // Get the position under the cursor
            int position = documentView.TextView.Selection.ActivePoint.Position.Position;

            // Get the syntax root
            SyntaxNode root = await roslynDocument.GetSyntaxRootAsync();
            SyntaxNode syntaxNode = root.FindNode(new TextSpan(position, 0), findInsideTrivia: true, getInnermostNodeForTie: true);

            // Get the type  information
            SemanticModel model = await roslynDocument.GetSemanticModelAsync();
            var typeInfo = model.GetTypeInfo(syntaxNode);
            var variableType = typeInfo.Type;

            // We did not get this type
            if (variableType is null)
                return;

            // Skip generics here, we don't need the grief
            var variableBaseTypeName = typeInfo.ConvertedType.ToDisplayString();
            var lessthanPosition = variableBaseTypeName.IndexOf('<');
            if (lessthanPosition > 0)
            {
                variableBaseTypeName = variableBaseTypeName.Substring(0, lessthanPosition);
            }

            await VS.StatusBar.ShowMessageAsync($"Finding calls taking {variableType.Name}");

            if (typeInfo.ConvertedType.ToDisplayString().StartsWith(variableBaseTypeName))
            {
                var namedTypeSymbol = (INamedTypeSymbol) typeInfo.Type;
                var matchingReferences = await SymbolFinder.FindReferencesAsync(namedTypeSymbol, workspace.CurrentSolution);

                foreach (var matchingReference in matchingReferences)
                {
                    foreach (var location in matchingReference.Locations)
                    {
                        await VS.StatusBar.ShowMessageAsync($"Searching {location.Document.FilePath}");

                        // Find all public methods that take this type as a parameter
                        var matchingTree = await location.Document.GetSyntaxTreeAsync();
                        var matchingRoot = await matchingTree.GetRootAsync();
                        var allMethods = matchingRoot.DescendantNodes().OfType<MethodDeclarationSyntax>();
                        var publicMethods = allMethods.Where(m
                            => m.Modifiers.Any(mod => mod.Text.Equals("public"))
                            && m.ParameterList.Parameters.Any(p => p.ToFullString().Contains(namedTypeSymbol.Name)))
                            .ToList();

                        if (!publicMethods.Any())
                            continue;

                        // Show the first match. TODO: Handle multiple matches
                        var openedView = await VS.Documents.OpenAsync(location.Document.FilePath);
                        openedView.TextView.Caret.MoveTo(new SnapshotPoint(openedView.TextBuffer.CurrentSnapshot, publicMethods.First().Span.Start));
                        openedView.TextView.Caret.EnsureVisible();
                        await VS.StatusBar.ClearAsync();
                        return;
                    }
                }
            }
            await VS.StatusBar.ShowMessageAsync($"No public API methods found that take the type {typeInfo.Type.Name} as a parameter");
        }
    }
}
