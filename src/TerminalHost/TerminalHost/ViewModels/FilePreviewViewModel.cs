using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using TerminalHost.Domain;
using TerminalHost.Services;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace TerminalHost.ViewModels;

public partial class FilePreviewViewModel : ObservableObject
{
    private readonly FilePreviewService _filePreviewService;
    private string? _currentFilePath;

    [ObservableProperty]
    private string _title = "File Preview";

    [ObservableProperty]
    private FlowDocument _content = new();

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private bool _isOpen;

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

    public FilePreviewViewModel(FilePreviewService filePreviewService)
    {
        _filePreviewService = filePreviewService;
        // Initialize with an empty document
        _content = CreateInfoDocument("Select a file to preview.");
    }

    public void Open(string filePath, int? highlightLine = null)
    {
        Console.WriteLine($"[FilePreviewViewModel] Open called for: {filePath}, line: {highlightLine}");
        var result = _filePreviewService.LoadFilePreview(filePath, highlightLine);
        
        _currentFilePath = result?.FilePath;

        if (result == null)
        {
            Title = "Error";
            Content = CreateErrorDocument("Failed to load file preview service result.");
            Info = "Error";
            IsOpen = true;
            return;
        }

        if (result.IsSuccess)
        {
            Title = result.FileName;
            Content = result.Document!;
            Info = $"{result.LineCount:N0} lines • {FormatFileSize(result.FileSize)}";
        }
        else
        {
            Title = result.FileName;
            Content = CreateErrorDocument(result.Error!);
            Info = $"Error • {FormatFileSize(result.FileSize)}";
        }

        CanOpenInEditor = !string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath);
        IsOpen = true;

        if (highlightLine.HasValue && result?.Document != null)
        {
            ScrollToLineRequested?.Invoke(this, highlightLine.Value);
        }
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
        Title = "File Preview";
        Info = "";
    }

    [RelayCommand(CanExecute = nameof(CanOpenInEditor))]
    private void OpenInEditor()
    {
        if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
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
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code NF, Consolas, Courier New"),
            FontSize = 13,
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
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code NF, Consolas, Courier New"),
            FontSize = 13,
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
