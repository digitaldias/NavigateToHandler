using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Community.VisualStudio.Toolkit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CSharp;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Package;
using Microsoft.VisualStudio.PlatformUI;
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
            if(documentView is not null)
            {
                var documentId = workspace.CurrentSolution.GetDocumentIdsWithFilePath(documentView.FilePath).FirstOrDefault();
                if(documentId is not null)
                {
                    // Get Roslyn document
                    Document roslynDocument = workspace.CurrentSolution.GetDocument(documentId);

                    int position = documentView.TextView.Selection.ActivePoint.Position.Position;

                    // Get the syntax root
                    SyntaxNode root = await roslynDocument.GetSyntaxRootAsync();
                    SyntaxNode syntaxNode = root.FindNode(new TextSpan(position, 0), findInsideTrivia: true, getInnermostNodeForTie: true);

                    // Get the type  information
                    SemanticModel model = await roslynDocument.GetSemanticModelAsync();
                    var typeInfo = model.GetTypeInfo(syntaxNode);
                    await VS.StatusBar.ShowMessageAsync($"Locating handler for {typeInfo.Type.Name}");

                    // Bingo, this is what we're looking for
                    if(typeInfo.ConvertedType.ToDisplayString().StartsWith("MediatR.IRequest"))
                    {
                        var namedType = (INamedTypeSymbol) typeInfo.Type;
                        var matches = await SymbolFinder.FindReferencesAsync(namedType, workspace.CurrentSolution);
                        if(matches.Any())
                        {
                            var type = matches.First();
                            var matchedDocument = type.Locations.FirstOrDefault(location => location.Document.FilePath.EndsWith("Handler.cs"));
                            var openedView = await VS.Documents.OpenAsync(matchedDocument.Location.SourceTree.FilePath);
                            var line = openedView.TextBuffer.CurrentSnapshot.Lines.FirstOrDefault(line => line.GetText().Contains($" Handle({typeInfo.Type.Name}"));
                            openedView.TextView.Caret.MoveTo(new SnapshotPoint(openedView.TextBuffer.CurrentSnapshot, line.Start.Position));
                            await VS.StatusBar.ClearAsync();
                            return;
                        }
                    }
                    await VS.StatusBar.ShowMessageAsync($"No APIs found for {typeInfo.Type.Name}");
                }
            }

        }
    }
}
