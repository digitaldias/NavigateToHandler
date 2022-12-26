using System.Linq;
using System.Threading.Tasks;
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
            var allHandlers = await locator.FindAllHandlers();
            if (allHandlers is null || !allHandlers.Any())
                return;

            if(allHandlers.Count() == 1)
            {
                await DisplayHandler(allHandlers.First());
                return;
            }

            // Show the first match. TODO: Handle multiple matches
            return;
        }

        private async Task DisplayHandler(IdentifiedHandler identifiedHandler)
        {
            var openedView = await VS.Documents.OpenAsync(identifiedHandler.SourceFile);
            openedView.TextView.Caret.MoveTo(new SnapshotPoint(openedView.TextBuffer.CurrentSnapshot, identifiedHandler.LineNumber));
            openedView.TextView.Caret.EnsureVisible();
            await VS.StatusBar.ClearAsync();
        }
    }
}
