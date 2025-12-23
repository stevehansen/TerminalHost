using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.ViewModels;

/// <summary>
/// Wrapper ViewModel that adapts FileExplorerViewModel to the panel system.
/// Implements IPanelableViewModel to allow the file explorer to be docked as a panel,
/// shown as a popup, or detached to a window.
/// </summary>
public partial class FileExplorerPanelViewModel : ObservableObject, IPanelableViewModel
{
    private readonly FileExplorerViewModel _explorerViewModel;

    public FileExplorerPanelViewModel(FileExplorerViewModel explorerViewModel)
    {
        _explorerViewModel = explorerViewModel;

        DockCommand = new RelayCommand<PanelSide?>(OnDock);
        UndockCommand = new RelayCommand(OnUndock);
        DetachCommand = new RelayCommand(OnDetach);
        CloseCommand = new RelayCommand(OnClose);
    }

    /// <summary>
    /// Gets the wrapped FileExplorerViewModel for binding in the view.
    /// </summary>
    public FileExplorerViewModel ExplorerViewModel => _explorerViewModel;

    #region IPanelableViewModel Implementation

    public string PanelId => "fileExplorer";

    public string PanelTitle => "Explorer";

    public string PanelIcon => "\uD83D\uDCC1"; // 📁

    // FileExplorerView has its own toolbar with better Refresh button (includes pending indicator)
    public IEnumerable<PanelHeaderCommand>? HeaderCommands => null;

    public string? StatusText => _explorerViewModel.LastChangedFile != null
        ? $"Changed: {System.IO.Path.GetFileName(_explorerViewModel.LastChangedFile)}"
        : null;

    [ObservableProperty]
    private PanelDisplayState _displayState = PanelDisplayState.Panel;

    [ObservableProperty]
    private PanelSide _preferredSide = PanelSide.Right;

    [ObservableProperty]
    private bool _isOpen = true;

    [ObservableProperty]
    private double _width = 350;

    [ObservableProperty]
    private double _height = 500;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    public ICommand DockCommand { get; }
    public ICommand UndockCommand { get; }
    public ICommand DetachCommand { get; }
    public ICommand CloseCommand { get; }

    public event EventHandler<PanelStateChangeRequestedEventArgs>? StateChangeRequested;

    #endregion

    #region Command Handlers

    private void OnDock(PanelSide? side)
    {
        var dockSide = side ?? PreferredSide;
        StateChangeRequested?.Invoke(this, new PanelStateChangeRequestedEventArgs(PanelDisplayState.Panel, dockSide));
    }

    private void OnUndock()
    {
        StateChangeRequested?.Invoke(this, new PanelStateChangeRequestedEventArgs(PanelDisplayState.Popup));
    }

    private void OnDetach()
    {
        StateChangeRequested?.Invoke(this, new PanelStateChangeRequestedEventArgs(PanelDisplayState.Window));
    }

    private void OnClose()
    {
        IsOpen = false;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Updates the display state without raising events.
    /// Called by the panel host when state changes are applied.
    /// </summary>
    public void SetDisplayState(PanelDisplayState state, PanelSide? side = null)
    {
        DisplayState = state;
        if (side.HasValue)
        {
            PreferredSide = side.Value;
        }
    }

    /// <summary>
    /// Initializes the explorer with the specified working directory.
    /// Delegates to the wrapped FileExplorerViewModel.
    /// </summary>
    public Task InitializeAsync(string workingDirectory)
    {
        return _explorerViewModel.InitializeAsync(workingDirectory);
    }

    #endregion
}
