using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.ViewModels;

public partial class MergeConflictViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IToastService _toastService;
    private readonly IProcessService _processService;
    private readonly IConfigurationService _configurationService;
    private TerminalPairTabViewModel? _currentTerminalTab;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private double _width = 1200;

    [ObservableProperty]
    private double _height = 800;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    [ObservableProperty]
    private string _title = "Merge Conflict Resolution";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _conflictFiles = [];

    [ObservableProperty]
    private GitFileStatus? _selectedConflictFile;

    [ObservableProperty]
    private ConflictInfo? _currentConflict;

    [ObservableProperty]
    private string _resolvedContent = "";

    // Hunk navigation state
    [ObservableProperty]
    private int _currentHunkIndex;

    [ObservableProperty]
    private string _hunkCountText = "No conflicts";

    [ObservableProperty]
    private bool _canNavigatePrev;

    [ObservableProperty]
    private bool _canNavigateNext;

    [ObservableProperty]
    private string _oursContent = "";

    [ObservableProperty]
    private string _theirsContent = "";

    [ObservableProperty]
    private string _resultContent = "";

    [ObservableProperty]
    private bool _isAiSuggestion;

    [ObservableProperty]
    private bool _isAiLoading;

    public MergeConflictViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IFileSystem fileSystem,
        IToastService toastService,
        IProcessService processService,
        IConfigurationService configurationService)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _toastService = toastService;
        _processService = processService;
        _configurationService = configurationService;
    }

    [RelayCommand]
    public async Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        Title = $"Merge Conflicts - {terminalTab.Title}";
        Info = terminalTab.Pair.WorkingDirectory;

        await RefreshConflictFilesAsync();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        SelectedConflictFile = null;
        CurrentConflict = null;
        ResolvedContent = "";
        OursContent = "";
        TheirsContent = "";
        ResultContent = "";
        IsAiSuggestion = false;
        _currentTerminalTab = null;
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
        IsAiSuggestion = false;
        LoadConflictAsync(value);
    }

    private async void LoadConflictAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            CurrentConflict = null;
            ClearHunkDisplay();
            return;
        }

        CurrentConflict = await _gitStatusService.ParseConflictFileAsync(
            _currentTerminalTab.Pair.WorkingDirectory, file.FilePath);

        CurrentHunkIndex = 0;
        ShowCurrentHunk();
    }

    private void ClearHunkDisplay()
    {
        OursContent = "";
        TheirsContent = "";
        ResultContent = "";
        HunkCountText = "No conflicts";
        CanNavigatePrev = false;
        CanNavigateNext = false;
    }

    private void ShowCurrentHunk()
    {
        if (CurrentConflict == null || CurrentConflict.Hunks.Count == 0)
        {
            ClearHunkDisplay();
            return;
        }

        if (CurrentHunkIndex >= CurrentConflict.Hunks.Count)
        {
            CurrentHunkIndex = 0;
        }

        var hunk = CurrentConflict.Hunks[CurrentHunkIndex];
        OursContent = hunk.OursContent;
        TheirsContent = hunk.TheirsContent;
        ResultContent = CurrentConflict.ResolvedLines.Count > CurrentHunkIndex
            ? CurrentConflict.ResolvedLines[CurrentHunkIndex]
            : hunk.OursContent;

        HunkCountText = $"Conflict {CurrentHunkIndex + 1} / {CurrentConflict.Hunks.Count}";
        CanNavigatePrev = CurrentHunkIndex > 0;
        CanNavigateNext = CurrentHunkIndex < CurrentConflict.Hunks.Count - 1;
    }

    [RelayCommand]
    private void NavigatePrevHunk()
    {
        if (CurrentConflict == null || CurrentHunkIndex <= 0) return;

        SaveCurrentHunkResult();
        CurrentHunkIndex--;
        IsAiSuggestion = false;
        ShowCurrentHunk();
    }

    [RelayCommand]
    private void NavigateNextHunk()
    {
        if (CurrentConflict == null || CurrentHunkIndex >= CurrentConflict.Hunks.Count - 1) return;

        SaveCurrentHunkResult();
        CurrentHunkIndex++;
        IsAiSuggestion = false;
        ShowCurrentHunk();
    }

    private void SaveCurrentHunkResult()
    {
        if (CurrentConflict == null) return;

        while (CurrentConflict.ResolvedLines.Count <= CurrentHunkIndex)
            CurrentConflict.ResolvedLines.Add("");
        CurrentConflict.ResolvedLines[CurrentHunkIndex] = ResultContent;
    }

    [RelayCommand]
    private void AcceptOurs()
    {
        ApplyResolution(ConflictResolution.AcceptOurs);
    }

    [RelayCommand]
    private void AcceptTheirs()
    {
        ApplyResolution(ConflictResolution.AcceptTheirs);
    }

    [RelayCommand]
    private void AcceptBoth()
    {
        ApplyResolution(ConflictResolution.AcceptBoth);
    }

    private void ApplyResolution(ConflictResolution resolution)
    {
        if (CurrentConflict == null || CurrentHunkIndex >= CurrentConflict.Hunks.Count) return;

        var hunk = CurrentConflict.Hunks[CurrentHunkIndex];
        var resolved = resolution switch
        {
            ConflictResolution.AcceptOurs => hunk.OursContent,
            ConflictResolution.AcceptTheirs => hunk.TheirsContent,
            ConflictResolution.AcceptBoth => hunk.OursContent + "\n" + hunk.TheirsContent,
            _ => ResultContent
        };

        ResultContent = resolved;
        IsAiSuggestion = false;

        // Update resolved lines
        while (CurrentConflict.ResolvedLines.Count <= CurrentHunkIndex)
            CurrentConflict.ResolvedLines.Add("");
        CurrentConflict.ResolvedLines[CurrentHunkIndex] = resolved;
    }

    [RelayCommand]
    private async Task AiSuggestAsync()
    {
        if (CurrentConflict == null || CurrentHunkIndex >= CurrentConflict.Hunks.Count) return;

        var hunk = CurrentConflict.Hunks[CurrentHunkIndex];
        IsAiLoading = true;
        try
        {
            var suggestion = await SuggestResolutionAsync(hunk.OursContent, hunk.TheirsContent);
            if (suggestion != null)
            {
                ResultContent = suggestion;
                IsAiSuggestion = true;

                // Update resolved lines
                while (CurrentConflict.ResolvedLines.Count <= CurrentHunkIndex)
                    CurrentConflict.ResolvedLines.Add("");
                CurrentConflict.ResolvedLines[CurrentHunkIndex] = suggestion;
            }
        }
        finally
        {
            IsAiLoading = false;
        }
    }

    public async Task<string?> SuggestResolutionAsync(string oursContent, string theirsContent)
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return null;

        var config = _configurationService.Load();
        var claudePath = Environment.ExpandEnvironmentVariables(config.Settings.CustomCommand);
        var claudeFileName = System.IO.Path.GetFileNameWithoutExtension(claudePath).ToLowerInvariant();
        if (!claudeFileName.Contains("claude") && !claudeFileName.Contains("gemini")) return null;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var filePath = SelectedConflictFile?.FilePath ?? "";
        var ext = System.IO.Path.GetExtension(filePath).TrimStart('.');
        var lang = ext.Length > 0 ? ext : "text";

        // Truncate to 200 lines each per spec limit
        const int maxLines = 200;
        var oursText = string.Join('\n', oursContent.Split('\n').Take(maxLines));
        var theirsText = string.Join('\n', theirsContent.Split('\n').Take(maxLines));

        var prompt = $"""
            You are resolving a git merge conflict. Produce ONLY the resolved code — no explanation, no markdown fences.

            File: {filePath}
            Language: {lang}

            <<<<<<< OURS
            {oursText}
            =======
            {theirsText}
            >>>>>>> THEIRS

            Output the resolved version of the conflicted section only.
            """;

        var (exitCode, output, _) = await _processService.RunAsync(
            claudePath, "-p --no-session-persistence", workingDirectory,
            stdin: prompt, timeout: TimeSpan.FromSeconds(30));

        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            _toastService.Show("AI suggestion failed — check AI assistant configuration", ToastType.Error);
            return null;
        }

        return output.Trim();
    }

    [RelayCommand]
    private async Task SaveAndMarkResolvedAsync()
    {
        if (SelectedConflictFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
            return;

        if (CurrentConflict == null) return;

        // Save the current hunk result before proceeding
        SaveCurrentHunkResult();

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
            Close();
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
            Close();
        }
        else
        {
            _toastService.Show($"Failed to continue merge: {result.Error}", ToastType.Error);
        }
    }
}
