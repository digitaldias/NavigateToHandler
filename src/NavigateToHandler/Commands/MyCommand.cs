using System.Linq;
using HandlerLocator;
using Microsoft.CodeAnalysis;
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

            var locator = new FindHandlerLocator(workspace.CurrentSolution, roslynDocument, documentView.FilePath, position);
            var result = await locator.FindFirstHandler();
            if (result is null)
            {
                return;
            }

            // Show the first match. TODO: Handle multiple matches
            var openedView = await VS.Documents.OpenAsync(result.SourceFile);
            openedView.TextView.Caret.MoveTo(new SnapshotPoint(openedView.TextBuffer.CurrentSnapshot, result.LineNumber));
            openedView.TextView.Caret.EnsureVisible();
            await VS.StatusBar.ClearAsync();
            return;
        }
    }
}
