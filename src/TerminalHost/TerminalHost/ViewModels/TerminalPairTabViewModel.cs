using System.Diagnostics;
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
    private bool _isSplitView = false;

    public TerminalPair Pair { get; }

    public string CurrentIcon => ActiveTerminal == ActiveTerminal.Custom ? CustomIcon : ShellIcon;

    public ContentControl? CurrentTerminalContent => ActiveTerminal == ActiveTerminal.Custom
        ? CustomTerminalContent
        : ShellTerminalContent;

    public event EventHandler? CloseRequested;

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
        Debug.WriteLine($"[TerminalPairTabViewModel] SetTerminalControls called for {Title}");
        Debug.WriteLine($"[TerminalPairTabViewModel] CustomControl: {customControl != null}, ShellControl: {shellControl != null}");

        CustomTerminalContent = customControl;
        ShellTerminalContent = shellControl;

        Pair.CustomTerminal.SetTerminalControl(customControl);
        Pair.ShellTerminal.SetTerminalControl(shellControl);

        // Notify that CurrentTerminalContent has changed
        OnPropertyChanged(nameof(CurrentTerminalContent));

        Debug.WriteLine($"[TerminalPairTabViewModel] CurrentTerminalContent: {CurrentTerminalContent != null}");
    }

    [RelayCommand]
    private void SwitchTerminal()
    {
        Pair.SwitchTerminal();
        ActiveTerminal = Pair.ActiveTerminal;
        OnPropertyChanged(nameof(CurrentIcon));
        OnPropertyChanged(nameof(CurrentTerminalContent));
    }

    [RelayCommand]
    private void ShowCustomTerminal()
    {
        if (ActiveTerminal != ActiveTerminal.Custom)
        {
            Pair.ActiveTerminal = ActiveTerminal.Custom;
            ActiveTerminal = ActiveTerminal.Custom;
            OnPropertyChanged(nameof(CurrentIcon));
            OnPropertyChanged(nameof(CurrentTerminalContent));
        }
    }

    [RelayCommand]
    private void ShowShellTerminal()
    {
        if (ActiveTerminal != ActiveTerminal.Shell)
        {
            Pair.ActiveTerminal = ActiveTerminal.Shell;
            ActiveTerminal = ActiveTerminal.Shell;
            OnPropertyChanged(nameof(CurrentIcon));
            OnPropertyChanged(nameof(CurrentTerminalContent));
        }
    }

    [RelayCommand]
    private void ToggleSplitView()
    {
        IsSplitView = !IsSplitView;
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
