using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for Git Changes panel (Ctrl+G).
/// Supports Panel, Popup, and Window display states.
/// </summary>
public partial class GitFilesViewModel : ObservableObject, IPanelableViewModel
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IFilePreviewService _filePreviewService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
    private TerminalPairTabViewModel? _currentTerminalTab;

    #region IPanelableViewModel Implementation

    public string PanelId => "gitChanges";
    public string PanelTitle => "Git Changes";
    public string PanelIcon => "\u0394"; // Δ (Delta symbol)

    public IEnumerable<PanelHeaderCommand>? HeaderCommands => null;
    public string? StatusText => null;

    [ObservableProperty]
    private PanelDisplayState _displayState = PanelDisplayState.Popup;

    [ObservableProperty]
    private PanelSide _preferredSide = PanelSide.Right;

    public ICommand DockCommand { get; private set; } = null!;
    public ICommand UndockCommand { get; private set; } = null!;
    public ICommand DetachCommand { get; private set; } = null!;
    ICommand IPanelableViewModel.CloseCommand => CloseCommand;

    public event EventHandler<PanelStateChangeRequestedEventArgs>? StateChangeRequested;

    /// <summary>
    /// Event raised when the panel needs to be shown.
    /// </summary>
    public event EventHandler? ShowRequested;

    #endregion

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _gitFiles = [];

    [ObservableProperty]
    private GitFileStatus? _selectedGitFile;

    [ObservableProperty]
    private string _diffText = "";

    [ObservableProperty]
    private string _title = "Git Changes";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private bool _isEmptyStateVisible;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isDragging; 
    
    [ObservableProperty]
    private double _width = 1100;

    [ObservableProperty]
    private double _height = 700;

    public PanelSizePreset SizePreset => PanelSizePreset.Large;

    [ObservableProperty]
    private double _horizontalOffset;
    
    [ObservableProperty]
    private double _verticalOffset;

    public GitFilesViewModel(IGitStatusService gitStatusService, IFilePreviewService filePreviewService, IDialogService dialogService, IFileSystem fileSystem, IProcessService processService)
    {
        _gitStatusService = gitStatusService;
        _filePreviewService = filePreviewService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _processService = processService;
        _diffText = "";

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
        // After state change removes from docked panels, request to show as popup
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDetach()
    {
        StateChangeRequested?.Invoke(this, new PanelStateChangeRequestedEventArgs(PanelDisplayState.Window));
        // After state change removes from docked panels, request to show as window
        ShowRequested?.Invoke(this, EventArgs.Empty);
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

    [RelayCommand]
    public async Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        if (terminalTab.GitStatus?.IsGitRepository != true)
        {
            _dialogService.ShowInfo( 
                "The selected tab is not a Git repository or Git status is unavailable.",
                "Git Changes");
            _currentTerminalTab = null; 
            return;
        }

        Title = $"Git Changes - {terminalTab.Title}";
        Info = terminalTab.Pair.WorkingDirectory;

        await RefreshGitFilesAsync();

        // Request to be shown in the appropriate mode
        // NOTE: Don't set IsOpen here - let the ShowRequested handler set it based on DisplayState
        // This prevents the popup from showing when we want Panel or Window mode
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        SelectedGitFile = null;
        DiffText = "";
        _currentTerminalTab = null; 
    }

    [RelayCommand]
    private async Task RefreshGitFilesAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            GitFiles.Clear();
            IsEmptyStateVisible = true;
            SelectedGitFile = null;
            DiffText = "";
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var files = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);

        GitFiles = new ObservableCollection<GitFileStatus>(files);
        IsEmptyStateVisible = !GitFiles.Any();

        SelectedGitFile = null;
        DiffText = IsEmptyStateVisible ? "" : "";

        if (GitFiles.Any())
        {
            SelectedGitFile = GitFiles.First();
        }
    }

    partial void OnSelectedGitFileChanged(GitFileStatus? value)
    {
        UpdateButtonsEnabledState();
        LoadDiffForSelectedFileAsync(value);
    }

    private async void LoadDiffForSelectedFileAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            DiffText = "";
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var diff = await _gitStatusService.GetFileDiffAsync(workingDirectory, file.FilePath, file.IsStaged);

        if (!string.IsNullOrEmpty(diff))
        {
            DiffText = diff;
        }
        else
        {
            DiffText = "";
        }
    }

    private void UpdateButtonsEnabledState()
    {
        PreviewFileCommand.NotifyCanExecuteChanged();
        EditFileCommand.NotifyCanExecuteChanged();
        ExploreFileCommand.NotifyCanExecuteChanged();
    }

    public bool CanPreviewFile => SelectedGitFile != null && SelectedGitFile.Status != GitFileStatusType.Deleted;
    [RelayCommand(CanExecute = nameof(CanPreviewFile))]
    private void PreviewFile()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var fullPath = System.IO.Path.Combine(_currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);
        FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs { FilePath = fullPath });
        Close();
    }

    public bool CanEditFile => SelectedGitFile != null && SelectedGitFile.Status != GitFileStatusType.Deleted;
    [RelayCommand(CanExecute = nameof(CanEditFile))]
    private void EditFile()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var fullPath = System.IO.Path.Combine(_currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);
        FileEditRequested?.Invoke(this, new FileEditRequestedEventArgs { FilePath = fullPath });
        Close();
    }

    public bool CanExploreFile => SelectedGitFile != null;
    [RelayCommand(CanExecute = nameof(CanExploreFile))]
    private void ExploreFile()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var fullPath = System.IO.Path.Combine(_currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);

        if (_fileSystem.DirectoryExists(directory))
        {
            if (_fileSystem.FileExists(fullPath))
            {
                _processService.Start("explorer.exe", $"/select,\"{fullPath}\"");
            }
            else
            {
                _processService.Start("explorer.exe", directory!);
            }
        }
    }

    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
    public event EventHandler<FileEditRequestedEventArgs>? FileEditRequested;
}
