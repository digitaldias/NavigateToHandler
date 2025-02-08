using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using HandlerLocator;
using Microsoft.VisualStudio.Shell.Interop;

namespace NavigateToHandler.Dialogs;

public class IdentifiedHandlerViewModel
{
    private readonly IdentifiedHandler _handler;

    public IdentifiedHandlerViewModel(IdentifiedHandler handler) => _handler = handler;

    public IdentifiedHandler BackingClass => _handler;

    public string SourceFile => _handler.SourceFile;

    public string TypeToFind => _handler.TypeToFind;

    public string ClassName => _handler.ClassName;

    public string MethodName => _handler.MethodName;

    public N2HMethodAccess MethodAccess => _handler.MethodAccess;

    public string LineNumber => _handler.LineNumber.ToString();

    public string CaretPosition => _handler.Column.ToString();

    public string Position => $"({_handler.LineNumber},{_handler.Column})";

    public string AsArgument => _handler.AsArgument;

    public string ClassType => _handler.ClassType;

    public string ArgumentName => _handler.AsArgument;
}

/// <summary>
/// Interaction logic for DisplayResultsWindowControl.
/// </summary>
public partial class DisplayResultsWindowControl : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DisplayResultsWindowControl"/> class.
    /// </summary>
    public DisplayResultsWindowControl()
    {
        InitializeComponent();
        DataContext = this;
        IdentifiedHandlers = [];
    }

    public ObservableCollection<IdentifiedHandlerViewModel> IdentifiedHandlers { get; private set; }

    public async Task PopulateListAsync(IEnumerable<IdentifiedHandler> handlers)
    {
        IdentifiedHandlers.Clear();
        foreach (var handler in handlers)
        {
            IdentifiedHandlers.Add(new IdentifiedHandlerViewModel(handler));
        }
        await Task.Delay(50);
    }

    private void ItemsToShow_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _ = HandleSelectionChangedAsync(sender, e);
    }

    private async Task HandleSelectionChangedAsync(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ItemsToShow.SelectedItem is IdentifiedHandlerViewModel selectedItem && selectedItem != null)
        {
            try
            {
                await OpenFileAtPositionAsync(selectedItem.BackingClass.SourceFile, selectedItem.BackingClass.LineNumber, selectedItem.BackingClass.CaretPosition);
                ItemsToShow.SelectedValue = null;
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"An error occurred while opening the file: {ex.Message}");
            }
        }
    }

    private async Task OpenFileAtPositionAsync(string filePath, int line, int column)
    {
        try
        {
            var documentView = await VS.Documents.OpenAsync(filePath);
            var textView = documentView.TextView;

            var lineSnapshot = textView.TextSnapshot.GetLineFromLineNumber(line > 0 ? line - 1 : line);
            int targetPosition = Math.Min(column, lineSnapshot.End.Position);

            textView.Caret.MoveTo(new Microsoft.VisualStudio.Text.SnapshotPoint(textView.TextSnapshot, targetPosition));
            textView.ViewScroller.EnsureSpanVisible(new Microsoft.VisualStudio.Text.SnapshotSpan(textView.TextSnapshot, targetPosition, 1));
        }
        catch (Exception ex)
        {
            // Handle exceptions related to opening the document or moving the caret
            await ShowErrorMessageAsync($"An error occurred while navigating to the position: {ex.Message}");
        }
    }

    private async Task ShowErrorMessageAsync(string message)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        VsShellUtilities.ShowMessageBox(
            ServiceProvider.GlobalProvider,
            message,
            "Error",
            OLEMSGICON.OLEMSGICON_CRITICAL,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
