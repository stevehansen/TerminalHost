using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class FilePreviewViewModel : ObservableObject
{
    private readonly IFilePreviewService _filePreviewService;
    private readonly IFileSystem _fileSystem;
    private readonly IMarkdownService _markdownService;
    private readonly IFilePickerService _filePickerService;
    private string? _currentFilePath;

    [ObservableProperty]
    private string _title = "File Preview";

    [ObservableProperty]
    private string _content = "";

    [ObservableProperty]
    private string _contentError = "";

    [ObservableProperty]
    private string _rawMarkdown = "";

    [ObservableProperty]
    private string _imageSource = "";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isText = true;

    [ObservableProperty]
    private bool _isMarkdown;

    [ObservableProperty]
    private bool _isImage;

    [ObservableProperty]
    private double _width = 900;

    [ObservableProperty]
    private double _height = 600;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenInEditorCommand))]
    private bool _canOpenInEditor;

    public event EventHandler<FileEditRequestedEventArgs>? OpenFileEditRequested;
    public event EventHandler<int>? ScrollToLineRequested;

    public FilePreviewViewModel(IFilePreviewService filePreviewService, IFileSystem fileSystem, IMarkdownService markdownService, IFilePickerService filePickerService)
    {
        _filePreviewService = filePreviewService;
        _fileSystem = fileSystem;
        _markdownService = markdownService;
        _filePickerService = filePickerService;
        // Initialize with placeholder text
        _content = "Select a file to preview.";
    }

    public void Open(string filePath, int? highlightLine = null)
    {
        _currentFilePath = filePath;
        
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        IsText = false;
        IsMarkdown = false;
        IsImage = false;

        // Check for Image
        if (IsImageExtension(extension))
        {
            IsImage = true;
            ImageSource = filePath;
            Title = Path.GetFileName(filePath);
            var size = _fileSystem.GetFileSize(filePath);
            Info = $"{FormatFileSize(size)}";
            
            CanOpenInEditor = true;
            IsOpen = true;
            return;
        }

        // Load text/markdown content via service
        var result = _filePreviewService.LoadFilePreview(filePath, highlightLine);

        if (result == null)
        {
            Title = "Error";
            IsText = true;
            ContentError = "Failed to load file preview service result.";
            Content = "";
            Info = "Error";
            IsOpen = true;
            return;
        }

        if (result.IsSuccess)
        {
            Title = result.FileName;
            Info = $"{result.LineCount:N0} lines • {FormatFileSize(result.FileSize)}";
            ContentError = "";

            if (extension == ".md")
            {
                IsMarkdown = true;
                RawMarkdown = result.Content ?? "";
            }
            else
            {
                IsText = true;
                Content = result.Content ?? "";
            }
        }
        else
        {
            Title = result.FileName;
            IsText = true;
            ContentError = result.Error!;
            Content = "";
            Info = $"Error • {FormatFileSize(result.FileSize)}";
        }

        CanOpenInEditor = !string.IsNullOrEmpty(_currentFilePath) && _fileSystem.FileExists(_currentFilePath);
        IsOpen = true;

        if (highlightLine.HasValue && result?.Content != null && IsText)
        {
            ScrollToLineRequested?.Invoke(this, highlightLine.Value);
        }
    }

    private static bool IsImageExtension(string ext)
    {
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico";
    }

    [RelayCommand]
    private async Task OpenDialogAsync(string initialDirectory)
    {
        var filters = new List<FilePickerFilter>
        {
            new("All Files", "*"),
            new("Code Files", "cs", "js", "ts", "py", "json", "xml"),
            new("Text Files", "txt", "md", "log")
        };

        var path = await _filePickerService.PickFileAsync(
            "Select File to Preview",
            filters,
            initialDirectory);

        if (!string.IsNullOrEmpty(path))
        {
            Open(path);
        }
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        _currentFilePath = null;
        Content = "Select a file to preview.";
        ContentError = "";
        RawMarkdown = "";
        ImageSource = "";
        IsText = true;
        IsMarkdown = false;
        IsImage = false;
        Title = "File Preview";
        Info = "";
    }

    [RelayCommand(CanExecute = nameof(CanOpenInEditor))]
    private void OpenInEditor()
    {
        if (!string.IsNullOrEmpty(_currentFilePath) && _fileSystem.FileExists(_currentFilePath))
        {
            IsOpen = false; // Close preview before opening editor
            OpenFileEditRequested?.Invoke(this, new FileEditRequestedEventArgs { FilePath = _currentFilePath });
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}