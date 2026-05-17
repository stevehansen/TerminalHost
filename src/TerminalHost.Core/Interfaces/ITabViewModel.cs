using System.ComponentModel;

namespace TerminalHost.Core.Interfaces;

public interface ITabViewModel : INotifyPropertyChanged
{
    string Title { get; }
    string TabIcon { get; }
    string WorkingDirectory { get; }
    bool IsCloseable { get; }
    bool IsAnyTerminalActive { get; }

    /// <summary>
    /// True if terminal activity has completed but the tab hasn't been focused yet.
    /// Used to show a "completed" indicator (green) until the user checks the tab.
    /// </summary>
    bool HasUnreadActivity { get; }

    /// <summary>
    /// Clears the unread activity state. Called when the tab becomes focused/selected.
    /// </summary>
    void ClearUnreadActivity();

    /// <summary>
    /// Whether this tab is currently selected. Set by MainViewModel.
    /// Used to prevent false activity indicators from terminal focus/blur events.
    /// </summary>
    bool IsSelected { get; set; }

    /// <summary>
    /// The title to display in the tab header. May include additional info like git branch.
    /// </summary>
    string DisplayTitle { get; }

    /// <summary>
    /// Whether this tab should be visible when focus mode is enabled.
    /// Returns true if focus mode is disabled or if the tab's project is in the current task.
    /// </summary>
    bool IsVisibleInFocusMode { get; }

    /// <summary>
    /// Whether to show the activity spinner on the tab.
    /// True when terminal is active AND tab is NOT selected.
    /// </summary>
    bool ShowActivitySpinner { get; }

    /// <summary>
    /// Whether to show the completed indicator (green dot) on the tab.
    /// True when activity finished AND has unread activity AND tab is NOT selected.
    /// </summary>
    bool ShowCompletedIndicator { get; }

    /// <summary>
    /// Whether the terminal is waiting for user input (detected via patterns).
    /// </summary>
    bool IsWaitingForInput { get; }

    /// <summary>
    /// Whether to show the waiting for input indicator on the tab.
    /// True when waiting for input AND tab is NOT selected.
    /// </summary>
    bool ShowWaitingIndicator { get; }

    /// <summary>
    /// Whether to show the Claude task indicator (blue robot) on the tab.
    /// True when there are active Claude tasks for this workspace.
    /// </summary>
    bool ShowClaudeTaskIndicator { get; }

    /// <summary>
    /// Whether the terminal(s) for this tab have been initialized.
    /// Used for lazy initialization - terminals are only created when the tab is first selected.
    /// </summary>
    bool IsTerminalInitialized { get; }

    /// <summary>
    /// Initializes the terminal(s) for this tab. Called when the tab is first selected.
    /// For non-terminal tabs, this is a no-op.
    /// </summary>
    Task InitializeTerminalsAsync();

    /// <summary>
    /// Updates the IsVisibleInFocusMode property based on current focus mode state.
    /// </summary>
    void UpdateFocusModeVisibility(bool isFocusModeEnabled, IReadOnlyList<string> currentTaskProjects);

    /// <summary>
    /// Whether this tab can be duplicated. Only project tabs can be duplicated.
    /// </summary>
    bool CanDuplicate { get; }

    /// <summary>
    /// Asks the user to confirm closing this tab if it holds work that would be lost
    /// (e.g. running processes). Returns false if the user canceled the close.
    /// Default implementation returns true; tabs that own running work override this.
    /// </summary>
    /// <param name="dialogService">Service used to show a confirmation prompt.</param>
    /// <param name="confirmIfBusy">If false, the tab should not prompt even when busy.</param>
    bool ConfirmCanClose(IDialogService dialogService, bool confirmIfBusy) => true;

    event EventHandler? CloseRequested;
}
