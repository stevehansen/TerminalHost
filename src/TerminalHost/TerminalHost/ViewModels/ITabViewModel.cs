using System.ComponentModel;

namespace TerminalHost.ViewModels;

public interface ITabViewModel : INotifyPropertyChanged
{
    string Title { get; }
    string TabIcon { get; }
    string WorkingDirectory { get; }
    bool IsCloseable { get; }
    bool IsAnyTerminalActive { get; }
    event EventHandler? CloseRequested;
}
