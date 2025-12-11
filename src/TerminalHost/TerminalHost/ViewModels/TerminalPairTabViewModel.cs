using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWindowsTerminalControl;
using TerminalHost.Domain;

namespace TerminalHost.ViewModels;

public partial class TerminalPairTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Terminal";

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

    [RelayCommand]
    private void ToggleSplitView()
    {
        IsSplitView = !IsSplitView;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
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

    public void UpdateSplitRatioFromColumnWidths(double customWidth, double shellWidth)
    {
        var total = customWidth + shellWidth;
        if (total > 0)
        {
            // Setting the property will trigger OnSplitRatioChanged which updates computed properties
            SplitRatio = customWidth / total;
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
