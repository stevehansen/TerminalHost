using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWindowsTerminalControl;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class TerminalPairTabViewModel : ObservableObject, ITabViewModel
{
    [ObservableProperty]
    private string _title = "Terminal";

    public string TabIcon => "📁";
    public string WorkingDirectory => Pair.WorkingDirectory;
    public bool IsCloseable => true;

    [ObservableProperty]
    private string _customIcon = "🤖";

    [ObservableProperty]
    private string _shellIcon = "💻";

    [ObservableProperty]
    private ActiveTerminal _activeTerminal = ActiveTerminal.Custom;

    [ObservableProperty]
    private ContentControl? _customTerminalContent;

    [ObservableProperty]
    private ContentControl? _shellTerminalContent;

    [ObservableProperty]
    private bool _isSplitView = true;  // Default to split view

    [ObservableProperty]
    private double _splitRatio = 0.6;  // Custom terminal takes 60% by default

    [ObservableProperty]
    private GitStatus? _gitStatus;

    [ObservableProperty]
    private bool _isCustomTerminalActive;

    [ObservableProperty]
    private bool _isShellTerminalActive;

    /// <summary>
    /// True if either terminal is currently producing output.
    /// </summary>
    public bool IsAnyTerminalActive => IsCustomTerminalActive || IsShellTerminalActive;

    /// <summary>
    /// Collection of detected links from terminal output.
    /// </summary>
    public ObservableCollection<DetectedLink> DetectedLinks { get; } = new();

    /// <summary>
    /// True if there are any detected links to display.
    /// </summary>
    public bool HasDetectedLinks => DetectedLinks.Count > 0;

    // Computed column widths from split ratio
    public GridLength CustomColumnWidth => new GridLength(SplitRatio, GridUnitType.Star);
    public GridLength ShellColumnWidth => new GridLength(1 - SplitRatio, GridUnitType.Star);

    // Git display properties
    public string TitleWithGit => GitStatus?.IsGitRepository == true
        ? $"{Title} {GitStatus.BranchDisplayShort}"
        : Title;

    public string GitStatusDisplay => GitStatus?.StatusDisplayFull ?? "";

    public TerminalPair Pair { get; }

    public string CurrentIcon => ActiveTerminal == ActiveTerminal.Custom ? CustomIcon : ShellIcon;

    public ContentControl? CurrentTerminalContent => ActiveTerminal == ActiveTerminal.Custom
        ? CustomTerminalContent
        : ShellTerminalContent;

    public event EventHandler? CloseRequested;
    public event EventHandler? SettingsChanged;

    public TerminalPairTabViewModel(TerminalPair pair, string customIcon, string shellIcon)
    {
        Pair = pair;
        Title = pair.DirectoryName;
        CustomIcon = customIcon;
        ShellIcon = shellIcon;
        ActiveTerminal = pair.ActiveTerminal;
    }

    public void SetTerminalControls(EasyTerminalControl customControl, EasyTerminalControl shellControl)
    {
        CustomTerminalContent = customControl;
        ShellTerminalContent = shellControl;

        Pair.CustomTerminal.SetTerminalControl(customControl);
        Pair.ShellTerminal.SetTerminalControl(shellControl);

        // Subscribe to activity changes for immediate UI updates
        Pair.CustomTerminal.ActivityChanged += (s, e) =>
        {
            IsCustomTerminalActive = Pair.CustomTerminal.IsActive;
        };
        Pair.ShellTerminal.ActivityChanged += (s, e) =>
        {
            IsShellTerminalActive = Pair.ShellTerminal.IsActive;
        };

        // Notify that CurrentTerminalContent has changed
        OnPropertyChanged(nameof(CurrentTerminalContent));
    }

    [RelayCommand]
    private void SwitchTerminal()
    {
        Pair.SwitchTerminal();
        ActiveTerminal = Pair.ActiveTerminal;
    }

    [RelayCommand]
    private void ShowCustomTerminal()
    {
        if (ActiveTerminal != ActiveTerminal.Custom)
        {
            Pair.ActiveTerminal = ActiveTerminal.Custom;
            ActiveTerminal = ActiveTerminal.Custom;
        }
    }

    [RelayCommand]
    private void ShowShellTerminal()
    {
        if (ActiveTerminal != ActiveTerminal.Shell)
        {
            Pair.ActiveTerminal = ActiveTerminal.Shell;
            ActiveTerminal = ActiveTerminal.Shell;
        }
    }

    partial void OnSplitRatioChanged(double value)
    {
        OnPropertyChanged(nameof(CustomColumnWidth));
        OnPropertyChanged(nameof(ShellColumnWidth));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnActiveTerminalChanged(ActiveTerminal value)
    {
        OnPropertyChanged(nameof(CurrentIcon));
        OnPropertyChanged(nameof(CurrentTerminalContent));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnGitStatusChanged(GitStatus? value)
    {
        OnPropertyChanged(nameof(TitleWithGit));
        OnPropertyChanged(nameof(GitStatusDisplay));
    }

    partial void OnIsCustomTerminalActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyTerminalActive));
    }

    partial void OnIsShellTerminalActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyTerminalActive));
    }

    /// <summary>
    /// Updates activity state from the terminal sessions.
    /// </summary>
    public void UpdateActivityState()
    {
        // Check for idle transitions
        Pair.CustomTerminal.CheckActivityState();
        Pair.ShellTerminal.CheckActivityState();

        // Update properties
        IsCustomTerminalActive = Pair.CustomTerminal.IsActive;
        IsShellTerminalActive = Pair.ShellTerminal.IsActive;
    }

    public void UpdateSplitRatioFromColumnWidths(double customWidth, double shellWidth)
    {
        var total = customWidth + shellWidth;
        if (total > 0)
        {
            // Setting the property will trigger OnSplitRatioChanged which updates computed properties
            SplitRatio = customWidth / total;
        }
    }

    /// <summary>
    /// Updates the detected links collection from terminal output.
    /// Only updates if the actual links have changed to preserve UI selection state.
    /// </summary>
    /// <param name="linkDetectionService">The link detection service to use.</param>
    public void UpdateDetectedLinks(LinkDetectionService linkDetectionService)
    {
        // Get recent output from both terminals
        var customOutput = Pair.CustomTerminal.GetRecentOutput(10000);
        var shellOutput = Pair.ShellTerminal.GetRecentOutput(10000);
        var combinedOutput = customOutput + "\n" + shellOutput;

        // Detect links
        var newLinks = linkDetectionService.DetectAllLinks(combinedOutput, Pair.WorkingDirectory, 15);

        // Check if links have changed by comparing URLs
        var currentUrls = DetectedLinks.Select(l => l.Url).ToList();
        var newUrls = newLinks.Select(l => l.Url).ToList();

        if (currentUrls.SequenceEqual(newUrls))
        {
            // No change, preserve selection state
            return;
        }

        // Links changed, update collection
        DetectedLinks.Clear();
        foreach (var link in newLinks)
        {
            DetectedLinks.Add(link);
        }

        OnPropertyChanged(nameof(HasDetectedLinks));
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
