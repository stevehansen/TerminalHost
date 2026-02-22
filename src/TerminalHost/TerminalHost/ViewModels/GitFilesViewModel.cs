using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for Git Changes panel (Alt+G).
/// Supports Panel, Popup, and Window display states.
/// Provides interactive staging, unstaging, and commit functionality.
/// </summary>
public partial class GitFilesViewModel : BasePanelViewModel
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IFilePreviewService _filePreviewService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
    private readonly IToastService _toastService;
    private readonly IDiffParserService _diffParserService;
    private readonly IConfigurationService _configurationService;
    private TerminalPairTabViewModel? _currentTerminalTab;

    #region IPanelableViewModel Implementation

    public override string PanelId => "gitChanges";
    public override string PanelTitle => "Git Changes";
    public override string PanelIcon => "\u0394"; // Δ (Delta symbol)
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    public override IEnumerable<PanelHeaderCommand>? HeaderCommands =>
    [
        new PanelHeaderCommand
        {
            Icon = "📦",
            Tooltip = "Stash changes (Ctrl+Shift+S for stash manager)",
            Command = QuickStashCommand
        },
        new PanelHeaderCommand
        {
            Icon = "↻",
            Tooltip = "Refresh file list",
            Command = RefreshGitFilesCommand
        }
    ];

    #endregion

    #region Git Properties

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _gitFiles = [];

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _stagedFiles = [];

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _unstagedFiles = [];

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
    private bool _isDragging;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isTreeView;

    [ObservableProperty]
    private ObservableCollection<FileTreeNode> _stagedTree = [];

    [ObservableProperty]
    private ObservableCollection<FileTreeNode> _unstagedTree = [];

    [ObservableProperty]
    private ParsedDiff? _currentParsedDiff;

    [ObservableProperty]
    private bool _isMergeInProgress;

    [ObservableProperty]
    private int _conflictCount;

    /// <summary>
    /// View mode: "Working" for working directory changes, "Branch" for branch comparison.
    /// </summary>
    [ObservableProperty]
    private string _changesViewMode = "Working";

    [ObservableProperty]
    private string _baseBranchName = "";

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _branchChangedFiles = [];

    [ObservableProperty]
    private GitFileStatus? _selectedBranchFile;

    [ObservableProperty]
    private string _branchDiffText = "";

    public bool IsWorkingView => ChangesViewMode == "Working";
    public bool IsBranchView => ChangesViewMode == "Branch";

    #endregion

    #region Commit Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubjectLength))]
    [NotifyPropertyChangedFor(nameof(IsSubjectTooLong))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommitCommand))]
    private string _commitMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommitCommand))]
    private bool _amendCommit;

    public int SubjectLength => CommitMessage.Split('\n').FirstOrDefault()?.Length ?? 0;
    public bool IsSubjectTooLong => SubjectLength > 72;
    public bool HasStagedFiles => StagedFiles.Count > 0;

    public string FileChangeSummary
    {
        get
        {
            var count = GitFiles.Count;
            if (count == 0) return "No changes";
            var branch = _currentTerminalTab?.GitStatus?.BranchName ?? "unknown";
            return $"{count} file {(count == 1 ? "change" : "changes")} on {branch}";
        }
    }

    #endregion

    #region Events

    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
    public event EventHandler<FileEditRequestedEventArgs>? FileEditRequested;

    #endregion

    public GitFilesViewModel(
        IGitStatusService gitStatusService,
        IFilePreviewService filePreviewService,
        IDialogService dialogService,
        IFileSystem fileSystem,
        IProcessService processService,
        IToastService toastService,
        IDiffParserService diffParserService,
        IConfigurationService configurationService)
    {
        _gitStatusService = gitStatusService;
        _filePreviewService = filePreviewService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _processService = processService;
        _toastService = toastService;
        _diffParserService = diffParserService;
        _configurationService = configurationService;

        // Set defaults for git changes - defaults to Panel
        DisplayState = PanelDisplayState.Panel;
        Width = 1100;
        Height = 700;
    }

    #region Overrides

    protected override void OnClose()
    {
        SelectedGitFile = null;
        DiffText = "";
        CommitMessage = "";
        AmendCommit = false;
        ChangesViewMode = "Working";
        BranchChangedFiles.Clear();
        SelectedBranchFile = null;
        BranchDiffText = "";
        BaseBranchName = "";
        _currentTerminalTab = null;
        base.OnClose();
    }

    #endregion

    #region Commands

    [RelayCommand]
    public async Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        if (terminalTab.GitStatus?.IsGitRepository != true)
        {
            _dialogService.ShowInfo(
                "The selected tab is not a Git repository or Git status is unavailable.",
                "Git Changes");
            return;
        }

        await LoadDataAsync(terminalTab);

        // Request to be shown in the appropriate mode
        RequestShow();
    }

    /// <summary>
    /// Loads git files data without opening the popup.
    /// Used by the unified Git panel to load data for embedded display.
    /// </summary>
    public async Task LoadDataAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        Title = $"Git Changes - {terminalTab.Title}";
        Info = terminalTab.Pair.WorkingDirectory;

        await RefreshGitFilesAsync();
    }

    [RelayCommand]
    private void Close()
    {
        OnClose();
    }

    [RelayCommand]
    private async Task RefreshGitFilesAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            GitFiles.Clear();
            StagedFiles.Clear();
            UnstagedFiles.Clear();
            IsEmptyStateVisible = true;
            SelectedGitFile = null;
            DiffText = "";
            OnPropertyChanged(nameof(HasStagedFiles));
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var files = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);

        GitFiles = new ObservableCollection<GitFileStatus>(files);
        StagedFiles = new ObservableCollection<GitFileStatus>(files.Where(f => f.IsStaged));
        UnstagedFiles = new ObservableCollection<GitFileStatus>(files.Where(f => !f.IsStaged));
        IsEmptyStateVisible = !GitFiles.Any();

        // Build trees if tree view is active
        if (IsTreeView)
        {
            BuildFileTrees();
        }

        // Check for merge in progress
        IsMergeInProgress = await _gitStatusService.IsMergeInProgressAsync(workingDirectory);
        ConflictCount = files.Count(f => f.Status == GitFileStatusType.Conflicted);

        SelectedGitFile = null;
        DiffText = "";
        CurrentParsedDiff = null;

        // Preserve selection if possible, otherwise select first file
        if (GitFiles.Any())
        {
            SelectedGitFile = UnstagedFiles.FirstOrDefault() ?? StagedFiles.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasStagedFiles));
        OnPropertyChanged(nameof(FileChangeSummary));
        UpdateStagingButtonsState();
    }

    [RelayCommand]
    private async Task QuickStashAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null)
            return;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;

        try
        {
            var result = await _gitStatusService.CreateStashAsync(workingDirectory);

            if (result.Success)
            {
                _toastService.Show("Changes stashed", ToastType.Success);
                await RefreshGitFilesAsync();

                // Also refresh the terminal tab's git status
                var status = await _gitStatusService.GetGitStatusAsync(workingDirectory);
                _currentTerminalTab.GitStatus = status;
            }
            else
            {
                var error = result.Error ?? "Unknown error";
                if (error.Contains("No local changes to save"))
                {
                    _toastService.Show("No changes to stash", ToastType.Warning);
                }
                else
                {
                    _dialogService.ShowWarning($"Failed to stash changes:\n{error}", "Git Stash");
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to stash changes: {ex.Message}", "Git Stash");
        }
    }

    public bool CanPreviewFile => SelectedGitFile != null && SelectedGitFile.Status != GitFileStatusType.Deleted && !SelectedGitFile.IsSubmodule;

    [RelayCommand(CanExecute = nameof(CanPreviewFile))]
    private void PreviewFile()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var fullPath = System.IO.Path.Combine(_currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);
        FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs { FilePath = fullPath });
    }

    public bool CanEditFile => SelectedGitFile != null && SelectedGitFile.Status != GitFileStatusType.Deleted && !SelectedGitFile.IsSubmodule;

    [RelayCommand(CanExecute = nameof(CanEditFile))]
    private void EditFile()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var fullPath = System.IO.Path.Combine(_currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);
        FileEditRequested?.Invoke(this, new FileEditRequestedEventArgs { FilePath = fullPath });
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

    #endregion

    #region Staging Commands

    public bool CanStageFile => SelectedGitFile != null && !SelectedGitFile.IsStaged;

    [RelayCommand(CanExecute = nameof(CanStageFile))]
    private async Task StageFileAsync()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.StageFileAsync(
                _currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);

            if (result.Success)
            {
                _toastService.Show($"Staged: {SelectedGitFile.FileName}", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to stage: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanUnstageFile => SelectedGitFile != null && SelectedGitFile.IsStaged;

    [RelayCommand(CanExecute = nameof(CanUnstageFile))]
    private async Task UnstageFileAsync()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.UnstageFileAsync(
                _currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);

            if (result.Success)
            {
                _toastService.Show($"Unstaged: {SelectedGitFile.FileName}", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to unstage: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanStageAll => UnstagedFiles.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStageAll))]
    private async Task StageAllAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.StageAllAsync(_currentTerminalTab.Pair.WorkingDirectory);

            if (result.Success)
            {
                _toastService.Show($"Staged all {UnstagedFiles.Count} files", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to stage all: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanUnstageAll => StagedFiles.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUnstageAll))]
    private async Task UnstageAllAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.UnstageAllAsync(_currentTerminalTab.Pair.WorkingDirectory);

            if (result.Success)
            {
                _toastService.Show($"Unstaged all {StagedFiles.Count} files", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to unstage all: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanDiscardChanges => SelectedGitFile != null && !SelectedGitFile.IsStaged;

    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    private async Task DiscardChangesAsync()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var confirmed = _dialogService.ShowConfirmation(
            $"Are you sure you want to discard changes to '{SelectedGitFile.FileName}'?\n\nThis cannot be undone.",
            "Discard Changes");

        if (!confirmed) return;

        IsLoading = true;
        try
        {
            var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
            var filePath = SelectedGitFile.FilePath;

            // For untracked files, delete the file
            if (SelectedGitFile.Status == GitFileStatusType.Untracked)
            {
                var fullPath = System.IO.Path.Combine(workingDirectory, filePath);
                if (_fileSystem.FileExists(fullPath))
                {
                    _fileSystem.DeleteFile(fullPath);
                    _toastService.Show($"Deleted: {SelectedGitFile.FileName}", ToastType.Success);
                }
            }
            else
            {
                var result = await _gitStatusService.DiscardChangesAsync(workingDirectory, filePath);

                if (result.Success)
                {
                    _toastService.Show($"Discarded changes: {SelectedGitFile.FileName}", ToastType.Success);
                }
                else
                {
                    _toastService.Show($"Failed to discard: {result.Error}", ToastType.Error);
                }
            }

            await RefreshGitFilesAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Commit Commands

    public bool CanCreateCommit => HasStagedFiles && !string.IsNullOrWhiteSpace(CommitMessage);

    [RelayCommand(CanExecute = nameof(CanCreateCommit))]
    private async Task CreateCommitAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.CreateCommitAsync(
                _currentTerminalTab.Pair.WorkingDirectory,
                CommitMessage,
                AmendCommit);

            if (result.Success)
            {
                _toastService.Show(AmendCommit ? "Commit amended" : "Commit created", ToastType.Success);
                CommitMessage = "";
                AmendCommit = false;
                await RefreshGitFilesAsync();

                // Also refresh the terminal tab's git status indicator
                var status = await _gitStatusService.GetGitStatusAsync(
                    _currentTerminalTab.Pair.WorkingDirectory);
                _currentTerminalTab.GitStatus = status;
            }
            else
            {
                _toastService.Show($"Commit failed: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void InsertConventionalPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(CommitMessage) || !CommitMessage.Contains(':'))
        {
            CommitMessage = $"{prefix}: {CommitMessage}";
        }
    }

    #endregion

    #region Inline File Commands

    [RelayCommand]
    private async Task StageItemAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.StageFileAsync(
                _currentTerminalTab.Pair.WorkingDirectory, file.FilePath);

            if (result.Success)
                await RefreshGitFilesAsync();
            else
                _toastService.Show($"Failed to stage: {result.Error}", ToastType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task UnstageItemAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.UnstageFileAsync(
                _currentTerminalTab.Pair.WorkingDirectory, file.FilePath);

            if (result.Success)
                await RefreshGitFilesAsync();
            else
                _toastService.Show($"Failed to unstage: {result.Error}", ToastType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DiscardItemAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var confirmed = _dialogService.ShowConfirmation(
            $"Discard changes to '{file.FileName}'?\n\nThis cannot be undone.",
            "Discard Changes");
        if (!confirmed) return;

        IsLoading = true;
        try
        {
            var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;

            if (file.Status == GitFileStatusType.Untracked)
            {
                var fullPath = System.IO.Path.Combine(workingDirectory, file.FilePath);
                if (_fileSystem.FileExists(fullPath))
                    _fileSystem.DeleteFile(fullPath);
            }
            else
            {
                var result = await _gitStatusService.DiscardChangesAsync(workingDirectory, file.FilePath);
                if (!result.Success)
                {
                    _toastService.Show($"Failed to discard: {result.Error}", ToastType.Error);
                    return;
                }
            }

            await RefreshGitFilesAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Commit Message Generation

    [ObservableProperty]
    private bool _isGeneratingMessage;

    [RelayCommand]
    private async Task GenerateCommitMessageAsync()
    {
        if (!StagedFiles.Any()) return;
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;

        // Try AI-powered generation first (via claude -p)
        IsGeneratingMessage = true;
        try
        {
            var aiMessage = await GenerateWithAiAsync(workingDirectory);
            if (!string.IsNullOrWhiteSpace(aiMessage))
            {
                CommitMessage = aiMessage.Trim();
                return;
            }
        }
        catch
        {
            // AI unavailable, fall through to heuristic
        }
        finally
        {
            IsGeneratingMessage = false;
        }

        // Fallback: heuristic-based generation
        GenerateHeuristicMessage();
    }

    private async Task<string?> GenerateWithAiAsync(string workingDirectory)
    {
        // Get the staged diff
        var diff = await _gitStatusService.GetStagedDiffAsync(workingDirectory);
        if (string.IsNullOrWhiteSpace(diff))
            return null;

        // Truncate very large diffs to avoid token limits
        const int maxDiffLength = 20_000;
        if (diff.Length > maxDiffLength)
            diff = diff[..maxDiffLength] + "\n\n[... diff truncated ...]";

        // Resolve the claude executable path from settings
        var config = _configurationService.Load();
        var claudePath = Environment.ExpandEnvironmentVariables(config.Settings.CustomCommand);

        // Verify it looks like a claude command
        var claudeFileName = System.IO.Path.GetFileNameWithoutExtension(claudePath).ToLowerInvariant();
        if (!claudeFileName.Contains("claude"))
            return null; // Not using Claude as custom command, skip AI generation

        // Combine prompt + diff as stdin (claude -p reads from stdin when no prompt arg given)
        var stdinContent = $"""
            Generate a conventional commit message for the following git diff.
            Format: type(scope): description

            Rules:
            - type: feat, fix, refactor, docs, chore, test, style, perf
            - scope: optional, the main area of change (short, lowercase)
            - description: imperative mood, lowercase, no period at end
            - First line MUST be under 72 characters
            - Add a blank line then a brief body (1-3 lines) only if the change is complex
            - Output ONLY the commit message, no explanation or markdown

            <diff>
            {diff}
            </diff>
            """;

        var (exitCode, output, _) = await _processService.RunAsync(
            claudePath,
            "-p --no-session-persistence",
            workingDirectory,
            stdin: stdinContent,
            timeout: TimeSpan.FromSeconds(30));

        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return null;

        // Clean up: remove markdown code fences if present
        var message = output.Trim();
        if (message.StartsWith("```"))
        {
            var lines = message.Split('\n');
            message = string.Join('\n', lines
                .SkipWhile(l => l.StartsWith("```"))
                .TakeWhile(l => !l.StartsWith("```")));
        }

        return message.Trim();
    }

    private void GenerateHeuristicMessage()
    {
        if (!StagedFiles.Any()) return;

        var files = StagedFiles.ToList();
        var prefix = DetermineConventionalPrefix(files);
        var scope = DetermineScope(files);
        var description = BuildDescription(files);

        CommitMessage = scope != null
            ? $"{prefix}({scope}): {description}"
            : $"{prefix}: {description}";
    }

    private static string DetermineConventionalPrefix(List<GitFileStatus> files)
    {
        if (files.All(f => IsTestFile(f.FilePath))) return "test";
        if (files.All(f => IsDocFile(f.FilePath))) return "docs";
        if (files.All(f => IsConfigFile(f.FilePath))) return "chore";
        if (files.All(f => f.Status == GitFileStatusType.Added)) return "feat";
        if (files.All(f => f.Status == GitFileStatusType.Deleted)) return "refactor";
        if (files.Any(f => f.Status == GitFileStatusType.Added)) return "feat";
        return "fix";
    }

    private static string? DetermineScope(List<GitFileStatus> files)
    {
        var directories = files
            .Select(f => System.IO.Path.GetDirectoryName(f.FilePath)?.Replace('\\', '/'))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .ToList();

        if (directories.Count == 1)
        {
            var parts = directories[0]!.Split('/');
            return parts.Last();
        }

        return null;
    }

    private static string BuildDescription(List<GitFileStatus> files)
    {
        if (files.Count == 1)
        {
            var file = files[0];
            var verb = file.Status switch
            {
                GitFileStatusType.Added => "add",
                GitFileStatusType.Deleted => "remove",
                GitFileStatusType.Renamed => "rename",
                _ => "update"
            };
            return $"{verb} {file.FileName}";
        }

        var added = files.Count(f => f.Status == GitFileStatusType.Added);
        var modified = files.Count(f => f.Status == GitFileStatusType.Modified);
        var deleted = files.Count(f => f.Status == GitFileStatusType.Deleted);

        var parts = new List<string>();
        if (added > 0) parts.Add($"add {added} file{(added > 1 ? "s" : "")}");
        if (modified > 0) parts.Add($"update {modified} file{(modified > 1 ? "s" : "")}");
        if (deleted > 0) parts.Add($"remove {deleted} file{(deleted > 1 ? "s" : "")}");

        return string.Join(", ", parts);
    }

    private static bool IsTestFile(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.Contains("test") || lower.Contains("spec") ||
               lower.Contains(".test.") || lower.Contains(".spec.");
    }

    private static bool IsDocFile(string path)
    {
        var lower = path.ToLowerInvariant();
        var ext = System.IO.Path.GetExtension(lower);
        return ext is ".md" or ".txt" or ".rst" or ".adoc"
            || lower.Contains("readme") || lower.Contains("doc/") || lower.Contains("docs/");
    }

    private static bool IsConfigFile(string path)
    {
        var lower = path.ToLowerInvariant();
        var ext = System.IO.Path.GetExtension(lower);
        var name = System.IO.Path.GetFileName(lower);
        return ext is ".json" or ".yml" or ".yaml" or ".toml" or ".ini" or ".cfg" or ".config" or ".xml" or ".props" or ".targets"
            || name.StartsWith(".")
            || name is "dockerfile" or "makefile" or ".gitignore" or ".editorconfig";
    }

    #endregion

    #region Tree View

    [RelayCommand]
    private void ToggleTreeView()
    {
        IsTreeView = !IsTreeView;
        if (IsTreeView)
        {
            BuildFileTrees();
        }
    }

    private void BuildFileTrees()
    {
        StagedTree = new ObservableCollection<FileTreeNode>(BuildFileTree(StagedFiles));
        UnstagedTree = new ObservableCollection<FileTreeNode>(BuildFileTree(UnstagedFiles));
    }

    private static List<FileTreeNode> BuildFileTree(IEnumerable<GitFileStatus> files)
    {
        var root = new List<FileTreeNode>();
        var folderMap = new Dictionary<string, FileTreeNode>();

        foreach (var file in files)
        {
            var parts = file.FilePath.Replace('\\', '/').Split('/');
            var currentList = root;
            var currentPath = "";

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
                bool isLeaf = i == parts.Length - 1;

                if (isLeaf)
                {
                    currentList.Add(new FileTreeNode
                    {
                        Name = part,
                        FullPath = file.FilePath,
                        IsFolder = false,
                        FileStatus = file
                    });
                }
                else
                {
                    if (!folderMap.TryGetValue(currentPath, out var folder))
                    {
                        folder = new FileTreeNode
                        {
                            Name = part,
                            FullPath = currentPath,
                            IsFolder = true,
                            IsExpanded = true
                        };
                        folderMap[currentPath] = folder;
                        currentList.Add(folder);
                    }
                    currentList = folder.Children;
                }
            }
        }

        // Update file counts
        UpdateFileCount(root);
        return root;
    }

    private static int UpdateFileCount(List<FileTreeNode> nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            if (node.IsFolder)
            {
                node.FileCount = UpdateFileCount(node.Children);
                count += node.FileCount;
            }
            else
            {
                count++;
            }
        }
        return count;
    }

    #endregion

    #region Hunk Staging

    [RelayCommand]
    private async Task StageHunkAsync(int hunkIndex)
    {
        if (CurrentParsedDiff == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var patch = _diffParserService.ExtractHunkPatch(CurrentParsedDiff, hunkIndex);
        if (string.IsNullOrEmpty(patch)) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.StageHunkAsync(
                _currentTerminalTab.Pair.WorkingDirectory, patch);

            if (result.Success)
            {
                _toastService.Show("Hunk staged", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to stage hunk: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task UnstageHunkAsync(int hunkIndex)
    {
        if (CurrentParsedDiff == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var patch = _diffParserService.ExtractHunkPatch(CurrentParsedDiff, hunkIndex);
        if (string.IsNullOrEmpty(patch)) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.UnstageHunkAsync(
                _currentTerminalTab.Pair.WorkingDirectory, patch);

            if (result.Success)
            {
                _toastService.Show("Hunk unstaged", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to unstage hunk: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DiscardHunkAsync(int hunkIndex)
    {
        if (CurrentParsedDiff == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var patch = _diffParserService.ExtractHunkPatch(CurrentParsedDiff, hunkIndex);
        if (string.IsNullOrEmpty(patch)) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.DiscardHunkAsync(
                _currentTerminalTab.Pair.WorkingDirectory, patch);

            if (result.Success)
            {
                _toastService.Show("Hunk discarded", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to discard hunk: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Undo Commit

    [RelayCommand]
    private async Task UndoLastCommitAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var confirmed = _dialogService.ShowConfirmation(
            "Undo the last commit? Changes will be kept staged.",
            "Undo Last Commit");
        if (!confirmed) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.UndoLastCommitAsync(
                _currentTerminalTab.Pair.WorkingDirectory);

            if (result.Success)
            {
                _toastService.Show("Last commit undone", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to undo commit: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Merge Conflict

    public event EventHandler? MergeConflictRequested;

    [RelayCommand]
    private void OpenMergeConflictResolver()
    {
        MergeConflictRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Event Handlers

    partial void OnSelectedGitFileChanged(GitFileStatus? value)
    {
        UpdateButtonsEnabledState();
        LoadDiffForSelectedFileAsync(value);
    }

    partial void OnChangesViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsWorkingView));
        OnPropertyChanged(nameof(IsBranchView));

        if (value == "Branch")
        {
            _ = LoadBranchChangesAsync();
        }
    }

    partial void OnSelectedBranchFileChanged(GitFileStatus? value)
    {
        LoadBranchFileDiffAsync(value);
    }

    #endregion

    #region Branch Changes Mode

    [RelayCommand]
    private void SetChangesViewMode(string mode)
    {
        ChangesViewMode = mode;
    }

    private async Task LoadBranchChangesAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;

        // Auto-detect base branch from key branches config
        if (string.IsNullOrEmpty(BaseBranchName))
        {
            var config = _configurationService.Load();
            var dirSettings = config.DirectorySettings.TryGetValue(workingDirectory.ToLowerInvariant(), out var ds) ? ds : null;
            var keyBranchPatterns = dirSettings?.KeyBranchOverrides ?? config.Settings.KeyBranches;

            var keyBranches = await _gitStatusService.GetKeyBranchesAsync(workingDirectory, keyBranchPatterns);
            var currentBranch = _currentTerminalTab.GitStatus?.BranchName;

            // Pick the closest key branch (fewest commits ahead = most likely parent)
            var candidates = keyBranches.Where(b => b.ShortName != currentBranch).ToList();
            string? bestBranch = null;
            int bestDistance = int.MaxValue;

            foreach (var branch in candidates)
            {
                var (ahead, _) = await _gitStatusService.GetAheadBehindAsync(workingDirectory, "HEAD", branch.Name);
                if (ahead >= 0 && ahead < bestDistance)
                {
                    bestDistance = ahead;
                    bestBranch = branch.ShortName;
                }
            }

            BaseBranchName = bestBranch
                ?? candidates.FirstOrDefault()?.ShortName
                ?? keyBranches.FirstOrDefault()?.ShortName
                ?? "main";
        }

        IsLoading = true;
        try
        {
            var files = await _gitStatusService.GetChangedFilesBetweenBranchesAsync(
                workingDirectory, BaseBranchName, "HEAD");
            BranchChangedFiles = new ObservableCollection<GitFileStatus>(files);

            SelectedBranchFile = BranchChangedFiles.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _toastService.Show($"Failed to load branch changes: {ex.Message}", ToastType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void LoadBranchFileDiffAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null || string.IsNullOrEmpty(BaseBranchName))
        {
            BranchDiffText = "";
            return;
        }

        var diff = await _gitStatusService.GetFileDiffBetweenBranchesAsync(
            _currentTerminalTab.Pair.WorkingDirectory, BaseBranchName, "HEAD", file.FilePath);

        BranchDiffText = diff ?? "";
    }

    #endregion

    #region Private Methods

    private async void LoadDiffForSelectedFileAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            DiffText = "";
            CurrentParsedDiff = null;
            return;
        }

        // Handle submodules specially - don't try to load a diff as it can hang
        if (file.IsSubmodule)
        {
            DiffText = $"Submodule: {file.FilePath}\n\nDiff preview is not available for submodules.\n\nTo view submodule changes, navigate to the submodule directory\nand use git commands directly.";
            CurrentParsedDiff = null;
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var diff = await _gitStatusService.GetFileDiffAsync(workingDirectory, file.FilePath, file.IsStaged);

        if (!string.IsNullOrEmpty(diff))
        {
            DiffText = diff;
            CurrentParsedDiff = _diffParserService.Parse(diff);
        }
        else
        {
            DiffText = "";
            CurrentParsedDiff = null;
        }
    }

    private void UpdateButtonsEnabledState()
    {
        PreviewFileCommand.NotifyCanExecuteChanged();
        EditFileCommand.NotifyCanExecuteChanged();
        ExploreFileCommand.NotifyCanExecuteChanged();
        UpdateStagingButtonsState();
    }

    private void UpdateStagingButtonsState()
    {
        StageFileCommand.NotifyCanExecuteChanged();
        UnstageFileCommand.NotifyCanExecuteChanged();
        StageAllCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
        CreateCommitCommand.NotifyCanExecuteChanged();
    }

    #endregion
}
