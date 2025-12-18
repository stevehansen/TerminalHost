using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class FilePreviewViewModel : ObservableObject
{
    private readonly IFilePreviewService _filePreviewService;
    private readonly IFileSystem _fileSystem;
    private readonly IMarkdownService _markdownService;
    private string? _currentFilePath;

    [ObservableProperty]
    private string _title = "File Preview";

    [ObservableProperty]
    private FlowDocument _content = new();

    [ObservableProperty]
    private string _renderedHtml = "";

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

    public FilePreviewViewModel(IFilePreviewService filePreviewService, IFileSystem fileSystem, IMarkdownService markdownService)
    {
        _filePreviewService = filePreviewService;
        _fileSystem = fileSystem;
        _markdownService = markdownService;
        // Initialize with an empty document
        _content = CreateInfoDocument("Select a file to preview.");
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
            Content = CreateErrorDocument("Failed to load file preview service result.");
            Info = "Error";
            IsOpen = true;
            return;
        }

        if (result.IsSuccess)
        {
            Title = result.FileName;
            Info = $"{result.LineCount:N0} lines • {FormatFileSize(result.FileSize)}";

            if (extension == ".md")
            {
                IsMarkdown = true;
                RenderedHtml = _markdownService.ConvertToHtml(result.Content ?? "");
            }
            else
            {
                IsText = true;
                Content = result.Document!;
            }
        }
        else
        {
            Title = result.FileName;
            IsText = true;
            Content = CreateErrorDocument(result.Error!);
            Info = $"Error • {FormatFileSize(result.FileSize)}";
        }

        CanOpenInEditor = !string.IsNullOrEmpty(_currentFilePath) && _fileSystem.FileExists(_currentFilePath);
        IsOpen = true;

        if (highlightLine.HasValue && result?.Document != null && IsText)
        {
            ScrollToLineRequested?.Invoke(this, highlightLine.Value);
        }
    }

    private static bool IsImageExtension(string ext)
    {
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico";
    }

    [RelayCommand]
    private void OpenDialog(string initialDirectory)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select File to Preview",
            Filter = "All Files (*.*)|*.*|Code Files (*.cs;*.js;*.ts;*.py;*.json;*.xml)|*.cs;*.js;*.ts;*.py;*.json;*.xml|Text Files (*.txt;*.md;*.log)|*.txt;*.md;*.log",
            FilterIndex = 1,
            InitialDirectory = initialDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            Open(dialog.FileName);
        }
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        _currentFilePath = null;
        Content = CreateInfoDocument("Select a file to preview.");
        RenderedHtml = "";
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

    private static FlowDocument CreateInfoDocument(string message)
    {
        return new FlowDocument
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["FontFamilyMonospace"],
            FontSize = (double)Application.Current.Resources["FontSizeCode"],
            PagePadding = new Thickness(16),
            PageWidth = 10000
        };
    }

    private static FlowDocument CreateErrorDocument(string error)
    {
        var document = new FlowDocument
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["FontFamilyMonospace"],
            FontSize = (double)Application.Current.Resources["FontSizeCode"],
            PagePadding = new Thickness(16),
            PageWidth = 10000
        };

        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run(error)
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0x48, 0x48))
        });
        document.Blocks.Add(paragraph);

        return document;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}