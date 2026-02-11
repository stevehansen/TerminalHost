using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class MergeConflictViewModel : BasePanelViewModel
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IToastService _toastService;
    private TerminalPairTabViewModel? _currentTerminalTab;

    public override string PanelId => "mergeConflict";
    public override string PanelTitle => "Merge Conflict Resolution";
    public override string PanelIcon => "\u26A0"; // Warning sign
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _conflictFiles = [];

    [ObservableProperty]
    private GitFileStatus? _selectedConflictFile;

    [ObservableProperty]
    private ConflictInfo? _currentConflict;

    [ObservableProperty]
    private string _title = "Merge Conflict Resolution";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _resolvedContent = "";

    public MergeConflictViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IFileSystem fileSystem,
        IToastService toastService)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _toastService = toastService;

        DisplayState = PanelDisplayState.Panel;
        Width = 1200;
        Height = 800;
    }

    [RelayCommand]
    public async Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        Title = $"Merge Conflicts - {terminalTab.Title}";
        Info = terminalTab.Pair.WorkingDirectory;

        await RefreshConflictFilesAsync();
        RequestShow();
    }

    [RelayCommand]
    private void Close()
    {
        OnClose();
    }

    [RelayCommand]
    private async Task RefreshConflictFilesAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
            var files = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);
            var conflicted = files.Where(f => f.Status == GitFileStatusType.Conflicted).ToList();

            ConflictFiles = new ObservableCollection<GitFileStatus>(conflicted);

            if (ConflictFiles.Count > 0 && SelectedConflictFile == null)
            {
                SelectedConflictFile = ConflictFiles[0];
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedConflictFileChanged(GitFileStatus? value)
    {
        LoadConflictAsync(value);
    }

    private async void LoadConflictAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            CurrentConflict = null;
            return;
        }

        CurrentConflict = await _gitStatusService.ParseConflictFileAsync(
            _currentTerminalTab.Pair.WorkingDirectory, file.FilePath);
    }

    [RelayCommand]
    private async Task SaveAndMarkResolvedAsync()
    {
        if (SelectedConflictFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
            return;

        if (CurrentConflict == null) return;

        IsLoading = true;
        try
        {
            var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
            var fullPath = System.IO.Path.Combine(workingDirectory, SelectedConflictFile.FilePath);

            // Build resolved file content
            var content = CurrentConflict.FullContent;
            var lines = content.Split('\n').ToList();

            // Replace conflict markers with resolved content (from last hunk to first to preserve indices)
            for (int i = CurrentConflict.Hunks.Count - 1; i >= 0; i--)
            {
                var hunk = CurrentConflict.Hunks[i];
                var resolved = i < CurrentConflict.ResolvedLines.Count
                    ? CurrentConflict.ResolvedLines[i]
                    : hunk.OursContent;

                var resolvedLines = resolved.Split('\n');
                lines.RemoveRange(hunk.StartLine, hunk.EndLine - hunk.StartLine + 1);
                lines.InsertRange(hunk.StartLine, resolvedLines);
            }

            _fileSystem.WriteAllText(fullPath, string.Join("\n", lines));

            var result = await _gitStatusService.MarkResolvedAsync(workingDirectory, SelectedConflictFile.FilePath);

            if (result.Success)
            {
                _toastService.Show($"Resolved: {SelectedConflictFile.FileName}", ToastType.Success);
                await RefreshConflictFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to mark resolved: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AbortMergeAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var confirmed = _dialogService.ShowConfirmation(
            "Abort the current merge? All changes will be lost.",
            "Abort Merge");
        if (!confirmed) return;

        var result = await _gitStatusService.MergeAbortAsync(
            _currentTerminalTab.Pair.WorkingDirectory);

        if (result.Success)
        {
            _toastService.Show("Merge aborted", ToastType.Success);
            OnClose();
        }
        else
        {
            _toastService.Show($"Failed to abort merge: {result.Error}", ToastType.Error);
        }
    }

    [RelayCommand]
    private async Task ContinueMergeAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var result = await _gitStatusService.MergeContinueAsync(
            _currentTerminalTab.Pair.WorkingDirectory);

        if (result.Success)
        {
            _toastService.Show("Merge completed", ToastType.Success);
            OnClose();
        }
        else
        {
            _toastService.Show($"Failed to continue merge: {result.Error}", ToastType.Error);
        }
    }
}
