using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Markdown Preview (Ctrl+M).
/// Supports Panel, Popup, and Window display states.
/// </summary>
public partial class MarkdownPreviewViewModel : BasePanelViewModel, IPanelPlacement
{
    public PanelZone PreferredZone => PanelZone.Center;

    private readonly IMarkdownService _markdownService;
    private readonly IFileSystem _fileSystem;
    private readonly IDispatcherService _dispatcherService;
    private readonly IProcessService _processService;
    private readonly IConfigurationService _configurationService;
    private readonly IToastService _toastService;
    private readonly IAiExecutionService _aiService;
    private FileSystemWatcher? _fileWatcher;

    #region IPanelableViewModel Implementation

    public override string PanelId => "markdownPreview";
    public override string PanelTitle => string.IsNullOrEmpty(FilePath) ? "Markdown" : FileName;
    public override string PanelIcon => "MD";
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    public override IEnumerable<PanelHeaderCommand>? HeaderCommands => new[]
    {
        new PanelHeaderCommand
        {
            Icon = "↻",
            Tooltip = "Refresh (F5)",
            Command = RefreshCommand
        },
        new PanelHeaderCommand
        {
            Icon = AutoReload ? "●" : "○",
            Tooltip = AutoReload ? "Auto-reload: ON (click to disable)" : "Auto-reload: OFF (click to enable)",
            Command = ToggleAutoReloadCommand
        }
    };

    public override string? StatusText => StatusMessage;

    #endregion

    #region Markdown Properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImproveMarkdownCommand))]
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
        : Path.GetFileName(FilePath);

    /// <summary>
    /// Gets the window title.
    /// </summary>
    public string WindowTitle => string.IsNullOrEmpty(FilePath)
        ? "Markdown Preview"
        : $"{Path.GetFileName(FilePath)} - Markdown Preview";

    /// <summary>
    /// Base path for resolving relative resources in markdown (images, links).
    /// </summary>
    public string? MarkdownBasePath => string.IsNullOrEmpty(FilePath)
        ? null
        : Path.GetDirectoryName(FilePath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMarkdownImprovements))]
    private string _markdownImprovements = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImproveMarkdownCommand))]
    private bool _isImprovingMarkdown;

    public bool HasMarkdownImprovements => !string.IsNullOrEmpty(MarkdownImprovements);

    public bool CanImproveMarkdown => !string.IsNullOrEmpty(FilePath) && !IsImprovingMarkdown;

    #endregion

    #region Events

    /// <summary>
    /// Event raised when the window should close.
    /// </summary>
    public event EventHandler? CloseRequested;

    #endregion

    public MarkdownPreviewViewModel(
        IMarkdownService markdownService,
        IFileSystem fileSystem,
        IDispatcherService dispatcherService,
        IProcessService processService,
        IConfigurationService configurationService,
        IToastService toastService,
        IAiExecutionService aiExecutionService)
    {
        _markdownService = markdownService;
        _fileSystem = fileSystem;
        _dispatcherService = dispatcherService;
        _processService = processService;
        _configurationService = configurationService;
        _aiService = aiExecutionService;
        _toastService = toastService;

        // Set defaults for markdown preview - defaults to Window
        DisplayState = PanelDisplayState.Window;
        Width = 800;
        Height = 600;
    }

    #region Public Methods

    /// <summary>
    /// Opens the preview for a specific file.
    /// </summary>
    public async Task OpenAsync(string filePath)
    {
        FilePath = filePath;
        IsOpen = true;
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(PanelTitle));
        OnPropertyChanged(nameof(MarkdownBasePath));

        await RefreshAsync();
        SetupFileWatcher();
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

    #endregion

    #region Commands

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
        IsOpen = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleAutoReload()
    {
        AutoReload = !AutoReload;
        StatusMessage = AutoReload ? "Auto-reload enabled" : "Auto-reload disabled";
        OnPropertyChanged(nameof(HeaderCommands));
    }

    [RelayCommand(CanExecute = nameof(CanImproveMarkdown))]
    private async Task ImproveMarkdownAsync()
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        if (!_aiService.IsAiAvailable()) return;

        string content;
        try { content = _fileSystem.ReadAllText(FilePath); }
        catch (Exception ex) { _toastService.Show($"Could not read file: {ex.Message}", ToastType.Error); return; }

        if (content.Length > 8000) content = content[..8000] + "\n[truncated]";

        var workDir = Path.GetDirectoryName(FilePath) ?? "";
        var prompt = $"Review this markdown document and suggest improvements. Cover: clarity, structure, completeness, broken relative links, missing sections. Format as short bullet points grouped by category. Be concise.\n\n{content}";

        IsImprovingMarkdown = true;
        try
        {
            var result = await _aiService.ExecuteAsync(prompt, workDir, "Improving markdown");
            if (result.Success)
                MarkdownImprovements = result.Output!;
        }
        finally { IsImprovingMarkdown = false; }
    }

    [RelayCommand]
    private void DismissMarkdownImprovements() => MarkdownImprovements = "";

    #endregion

    #region Private Methods

    private void SetupFileWatcher()
    {
        _fileWatcher?.Dispose();

        if (string.IsNullOrEmpty(FilePath) || !_fileSystem.FileExists(FilePath))
            return;

        var directory = Path.GetDirectoryName(FilePath);
        var filename = Path.GetFileName(FilePath);

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

    #endregion
}
