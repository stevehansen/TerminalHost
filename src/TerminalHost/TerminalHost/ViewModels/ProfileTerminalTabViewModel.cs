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

    public bool IsAnyTerminalActive => IsActive;

    public Profile Profile { get; }
    public TerminalSession Session { get; }

    private readonly StatisticsService _statisticsService;

    public event EventHandler? CloseRequested;

    public ProfileTerminalTabViewModel(
        Profile profile,
        string workingDirectory,
        StatisticsService statisticsService)
    {
        Profile = profile;
        WorkingDirectory = workingDirectory;
        _statisticsService = statisticsService;

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
        Session = new TerminalSession(profile, statisticsService, "Profile");
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
