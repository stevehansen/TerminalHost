using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Markdown Preview Window (Ctrl+M).
/// </summary>
public partial class MarkdownPreviewViewModel : ObservableObject
{
    private readonly IMarkdownService _markdownService;
    private readonly IFileSystem _fileSystem;
    private readonly IDispatcherService _dispatcherService;
    private FileSystemWatcher? _fileWatcher;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _rawMarkdown = "";

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
    }

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
            RawMarkdown = "";
            StatusMessage = "No file loaded";
            return;
        }

        IsLoading = true;
        StatusMessage = "Loading...";

        try
        {
            RawMarkdown = await _fileSystem.ReadAllTextAsync(FilePath);
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
