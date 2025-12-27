using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class FileHistoryViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IToastService _toastService;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private GitCommit? _selectedCommit;

    [ObservableProperty]
    private string _fileDiff = "";

    [ObservableProperty]
    private bool _hasMoreCommits = true;

    public ObservableCollection<GitCommit> Commits { get; } = [];

    private int _currentSkip;
    private const int PageSize = 25;

    public FileHistoryViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IClipboardService clipboardService,
        IToastService toastService)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _toastService = toastService;
    }

    public async Task OpenAsync(string workingDirectory, string filePath)
    {
        WorkingDirectory = workingDirectory;
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);

        Commits.Clear();
        SelectedCommit = null;
        FileDiff = "";
        _currentSkip = 0;
        HasMoreCommits = true;

        IsOpen = true;
        await LoadCommitsAsync();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    [RelayCommand]
    private async Task LoadCommitsAsync()
    {
        if (IsLoading || string.IsNullOrEmpty(WorkingDirectory) || string.IsNullOrEmpty(FilePath))
            return;

        IsLoading = true;

        try
        {
            var commits = await _gitStatusService.GetFileHistoryAsync(WorkingDirectory, FilePath, _currentSkip, PageSize);

            foreach (var commit in commits)
            {
                Commits.Add(commit);
            }

            _currentSkip += commits.Count;
            HasMoreCommits = commits.Count == PageSize;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMoreCommits) return;
        await LoadCommitsAsync();
    }

    partial void OnSelectedCommitChanged(GitCommit? value)
    {
        if (value != null)
        {
            _ = LoadDiffAsync(value);
        }
        else
        {
            FileDiff = "";
        }
    }

    private async Task LoadDiffAsync(GitCommit commit)
    {
        try
        {
            var diff = await _gitStatusService.GetFileDiffInCommitAsync(WorkingDirectory, commit.Hash, FilePath);
            FileDiff = diff ?? "";
        }
        catch
        {
            FileDiff = "";
        }
    }

    [RelayCommand]
    private async Task CopyFileContentAtCommitAsync()
    {
        if (SelectedCommit == null) return;

        try
        {
            var content = await _gitStatusService.GetFileContentAtCommitAsync(WorkingDirectory, SelectedCommit.Hash, FilePath);
            if (content != null)
            {
                await _clipboardService.SetTextAsync(content);
                _toastService.Show($"Copied file content from {SelectedCommit.ShortHash}", ToastType.Success);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to get file content: {ex.Message}", "Error");
        }
    }

    [RelayCommand]
    private async Task CopyHashAsync()
    {
        if (SelectedCommit == null) return;

        try
        {
            await _clipboardService.SetTextAsync(SelectedCommit.Hash);
            _toastService.Show("Hash copied to clipboard", ToastType.Success);
        }
        catch
        {
            // Clipboard access can fail silently
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Commits.Clear();
        _currentSkip = 0;
        HasMoreCommits = true;
        await LoadCommitsAsync();
    }
}
