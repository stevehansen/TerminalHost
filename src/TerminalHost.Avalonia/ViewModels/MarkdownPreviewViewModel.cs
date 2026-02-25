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
    private readonly IConfigurationService _configurationService;
    private readonly IProcessService _processService;
    private readonly IToastService _toastService;
    private readonly IAiExecutionService _aiExecutionService;
    private readonly IClipboardService _clipboardService;
    private FileSystemWatcher? _fileWatcher;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImproveMarkdownCommand))]
    private string _filePath = "";

    [ObservableProperty]
    private string _rawMarkdown = "";

    [ObservableProperty]
    private bool _autoReload = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    // AI: Markdown improvements
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMarkdownImprovements))]
    private string _markdownImprovements = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImproveMarkdownCommand))]
    private bool _isImprovingMarkdown;

    public bool HasMarkdownImprovements => !string.IsNullOrEmpty(MarkdownImprovements);

    public bool CanImproveMarkdown => !string.IsNullOrEmpty(FilePath) && !IsImprovingMarkdown;

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

    public MarkdownPreviewViewModel(
        IMarkdownService markdownService,
        IFileSystem fileSystem,
        IDispatcherService dispatcherService,
        IConfigurationService configurationService,
        IProcessService processService,
        IToastService toastService,
        IAiExecutionService aiExecutionService,
        IClipboardService clipboardService)
    {
        _markdownService = markdownService;
        _fileSystem = fileSystem;
        _dispatcherService = dispatcherService;
        _configurationService = configurationService;
        _processService = processService;
        _toastService = toastService;
        _aiExecutionService = aiExecutionService;
        _clipboardService = clipboardService;
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
        MarkdownImprovements = "";
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

    #region AI: Improve Markdown

    [RelayCommand(CanExecute = nameof(CanImproveMarkdown))]
    private async Task ImproveMarkdownAsync()
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        if (!_aiExecutionService.IsAiAvailable()) return;

        string content;
        try { content = _fileSystem.ReadAllText(FilePath); }
        catch (Exception ex) { _toastService.Show($"Could not read file: {ex.Message}", ToastType.Error); return; }

        if (content.Length > 8000) content = content[..8000] + "\n[truncated]";

        var workDir = System.IO.Path.GetDirectoryName(FilePath) ?? "";
        var prompt = $"Review this markdown document and suggest improvements. Cover: clarity, structure, completeness, broken relative links, missing sections. Format as short bullet points grouped by category. Be concise.\n\n{content}";

        IsImprovingMarkdown = true;
        try
        {
            var result = await _aiExecutionService.ExecuteAsync(prompt, workDir, "Improving markdown");

            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                MarkdownImprovements = result.Output.Trim();
        }
        finally { IsImprovingMarkdown = false; }
    }

    [RelayCommand]
    private void DismissMarkdownImprovements() => MarkdownImprovements = "";

    [RelayCommand]
    private async Task CopyMarkdownImprovementsAsync()
    {
        if (!string.IsNullOrEmpty(MarkdownImprovements))
        {
            await _clipboardService.SetTextAsync(MarkdownImprovements);
            _toastService.Show("Copied to clipboard", ToastType.Success);
        }
    }

    #endregion
}
