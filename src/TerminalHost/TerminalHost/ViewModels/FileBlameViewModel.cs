using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for File Blame panel (Ctrl+Shift+B).
/// Shows line-by-line blame annotations for a file.
/// </summary>
public partial class FileBlameViewModel : BasePanelViewModel
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;

    #region IPanelableViewModel Implementation

    public override string PanelId => "fileBlame";
    public override string PanelTitle => "Blame";
    public override string PanelIcon => "👤";
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    #endregion

    #region Properties

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private string _title = "Blame";

    [ObservableProperty]
    private GitBlameResult? _blameResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLine))]
    [NotifyCanExecuteChangedFor(nameof(CopyHashCommand))]
    [NotifyCanExecuteChangedFor(nameof(ViewCommitCommand))]
    private GitBlameLine? _selectedLine;

    [ObservableProperty]
    private GitCommitDetails? _selectedCommitDetails;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingCommitDetails;

    [ObservableProperty]
    private bool _colorByAuthor = true;

    public bool HasSelectedLine => SelectedLine != null;
    public bool HasBlameResult => BlameResult?.Lines.Count > 0;

    #endregion

    #region Events

    /// <summary>
    /// Raised when user wants to view full commit history for the selected commit.
    /// </summary>
    public event EventHandler<ViewCommitRequestedEventArgs>? ViewCommitRequested;

    #endregion

    public FileBlameViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IToastService toastService)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _toastService = toastService;

        // Set defaults - defaults to Panel
        DisplayState = PanelDisplayState.Panel;
        Width = 1100;
        Height = 750;
    }

    #region Overrides

    protected override void OnClose()
    {
        SelectedLine = null;
        SelectedCommitDetails = null;
        BlameResult = null;
        WorkingDirectory = "";
        FilePath = "";
        FileName = "";
        base.OnClose();
    }

    #endregion

    #region Commands

    public async Task OpenAsync(string workingDirectory, string filePath)
    {
        WorkingDirectory = workingDirectory;
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        Title = $"Blame: {FileName}";

        await RefreshBlameAsync();
        RequestShow();
    }

    [RelayCommand]
    private void Close()
    {
        OnClose();
    }

    [RelayCommand]
    private async Task RefreshBlameAsync()
    {
        if (string.IsNullOrEmpty(WorkingDirectory) || string.IsNullOrEmpty(FilePath))
        {
            BlameResult = null;
            return;
        }

        IsLoading = true;
        try
        {
            BlameResult = await _gitStatusService.GetFileBlameAsync(WorkingDirectory, FilePath);
            OnPropertyChanged(nameof(HasBlameResult));

            if (BlameResult == null || BlameResult.Lines.Count == 0)
            {
                _toastService.Show("Could not get blame information", ToastType.Warning);
            }
        }
        catch (Exception ex)
        {
            _toastService.Show($"Error loading blame: {ex.Message}", ToastType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanCopyHash => SelectedLine != null;

    [RelayCommand(CanExecute = nameof(CanCopyHash))]
    private void CopyHash()
    {
        if (SelectedLine == null) return;

        try
        {
            System.Windows.Clipboard.SetText(SelectedLine.CommitHash);
            _toastService.Show("Hash copied to clipboard", ToastType.Success);
        }
        catch
        {
            _toastService.Show("Failed to copy hash", ToastType.Error);
        }
    }

    public bool CanViewCommit => SelectedLine != null;

    [RelayCommand(CanExecute = nameof(CanViewCommit))]
    private void ViewCommit()
    {
        if (SelectedLine == null || string.IsNullOrEmpty(WorkingDirectory))
            return;

        ViewCommitRequested?.Invoke(this, new ViewCommitRequestedEventArgs
        {
            CommitHash = SelectedLine.CommitHash,
            WorkingDirectory = WorkingDirectory
        });
    }

    [RelayCommand]
    private void ToggleColorMode()
    {
        ColorByAuthor = !ColorByAuthor;
    }

    #endregion

    #region Event Handlers

    partial void OnSelectedLineChanged(GitBlameLine? value)
    {
        LoadCommitDetailsAsync(value);
    }

    #endregion

    #region Private Methods

    private async void LoadCommitDetailsAsync(GitBlameLine? line)
    {
        if (line == null || string.IsNullOrEmpty(WorkingDirectory))
        {
            SelectedCommitDetails = null;
            return;
        }

        IsLoadingCommitDetails = true;
        try
        {
            SelectedCommitDetails = await _gitStatusService.GetCommitDetailsAsync(
                WorkingDirectory,
                line.CommitHash);
        }
        finally
        {
            IsLoadingCommitDetails = false;
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the display color for a blame line based on the current color mode.
    /// </summary>
    public string GetLineColor(GitBlameLine line)
    {
        if (BlameResult?.AuthorColors == null)
            return "#CCCCCC";

        if (ColorByAuthor && BlameResult.AuthorColors.TryGetValue(line.Author, out var color))
            return color;

        // Color by recency - more recent = brighter
        var age = DateTimeOffset.Now - line.CommitDate;
        if (age.TotalDays < 7) return "#4EC9B0";   // Teal (very recent)
        if (age.TotalDays < 30) return "#9CDCFE";  // Light blue (recent)
        if (age.TotalDays < 90) return "#DCDCAA";  // Yellow (moderate)
        if (age.TotalDays < 365) return "#CE9178"; // Orange (older)
        return "#808080"; // Gray (very old)
    }

    #endregion
}

/// <summary>
/// Event args for view commit request.
/// </summary>
public class ViewCommitRequestedEventArgs : EventArgs
{
    public required string CommitHash { get; init; }
    public required string WorkingDirectory { get; init; }
}
