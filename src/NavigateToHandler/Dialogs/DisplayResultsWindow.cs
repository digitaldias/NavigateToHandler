using System.Runtime.InteropServices;

namespace NavigateToHandler.Dialogs
{
    /// <summary>
    /// This class implements the tool window exposed by this package and hosts a user control.
    /// </summary>
    /// <remarks>
    /// In Visual Studio tool windows are composed of a frame (implemented by the shell) and a pane,
    /// usually implemented by the package implementer.
    /// <para>
    /// This class derives from the ToolWindowPane class provided from the MPF in order to use its
    /// implementation of the IVsUIElementPane interface.
    /// </para>
    /// </remarks>
    [Guid("5342cbfd-1e84-4ac6-b306-7997cdd59c0d")]
    public class DisplayResultsWindow : ToolWindowPane
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DisplayResultsWindow"/> class.
        /// </summary>
        public DisplayResultsWindow() : base(null)
        {
            this.Caption = "Navigate to Handler";
            this.Content = new DisplayResultsWindowControl();
        }
    }
}
