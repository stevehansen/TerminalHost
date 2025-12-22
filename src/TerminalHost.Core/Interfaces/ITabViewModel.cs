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
    /// Updates the IsVisibleInFocusMode property based on current focus mode state.
    /// </summary>
    void UpdateFocusModeVisibility(bool isFocusModeEnabled, IReadOnlyList<string> currentTaskProjects);

    event EventHandler? CloseRequested;
}
