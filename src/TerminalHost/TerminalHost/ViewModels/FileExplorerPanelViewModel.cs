using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// Wrapper ViewModel that adapts FileExplorerViewModel to the panel system.
/// Implements IPanelableViewModel to allow the file explorer to be docked as a panel,
/// shown as a popup, or detached to a window.
/// </summary>
public partial class FileExplorerPanelViewModel : BasePanelViewModel
{
    private readonly FileExplorerViewModel _explorerViewModel;

    public FileExplorerPanelViewModel(FileExplorerViewModel explorerViewModel)
    {
        _explorerViewModel = explorerViewModel;

        // Set defaults for file explorer
        DisplayState = PanelDisplayState.Panel;
        Width = 350;
        Height = 500;
        IsOpen = true;
    }

    /// <summary>
    /// Gets the wrapped FileExplorerViewModel for binding in the view.
    /// </summary>
    public FileExplorerViewModel ExplorerViewModel => _explorerViewModel;

    #region IPanelableViewModel Implementation

    public override string PanelId => "fileExplorer";
    public override string PanelTitle => "Explorer";
    public override string PanelIcon => "\uD83D\uDCC1"; // 📁
    public override PanelSizePreset SizePreset => PanelSizePreset.Compact;

    // FileExplorerView has its own toolbar with better Refresh button (includes pending indicator)
    public override IEnumerable<PanelHeaderCommand>? HeaderCommands => null;

    public override string? StatusText => _explorerViewModel.LastChangedFile != null
        ? $"Changed: {System.IO.Path.GetFileName(_explorerViewModel.LastChangedFile)}"
        : null;

    #endregion

    #region Public Methods

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
