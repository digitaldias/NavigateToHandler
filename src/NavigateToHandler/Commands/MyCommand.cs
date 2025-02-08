using System.Collections.Generic;
using System.Linq;
using HandlerLocator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using NavigateToHandler.Dialogs;

namespace NavigateToHandler
{
    [Command(PackageIds.MyCommand)]
    internal sealed class MyCommand : BaseCommand<MyCommand>
    {
        private const string _paneTitle = "Navigate to Handler";
        private OutputWindowPane _pane;

        // Oy vey
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            // Initialize our pane
            _pane ??= await VS.Windows.CreateOutputWindowPaneAsync(_paneTitle, lazyCreate: true);

            var workspaceTask = VS.GetMefServiceAsync<VisualStudioWorkspace>();
            var documentViewTask = VS.Documents.GetActiveDocumentViewAsync();

            var workspace = await workspaceTask;
            var documentView = await documentViewTask;
            if (workspace is null || documentView is null)
                return;

            DocumentId documentId = workspace.CurrentSolution.GetDocumentIdsWithFilePath(documentView.FilePath).FirstOrDefault();
            if (documentId is null)
                return;

            // Get Roslyn document
            Document roslynDocument = workspace.CurrentSolution.GetDocument(documentId);

            // Get the position under the cursor
            int position = documentView.TextView.Selection.ActivePoint.Position.Position;

            FindHandlerLocator locator = new(workspace.CurrentSolution, roslynDocument, position);
            List<IdentifiedHandler> allHandlers = (await locator.FindAllHandlers())?.ToList();

            if (allHandlers is null || !allHandlers.Any())
            {
                await DisplayNoLoveAsync();
                return;
            }

            if (allHandlers.Count == 1)
            {
                var handler = allHandlers.First();
                await DisplayHandlerAsync(handler);
                return;
            }

            if (allHandlers.Count == 2)
            {
                SourceText sourceText = await roslynDocument.GetTextAsync();
                int cursorLine = sourceText.Lines.GetLineFromPosition(position).LineNumber + 1;

                var firstHandler = allHandlers[0];
                var secondHandler = allHandlers[1];

                // Both handlers need to be in the same file as the document.
                if (firstHandler.SourceFile == documentView.FilePath && secondHandler.SourceFile == documentView.FilePath)
                {
                    bool isCursorInFirst = cursorLine == firstHandler.LineNumber;
                    bool isCursorInSecond = cursorLine == secondHandler.LineNumber;

                    if (isCursorInFirst && !isCursorInSecond)
                    {
                        // Cursor is in first handler: jump to second.
                        await DisplayHandlerAsync(secondHandler);
                        return;
                    }
                    if (isCursorInSecond && !isCursorInFirst)
                    {
                        // Cursor is in second handler: jump to first.
                        await DisplayHandlerAsync(firstHandler);
                        return;
                    }

                    // If the cursor isn't inside either, just default to the first handler.
                    await DisplayHandlerAsync(firstHandler);
                    return;
                }
            }

            await DisplayHandlersInOutputPaneAsync(allHandlers);
        }

        private async Task DisplayNoLoveAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            await _pane.ClearAsync();
            await _pane.WriteLineAsync($"No handlers found for the type under the cursor.");
            await _pane.ActivateAsync();
            BringOutputWindowToFocus();
        }

        private async Task DisplayHandlersInOutputPaneAsync(List<IdentifiedHandler> allHandlers)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var firstOne = allHandlers.First();

            await ShowHandlersInToolWindowAsync([.. allHandlers]);
            string message = $"Found {allHandlers.Count()} public/protected methods that consume '{allHandlers.First().TypeToFind}':";
            string underlines = new('-', message.Length);

            await _pane.ClearAsync();

            using System.IO.TextWriter writer = await _pane.CreateOutputPaneTextWriterAsync();

            await writer.WriteLineAsync(message);
            await writer.WriteLineAsync(underlines);

            var sortedHandlers = allHandlers
                .OrderBy(h => h.SourceFile)
                .ThenBy(h => h.ClassName)
                .ThenBy(h => h.MethodName)
                .ToList();

            foreach (IdentifiedHandler handler in sortedHandlers)
            {
                await writer.WriteLineAsync($"{handler.DisplaySourceFile}:{handler.Fill} {handler.ClassType} {handler.ClassName}.{handler.MethodName}() as {handler.AsArgument}");
            }

            await writer.WriteLineAsync(underlines);
            await writer.WriteLineAsync($"Double-click the relevant line to open." + Environment.NewLine);
        }

        private async Task ShowHandlersInToolWindowAsync(List<IdentifiedHandler> allHandlers)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var window = await VS.Windows.ShowToolWindowAsync(Guid.Parse("5342cbfd-1e84-4ac6-b306-7997cdd59c0d"));
            if (window != null)
            {
                // Get the ToolWindowPane from the IVsWindowFrame
                if (window is IVsWindowFrame windowFrame)
                {
                    windowFrame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView);
                    var toolWindowPane = docView as ToolWindowPane;
                    if (toolWindowPane?.Content is DisplayResultsWindowControl control)
                    {
                        await control.PopulateListAsync(allHandlers);
                    }
                }
            }
        }

        private void BringOutputWindowToFocus()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsUIShell uiShell = VS.GetRequiredService<SVsUIShell, IVsUIShell>();
            if (uiShell != null)
            {
                // CLSID of the output windowFrame
                Guid clsidOutputWindow = new(ToolWindowGuids.Outputwindow);
                uiShell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref clsidOutputWindow, out IVsWindowFrame windowFrame);

                windowFrame?.Show();
            }
        }

        private async Task DisplayHandlerAsync(IdentifiedHandler identifiedHandler)
        {
            await _pane.ClearAsync();
            if (identifiedHandler.AsArgument == identifiedHandler.TypeToFind)
            {
                await _pane.WriteLineAsync($"Found {identifiedHandler.TypeToFind} in {identifiedHandler.ClassType} {identifiedHandler.ClassName}.{identifiedHandler.MethodName}(), line: {identifiedHandler.LineNumber}, column: {identifiedHandler.Column}");
            }
            else
            {
                await _pane.WriteLineAsync($"Found {identifiedHandler.TypeToFind} as '{identifiedHandler.AsArgument}' in {identifiedHandler.ClassType} {identifiedHandler.ClassName}.{identifiedHandler.MethodName}() as {identifiedHandler.TypeToFind}, line: {identifiedHandler.LineNumber}, column: {identifiedHandler.Column}");
            }

            DocumentView openedView = await VS.Documents.OpenAsync(identifiedHandler.SourceFile);
            openedView.TextView.Caret.MoveTo(new SnapshotPoint(openedView.TextBuffer.CurrentSnapshot, identifiedHandler.CaretPosition));
            openedView.TextView.Caret.EnsureVisible();
        }
    }
}
