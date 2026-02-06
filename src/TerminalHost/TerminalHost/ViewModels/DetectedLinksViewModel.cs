using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class DetectedLinksViewModel : BasePanelViewModel
{
    private readonly ILinkDetectionService _linkDetectionService;
    private readonly IFilePreviewService _filePreviewService;

    public override string PanelId => "detectedLinks";
    public override string PanelTitle => "Detected Links";
    public override string PanelIcon => "🔗";
    public override PanelSizePreset SizePreset => PanelSizePreset.Medium;

    [ObservableProperty]
    private ObservableCollection<DetectedLink> _links = [];

    [ObservableProperty]
    private DetectedLink? _selectedLink;

    [ObservableProperty]
    private bool _isDragging;

    [ObservableProperty]
    private bool _isEmptyStateVisible;

    [ObservableProperty]
    private bool _isLoading;

    public DetectedLinksViewModel(ILinkDetectionService linkDetectionService, IFilePreviewService filePreviewService)
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

    protected override void OnClose()
    {
        IsOpen = false;
        SelectedLink = null;
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
            // Trigger an update of the tab's link cache first
            terminalTab.UpdateDetectedLinks(_linkDetectionService);

            // Use the tab's MRU-cached links (already sorted by most recently seen)
            Links = new ObservableCollection<DetectedLink>(terminalTab.DetectedLinks);
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
