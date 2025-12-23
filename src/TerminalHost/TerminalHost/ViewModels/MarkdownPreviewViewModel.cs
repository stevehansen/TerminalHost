using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Markdown Preview (Ctrl+M).
/// Supports Panel, Popup, and Window display states.
/// </summary>
public partial class MarkdownPreviewViewModel : ObservableObject, IPanelableViewModel
{
    private readonly IMarkdownService _markdownService;
    private readonly IFileSystem _fileSystem;
    private readonly IDispatcherService _dispatcherService;
    private FileSystemWatcher? _fileWatcher;

    #region IPanelableViewModel Implementation

    public string PanelId => "markdownPreview";
    public string PanelTitle => "Markdown Preview";
    public string PanelIcon => "\uD83D\uDCDD"; // 📝

    public IEnumerable<PanelHeaderCommand>? HeaderCommands => null;
    public string? StatusText => null;

    [ObservableProperty]
    private PanelDisplayState _displayState = PanelDisplayState.Window;

    [ObservableProperty]
    private PanelSide _preferredSide = PanelSide.Right;

    [ObservableProperty]
    private double _width = 800;

    [ObservableProperty]
    private double _height = 600;

    public ICommand DockCommand { get; }
    public ICommand UndockCommand { get; }
    public ICommand DetachCommand { get; }
    ICommand IPanelableViewModel.CloseCommand => CloseCommand;

    public event EventHandler<PanelStateChangeRequestedEventArgs>? StateChangeRequested;

    #endregion

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _renderedHtml = "";

    [ObservableProperty]
    private bool _autoReload = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>
    /// Gets just the filename from the path.
    /// </summary>
    public string FileName => string.IsNullOrEmpty(FilePath)
        ? "Markdown Preview"
        : System.IO.Path.GetFileName(FilePath);

    /// <summary>
    /// Gets the window title.
    /// </summary>
    public string WindowTitle => string.IsNullOrEmpty(FilePath)
        ? "Markdown Preview"
        : $"{System.IO.Path.GetFileName(FilePath)} - Markdown Preview";

    /// <summary>
    /// Event raised when the window should close.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Event raised when the window needs to be shown.
    /// </summary>
    public event EventHandler? ShowRequested;

    public MarkdownPreviewViewModel(IMarkdownService markdownService, IFileSystem fileSystem, IDispatcherService dispatcherService)
    {
        _markdownService = markdownService;
        _fileSystem = fileSystem;
        _dispatcherService = dispatcherService;

        // Initialize panel commands
        DockCommand = new RelayCommand<PanelSide?>(OnDock);
        UndockCommand = new RelayCommand(OnUndock);
        DetachCommand = new RelayCommand(OnDetach);
    }

    #region Panel Command Handlers

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

    /// <summary>
    /// Sets the display state directly (called by panel host when state changes are applied).
    /// </summary>
    public void SetDisplayState(PanelDisplayState state, PanelSide? side = null)
    {
        DisplayState = state;
        if (side.HasValue)
        {
            PreferredSide = side.Value;
        }
    }

    #endregion

    /// <summary>
    /// Opens the preview for a specific file.
    /// </summary>
    public async Task OpenAsync(string filePath)
    {
        FilePath = filePath;
        IsOpen = true;
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(WindowTitle));

        await RefreshAsync();
        SetupFileWatcher();

        // Request window to be shown
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called when the window is closed by the user.
    /// </summary>
    public void OnWindowClosed()
    {
        IsOpen = false;
        _fileWatcher?.Dispose();
        _fileWatcher = null;
    }

    /// <summary>
    /// Toggles the preview open/closed.
    /// </summary>
    public void Toggle()
    {
        if (IsOpen)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetupFileWatcher()
    {
        _fileWatcher?.Dispose();

        if (string.IsNullOrEmpty(FilePath) || !_fileSystem.FileExists(FilePath))
            return;

        var directory = System.IO.Path.GetDirectoryName(FilePath);
        var filename = System.IO.Path.GetFileName(FilePath);

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(filename))
            return;

        try
        {
            _fileWatcher = new FileSystemWatcher(directory, filename)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += async (_, _) =>
            {
                if (AutoReload && IsOpen)
                {
                    // Add a small delay to avoid reading while file is still being written
                    await Task.Delay(100);
                    await _dispatcherService.InvokeAsync(async () =>
                    {
                        await RefreshAsync();
                    });
                }
            };
        }
        catch
        {
            // Ignore file watcher setup errors
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            RenderedHtml = "";
            StatusMessage = "No file loaded";
            return;
        }

        IsLoading = true;
        StatusMessage = "Loading...";

        try
        {
            RenderedHtml = await _markdownService.ConvertFileToHtmlAsync(FilePath);
            StatusMessage = $"Last updated: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleAutoReload()
    {
        AutoReload = !AutoReload;
        StatusMessage = AutoReload ? "Auto-reload enabled" : "Auto-reload disabled";
    }
}
