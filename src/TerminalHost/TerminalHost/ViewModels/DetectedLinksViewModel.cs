using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows; // For MessageBox, for now
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class DetectedLinksViewModel : ObservableObject
{
    private readonly LinkDetectionService _linkDetectionService;
    private readonly FilePreviewService _filePreviewService; // To open file previews

    [ObservableProperty]
    private ObservableCollection<DetectedLink> _links = new();

    [ObservableProperty]
    private DetectedLink? _selectedLink;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmptyStateVisible;

    // View properties for positioning/sizing the popup
    [ObservableProperty]
    private double _width = 500;
    
    [ObservableProperty]
    private double _height = 400;
    
    [ObservableProperty]
    private double _horizontalOffset;
    
    [ObservableProperty]
    private double _verticalOffset;

    public DetectedLinksViewModel(LinkDetectionService linkDetectionService, FilePreviewService filePreviewService)
    {
        _linkDetectionService = linkDetectionService;
        _filePreviewService = filePreviewService;
    }

    private TerminalPairTabViewModel? _currentTerminalTab;

    [RelayCommand]
    public async Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        // Refresh links when opened
        await RefreshLinksAsync();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        SelectedLink = null; // Clear selection on close
    }

    [RelayCommand]
    private async Task RefreshLinksAsync()
    {
        if (_currentTerminalTab is not { } terminalTab) // Use pattern matching for null check and assignment
        {
            Links.Clear();
            IsEmptyStateVisible = true;
            return;
        }

        IsLoading = true;
        try
        {
            var recentOutput = terminalTab.Pair.CustomTerminal?.GetRecentOutput(5000) + "\n" +
                               terminalTab.Pair.ShellTerminal?.GetRecentOutput(5000);

            var detectedLinks = _linkDetectionService.DetectAllLinks(recentOutput ?? "", terminalTab.Pair.WorkingDirectory);

            Links = new ObservableCollection<DetectedLink>(detectedLinks);
            IsEmptyStateVisible = !Links.Any();

            // Auto-select first link if available
            SelectedLink = Links.FirstOrDefault();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedLinkChanged(DetectedLink? value)
    {
        OpenSelectedLinkCommand.NotifyCanExecuteChanged();
        PreviewSelectedLinkCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedLink))]
    private void OpenSelectedLink()
    {
        if (SelectedLink == null) return;
        
        _linkDetectionService.OpenLink(SelectedLink.Url);
        IsOpen = false; // Close popup after opening link
    }

    private bool CanOpenSelectedLink() => SelectedLink != null;

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedLink))]
    private void PreviewSelectedLink()
    {
        if (SelectedLink == null || !SelectedLink.IsFile) return;

        // Parse for line/column numbers
        var (path, line, column) = FilePreviewService.ParseFilePathWithPosition(SelectedLink.Url);
        
        // This event needs to be handled by MainWindow, which knows how to show FilePreview.
        // For now, we'll raise an event.
        // In a more complex DI setup, a dedicated dialog service would be better.
        FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs
        {
            FilePath = path,
            Line = line,
            Column = column
        });
        
        IsOpen = false; // Close popup after requesting preview
    }

    private bool CanPreviewSelectedLink() => SelectedLink != null && SelectedLink.IsFile;

    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
}
