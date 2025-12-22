using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWindowsTerminalControl;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for a single-terminal tab launched from a custom profile.
/// Unlike TerminalPairTabViewModel, this shows only one terminal.
/// </summary>
public partial class ProfileTerminalTabViewModel : ObservableObject, ITabViewModel
{
    [ObservableProperty]
    private string _title = "Terminal";

    [ObservableProperty]
    private string _tabIcon = "▶";

    public string WorkingDirectory { get; }
    public bool IsCloseable => true;
    public string DisplayTitle => Title;

    [ObservableProperty]
    private ContentControl? _terminalContent;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _hasUnreadActivity;

    // Track previous activity state to detect transitions
    private bool _wasActive;

    /// <summary>
    /// Whether this tab is currently selected. Set by MainViewModel.
    /// </summary>
    public bool IsSelected { get; set; }

    [ObservableProperty]
    private bool _isVisibleInFocusMode = true;

    /// <summary>
    /// Updates visibility based on focus mode state.
    /// Profile terminals follow the same logic as project tabs.
    /// </summary>
    public void UpdateFocusModeVisibility(bool isFocusModeEnabled, IReadOnlyList<string> currentTaskProjects)
    {
        if (!isFocusModeEnabled || currentTaskProjects.Count == 0 || string.IsNullOrEmpty(WorkingDirectory))
        {
            // Focus mode disabled, no projects in task, or no working dir = show all
            IsVisibleInFocusMode = true;
            return;
        }

        // Check if this tab's working directory matches any project in the current task
        var normalizedPath = WorkingDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        IsVisibleInFocusMode = currentTaskProjects.Any(p =>
            p.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsAnyTerminalActive => IsActive;

    public Profile Profile { get; }
    public TerminalSession Session { get; }

    private readonly IStatisticsService _statisticsService;
    private readonly IClipboardService _clipboardService;

    public event EventHandler? CloseRequested;

    public ProfileTerminalTabViewModel(
        Profile profile,
        string workingDirectory,
        IStatisticsService statisticsService,
        IClipboardService clipboardService)
    {
        Profile = profile;
        WorkingDirectory = workingDirectory;
        _statisticsService = statisticsService;
        _clipboardService = clipboardService;

        // Set title: "ProfileName - DirectoryName" or just "ProfileName" if no working dir
        var dirName = string.IsNullOrWhiteSpace(workingDirectory)
            ? ""
            : System.IO.Path.GetFileName(workingDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar));

        Title = string.IsNullOrEmpty(dirName)
            ? profile.Name
            : $"{profile.Name} - {dirName}";

        // Set icon from profile, or default
        TabIcon = string.IsNullOrWhiteSpace(profile.Icon) ? "▶" : profile.Icon;

        // Create the terminal session
        Session = new TerminalSession(profile, statisticsService, clipboardService, "Profile");
    }

    /// <summary>
    /// Sets the terminal control after it's created by the factory.
    /// </summary>
    public void SetTerminalControl(EasyTerminalControl control)
    {
        TerminalContent = control;
        Session.SetTerminalControl(control);

        // Subscribe to activity changes for UI updates
        Session.ActivityChanged += (s, e) =>
        {
            IsActive = Session.IsActive;
        };
    }

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyTerminalActive));

        // Transition from active to idle: mark as unread, but only if tab is NOT selected
        // This prevents false positives from terminal focus/blur rendering events
        if (_wasActive && !value && !IsSelected)
        {
            HasUnreadActivity = true;
        }

        _wasActive = value;
    }

    /// <summary>
    /// Clears the unread activity state. Called when the tab becomes focused/selected.
    /// Also resets the transition tracking to prevent false positives from terminal
    /// rendering/focus events that briefly trigger activity.
    /// </summary>
    public void ClearUnreadActivity()
    {
        HasUnreadActivity = false;
        // Sync tracking state to current state to avoid false transition detection
        _wasActive = IsActive;
    }

    /// <summary>
    /// Updates activity state from the terminal session.
    /// Called periodically to check for idle transitions.
    /// </summary>
    public void UpdateActivityState()
    {
        Session.CheckActivityState();
        IsActive = Session.IsActive;
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Focuses the terminal control.
    /// </summary>
    public void Focus()
    {
        Session.Focus();
    }
}
