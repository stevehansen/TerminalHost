using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class FileViewerViewModel : ObservableObject
{
    private readonly IFilePreviewService _filePreviewService;
    private readonly IFileEditService _fileEditService;
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogService;
    private string? _currentFilePath;
    private Encoding? _currentEncoding;
    private string? _originalContent;

    // Image file extensions
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff", ".tif"
    };

    [ObservableProperty]
    private string _title = "File Viewer";

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string _fileName = "";

    // Preview mode content
    [ObservableProperty]
    private FlowDocument _previewDocument = new();

    // Edit mode content
    [ObservableProperty]
    private string _editContent = "";

    [ObservableProperty]
    private string _lineNumbers = "";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private string _cursorInfo = "";

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private FileViewerMode _mode = FileViewerMode.Preview;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isDetached;

    // For embedded viewer sizing
    [ObservableProperty]
    private double _width = 900;

    [ObservableProperty]
    private double _height = 600;

    // For popup positioning
    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    // Image mode
    [ObservableProperty]
    private bool _isImageMode;

    [ObservableProperty]
    private ImageSource? _imageSource;

    [ObservableProperty]
    private string _imageInfo = "";

    // Computed properties
    public bool IsPreviewMode => Mode == FileViewerMode.Preview && !IsImageMode;
    public bool IsEditMode => Mode == FileViewerMode.Edit && !IsImageMode;
    public bool CanSave => IsEditMode && IsModified && !IsReadOnly;

    // Events for view interaction
    public event EventHandler<int>? ScrollToLineRequested;
    public event EventHandler<int>? SetCaretIndexRequested;
    public event EventHandler? DetachRequested;

    public FileViewerViewModel(
        IFilePreviewService filePreviewService,
        IFileEditService fileEditService,
        IFileSystem fileSystem,
        IDialogService dialogService)
    {
        _filePreviewService = filePreviewService;
        _fileEditService = fileEditService;
        _fileSystem = fileSystem;
        _dialogService = dialogService;

        _previewDocument = CreateInfoDocument("Select a file to view.");
    }

    public void Open(string filePath, FileViewerMode mode = FileViewerMode.Preview, int? goToLine = null)
    {
        _currentFilePath = filePath;
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        Title = FileName;

        // Check if it's an image file
        var extension = Path.GetExtension(filePath);
        if (ImageExtensions.Contains(extension))
        {
            LoadImage(filePath);
            IsOpen = true;
            return;
        }

        // Reset image mode for text files
        IsImageMode = false;
        ImageSource = null;
        ImageInfo = "";
        Mode = mode;

        if (mode == FileViewerMode.Preview)
        {
            LoadPreview(filePath, goToLine);
        }
        else
        {
            LoadForEdit(filePath, goToLine);
        }

        IsOpen = true;
    }

    [RelayCommand]
    private void OpenDialog(string? initialDirectory)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select File to Open",
            Filter = "All Files (*.*)|*.*|Code Files (*.cs;*.js;*.ts;*.py;*.json;*.xml)|*.cs;*.js;*.ts;*.py;*.json;*.xml|Text Files (*.txt;*.md;*.log)|*.txt;*.md;*.log|Images (*.png;*.jpg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
            FilterIndex = 1,
            InitialDirectory = initialDirectory ?? ""
        };

        if (dialog.ShowDialog() == true)
        {
            Open(dialog.FileName);
        }
    }

    private void LoadImage(string filePath)
    {
        try
        {
            IsImageMode = true;
            Mode = FileViewerMode.Preview; // Images are always in preview mode

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            ImageSource = bitmap;

            var fileInfo = new FileInfo(filePath);
            ImageInfo = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} px - {FormatFileSize(fileInfo.Length)}";
            Info = ImageInfo;
            Title = FileName;
        }
        catch (Exception ex)
        {
            IsImageMode = false;
            ImageSource = null;
            Title = "Error";
            PreviewDocument = CreateErrorDocument($"Failed to load image: {ex.Message}");
            Info = "Error loading image";
        }

        OnPropertyChanged(nameof(IsPreviewMode));
        OnPropertyChanged(nameof(IsEditMode));
    }

    private void LoadPreview(string filePath, int? highlightLine)
    {
        var result = _filePreviewService.LoadFilePreview(filePath, highlightLine);

        if (result == null)
        {
            Title = "Error";
            PreviewDocument = CreateErrorDocument("Failed to load file preview.");
            Info = "Error";
            return;
        }

        if (result.IsSuccess)
        {
            Title = result.FileName;
            PreviewDocument = result.Document!;
            Info = $"{result.LineCount:N0} lines - {FormatFileSize(result.FileSize)}";
        }
        else
        {
            Title = result.FileName;
            PreviewDocument = CreateErrorDocument(result.Error!);
            Info = $"Error - {FormatFileSize(result.FileSize)}";
        }

        if (highlightLine.HasValue && result?.Document != null)
        {
            ScrollToLineRequested?.Invoke(this, highlightLine.Value);
        }
    }

    private void LoadForEdit(string filePath, int? goToLine)
    {
        var result = _fileEditService.LoadFile(filePath);

        if (!result.IsSuccess)
        {
            _dialogService.ShowError(result.Error ?? "Unknown error loading file");
            // Fall back to preview mode
            Mode = FileViewerMode.Preview;
            LoadPreview(filePath, goToLine);
            return;
        }

        _currentEncoding = result.Encoding;
        _originalContent = result.Content;
        EditContent = result.Content ?? "";
        IsModified = false;
        IsReadOnly = result.IsReadOnly;

        Title = result.FileName + (IsReadOnly ? " (Read-only)" : "");
        UpdateEditInfo(result.LineCount, result.FileSize, result.IsReadOnly);
        UpdateLineNumbers();

        if (goToLine.HasValue)
        {
            GoToLine(goToLine.Value);
        }
    }

    partial void OnModeChanged(FileViewerMode value)
    {
        OnPropertyChanged(nameof(IsPreviewMode));
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(CanSave));

        // Don't switch modes for images
        if (IsImageMode) return;

        // Reload content when switching modes
        if (_currentFilePath != null)
        {
            if (value == FileViewerMode.Preview)
            {
                LoadPreview(_currentFilePath, null);
            }
            else if (value == FileViewerMode.Edit)
            {
                LoadForEdit(_currentFilePath, null);
            }
        }
    }

    partial void OnIsImageModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPreviewMode));
        OnPropertyChanged(nameof(IsEditMode));
    }

    partial void OnEditContentChanged(string value)
    {
        IsModified = value != _originalContent;
        OnPropertyChanged(nameof(CanSave));
        UpdateLineNumbers();
    }

    [RelayCommand]
    private void SwitchToPreview()
    {
        if (IsModified)
        {
            if (!_dialogService.ShowConfirmation(
                "You have unsaved changes. Switch to preview mode and lose changes?",
                "Unsaved Changes"))
                return;
        }

        Mode = FileViewerMode.Preview;
    }

    [RelayCommand]
    private void SwitchToEdit()
    {
        // Can't edit images
        if (IsImageMode) return;
        Mode = FileViewerMode.Edit;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrEmpty(_currentFilePath) || IsReadOnly) return;

        var result = _fileEditService.SaveFile(_currentFilePath, EditContent, _currentEncoding);

        if (result.Success)
        {
            _originalContent = EditContent;
            IsModified = false;
            OnPropertyChanged(nameof(CanSave));

            var fileSize = _fileSystem.GetFileSize(_currentFilePath);
            var lineCount = EditContent.Split('\n').Length;
            Info = $"{lineCount:N0} lines - {FormatFileSize(fileSize)} - Saved";
        }
        else
        {
            _dialogService.ShowError(result.Error ?? "Unknown error saving file");
        }
    }

    [RelayCommand]
    private void Reload()
    {
        if (string.IsNullOrEmpty(_currentFilePath)) return;

        if (IsModified)
        {
            if (!_dialogService.ShowConfirmation(
                "You have unsaved changes. Reload and lose changes?",
                "Confirm Reload"))
                return;
        }

        Open(_currentFilePath, Mode);
    }

    [RelayCommand]
    public void Close()
    {
        if (IsModified && Mode == FileViewerMode.Edit && !IsImageMode)
        {
            if (!_dialogService.ShowConfirmation(
                "You have unsaved changes. Close without saving?",
                "Unsaved Changes"))
                return;
        }

        IsOpen = false;
        _currentFilePath = null;
        _currentEncoding = null;
        _originalContent = null;
        IsModified = false;
        EditContent = "";
        PreviewDocument = CreateInfoDocument("Select a file to view.");
        Title = "File Viewer";
        Info = "";

        // Reset image mode
        IsImageMode = false;
        ImageSource = null;
        ImageInfo = "";
    }

    [RelayCommand]
    private void Detach()
    {
        DetachRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void GoToLineDialog()
    {
        var lineCount = EditContent.Split('\n').Length;
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            $"Enter line number (1-{lineCount}):",
            "Go to Line",
            "1");

        if (!string.IsNullOrEmpty(input) && int.TryParse(input, out var lineNumber))
        {
            GoToLine(lineNumber);
        }
    }

    private void GoToLine(int lineNumber)
    {
        var lines = EditContent.Split('\n');
        var targetLine = Math.Max(0, Math.Min(lineNumber - 1, lines.Length - 1));

        int charIndex = 0;
        for (int i = 0; i < targetLine; i++)
        {
            charIndex += lines[i].Length + 1;
        }

        SetCaretIndexRequested?.Invoke(this, charIndex);
        ScrollToLineRequested?.Invoke(this, targetLine);
    }

    public void UpdateCursorInfo(int caretIndex)
    {
        var text = EditContent;
        var textUpToCaret = text.Substring(0, Math.Min(caretIndex, text.Length));
        var line = textUpToCaret.Count(c => c == '\n') + 1;
        var lastNewline = textUpToCaret.LastIndexOf('\n');
        var column = lastNewline < 0 ? caretIndex + 1 : caretIndex - lastNewline;

        CursorInfo = $"Ln {line}, Col {column}";
    }

    private void UpdateLineNumbers()
    {
        var lineCount = EditContent.Split('\n').Length;
        var sb = new StringBuilder();
        for (int i = 1; i <= lineCount; i++)
        {
            sb.AppendLine(i.ToString());
        }
        LineNumbers = sb.ToString().TrimEnd();
    }

    private void UpdateEditInfo(int lineCount, long fileSize, bool isReadOnly, string? status = null)
    {
        var info = $"{lineCount:N0} lines - {FormatFileSize(fileSize)}";
        if (isReadOnly) info += " - Read-only";
        if (status != null) info += " - " + status;
        Info = info;
    }

    private static FlowDocument CreateInfoDocument(string message)
    {
        return new FlowDocument
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            FontFamily = (System.Windows.Media.FontFamily)System.Windows.Application.Current.Resources["FontFamilyMonospace"],
            FontSize = (double)System.Windows.Application.Current.Resources["FontSizeCode"],
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
            FontFamily = (System.Windows.Media.FontFamily)System.Windows.Application.Current.Resources["FontFamilyMonospace"],
            FontSize = (double)System.Windows.Application.Current.Resources["FontSizeCode"],
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
