using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class FileHistoryViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IToastService _toastService;
    private readonly IConfigurationService _configurationService;
    private readonly IProcessService _processService;
    private readonly IAiExecutionService _aiExecutionService;

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

    [ObservableProperty]
    private string? _aiExplanation;

    [ObservableProperty]
    private bool _isAiLoading;

    public ObservableCollection<GitCommit> Commits { get; } = [];

    private int _currentSkip;
    private const int PageSize = 25;

    public FileHistoryViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IClipboardService clipboardService,
        IToastService toastService,
        IConfigurationService configurationService,
        IProcessService processService,
        IAiExecutionService aiExecutionService)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _toastService = toastService;
        _configurationService = configurationService;
        _processService = processService;
        _aiExecutionService = aiExecutionService;
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
            // Use GetCommitHistoryAsync with filePath parameter to get file-specific history
            var commits = await _gitStatusService.GetCommitHistoryAsync(
                WorkingDirectory,
                _currentSkip + PageSize,
                author: null,
                filePath: FilePath);

            // Add new commits that aren't already in the list
            foreach (var commit in commits.Skip(_currentSkip))
            {
                Commits.Add(commit);
            }

            _currentSkip = Commits.Count;
            HasMoreCommits = commits.Count >= _currentSkip;
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
            // Get diff for this specific file in this commit
            var diff = await _gitStatusService.GetCommitDiffAsync(
                WorkingDirectory,
                commit.Hash,
                FilePath);

            FileDiff = diff ?? "";
        }
        catch
        {
            FileDiff = "// Failed to load diff";
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

    [RelayCommand]
    private void DismissAiExplanation()
    {
        AiExplanation = null;
    }

    [RelayCommand]
    private async Task SummarizeHistoryAsync()
    {
        if (Commits.Count == 0) return;
        if (!_aiExecutionService.IsAiAvailable()) return;

        IsAiLoading = true;
        try
        {
            var messages = string.Join("\n", Commits.Select(c => $"- {c.ShortHash}: {c.Subject}"));
            var prompt = $"You are a git expert. Summarize concisely.\n\nSummarize the evolution of file '{FileName}' based on these commits:\n{messages}";
            var result = await _aiExecutionService.ExecuteAsync(prompt, WorkingDirectory, "Summarizing file history", timeout: TimeSpan.FromSeconds(60));

            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                AiExplanation = result.Output.Trim();
        }
        finally { IsAiLoading = false; }
    }

    [RelayCommand]
    private async Task CopyAiExplanationAsync()
    {
        if (!string.IsNullOrEmpty(AiExplanation))
        {
            await _clipboardService.SetTextAsync(AiExplanation);
            _toastService.Show("Copied to clipboard", ToastType.Success);
        }
    }
}
