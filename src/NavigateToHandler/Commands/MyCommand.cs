using System.Collections.Generic;
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
        private const string PANE_TITLE = "Public Handler Results";
        private OutputWindowPane _pane;

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
            {
                return;
            }

            // Get Roslyn document
            Document roslynDocument = workspace.CurrentSolution.GetDocument(documentId);

            // Get the position under the cursor
            int position = documentView.TextView.Selection.ActivePoint.Position.Position;

            var locator = new FindHandlerLocator(workspace.CurrentSolution, roslynDocument, documentView.FilePath, position);
            var allHandlers = await locator.FindAllHandlers();
            if (allHandlers is null || !allHandlers.Any())
            {
                await DisplayNoLoveAsync();
                return;
            }

            if(allHandlers.Count() == 1)
            {
                await DisplayHandlerAsync(allHandlers.First());
                return;
            }

            await DisplayHandlersInOutputPaneAsync(allHandlers);
        }

        private async Task DisplayNoLoveAsync()
        {
            _pane ??= await VS.Windows.CreateOutputWindowPaneAsync(PANE_TITLE, lazyCreate: true);
            await _pane.ClearAsync();
            await _pane.WriteLineAsync($"No public handlers exist for the type under the cursor in the current solution.");
            await _pane.ActivateAsync();
        }

        private async Task DisplayHandlersInOutputPaneAsync(IEnumerable<IdentifiedHandler> allHandlers)
        {
            _pane ??= await VS.Windows.CreateOutputWindowPaneAsync(PANE_TITLE, lazyCreate: true);
            await _pane.ClearAsync();
            await _pane.WriteLineAsync($"Found {allHandlers.Count()} public methods that consume '{allHandlers.First().TypeToFind}':");
            await _pane.WriteLineAsync($"Double-click the relevant line to open." + Environment.NewLine);
            foreach ( var handler in allHandlers.OrderBy(h => h.SourceFile).ThenBy(h => h.TypeName).ThenBy(h => h.PublicMethod))
            {
                await _pane.WriteLineAsync($"{handler.DisplaySourceFile}:{handler.Fill}{handler.TypeName}.{handler.PublicMethod}()");
            }
            await _pane.ActivateAsync();
        }

        private async Task DisplayHandlerAsync(IdentifiedHandler identifiedHandler)
        {
            _pane ??= await VS.Windows.CreateOutputWindowPaneAsync(PANE_TITLE, lazyCreate: true);
            await _pane.ClearAsync();
            await _pane.WriteLineAsync($"Match found: {identifiedHandler.TypeName}.{identifiedHandler.PublicMethod}(), line: {identifiedHandler.LineNumber}, column: {identifiedHandler.Column}");
            await _pane.ActivateAsync();

            var openedView = await VS.Documents.OpenAsync(identifiedHandler.SourceFile);
            openedView.TextView.Caret.MoveTo(new SnapshotPoint(openedView.TextBuffer.CurrentSnapshot, identifiedHandler.CaretPosition));
            openedView.TextView.Caret.EnsureVisible();
        }
    }
}
