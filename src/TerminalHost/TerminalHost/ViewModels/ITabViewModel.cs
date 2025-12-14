using System.ComponentModel;

namespace TerminalHost.ViewModels;

public interface ITabViewModel : INotifyPropertyChanged
{
    string Title { get; }
    string TabIcon { get; }
    string WorkingDirectory { get; }
    bool IsCloseable { get; }
    bool IsAnyTerminalActive { get; }

    /// <summary>
    /// The title to display in the tab header. May include additional info like git branch.
    /// </summary>
    string DisplayTitle { get; }

    event EventHandler? CloseRequested;
}
