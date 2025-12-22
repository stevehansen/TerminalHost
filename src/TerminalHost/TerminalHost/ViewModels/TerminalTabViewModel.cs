using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;

namespace TerminalHost.ViewModels;

public partial class TerminalTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Terminal";

    [ObservableProperty]
    private string _icon = "💻";

    [ObservableProperty]
    private SessionState _state = SessionState.Running;

    [ObservableProperty]
    private ContentControl? _terminalContent;

    public TerminalSession Session { get; }

    public event EventHandler? CloseRequested;

    public TerminalTabViewModel(TerminalSession session)
    {
        Session = session;
        Title = session.Profile.Name;
        Icon = session.Profile.Icon ?? "💻";
        State = session.State;

        session.ProcessExited += OnProcessExited;
    }

    private void OnProcessExited(object? sender, int exitCode)
    {
        State = SessionState.Exited;
        Title = $"{Session.Profile.Name} [Exited: {exitCode}]";
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
