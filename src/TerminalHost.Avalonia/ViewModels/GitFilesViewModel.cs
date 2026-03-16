using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class GitFilesViewModel : ObservableObject
{
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svg", ".tiff", ".tif",
        ".psd", ".ai", ".eps", ".raw", ".cr2", ".nef", ".heic", ".avif",
        // Audio
        ".mp3", ".wav", ".ogg", ".flac", ".aac", ".wma", ".m4a",
        // Video
        ".mp4", ".avi", ".mkv", ".mov", ".webm", ".wmv", ".flv", ".m4v",
        // Archives
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".zst",
        // Executables/Libraries
        ".exe", ".dll", ".so", ".dylib", ".msi", ".app", ".deb", ".rpm",
        // Documents
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        // Fonts
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        // Database
        ".db", ".sqlite", ".sqlite3",
        // Other binary
        ".bin", ".dat", ".class", ".pyc", ".pyo", ".o", ".obj", ".lib", ".a",
        ".nupkg", ".snupkg", ".whl",
    };

    private const long MaxDiffFileSize = 5 * 1024 * 1024; // 5 MB

    private static bool IsBinaryFile(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext);
    }

    private readonly IGitStatusService _gitStatusService;
    private readonly IFilePreviewService _filePreviewService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
    private readonly IConfigurationService _configurationService;
    private readonly IToastService _toastService;
    private readonly IInvisibleChangeService _invisibleChangeService;
    private readonly IAiExecutionService _aiExecutionService;
    private readonly IClipboardService _clipboardService;
    private readonly IDiffParserService _diffParserService;
    private readonly IGitIgnoreService _gitIgnoreService;
    private TerminalPairTabViewModel? _currentTerminalTab;

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
    private bool _isOpen;

    [ObservableProperty]
    private bool _isDragging;

    [ObservableProperty]
    private double _width = 1100;

    [ObservableProperty]
    private double _height = 700;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private string _commitMessage = "";

    [ObservableProperty]
    private bool _isAmend;

    [ObservableProperty]
    private InvisibleChangeInfo? _invisibleChangeInfo;

    [ObservableProperty]
    private bool _isMergeInProgress;

    [ObservableProperty]
    private int _conflictCount;

    [ObservableProperty]
    private ParsedDiff? _currentParsedDiff;

    [ObservableProperty]
    private int _commitMessageLength;

    private const int MaxCommitMessageLength = 72; // Conventional max for first line

    public bool IsSubjectTooLong => CommitMessageLength > MaxCommitMessageLength;

    public string FileChangeSummary => GitFiles?.Count switch
    {
        null or 0 => "No changes",
        1 => "1 file changed",
        var n => $"{n} files changed"
    };

    public GitFilesViewModel(IGitStatusService gitStatusService, IFilePreviewService filePreviewService, IDialogService dialogService, IFileSystem fileSystem, IProcessService processService, IConfigurationService configurationService, IToastService toastService, IInvisibleChangeService invisibleChangeService, IAiExecutionService aiExecutionService, IClipboardService clipboardService, IDiffParserService diffParserService, IGitIgnoreService gitIgnoreService)
    {
        _gitStatusService = gitStatusService;
        _filePreviewService = filePreviewService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _processService = processService;
        _configurationService = configurationService;
        _toastService = toastService;
        _invisibleChangeService = invisibleChangeService;
        _aiExecutionService = aiExecutionService;
        _clipboardService = clipboardService;
        _diffParserService = diffParserService;
        _gitIgnoreService = gitIgnoreService;
        _diffText = "";
    }

    partial void OnCommitMessageChanged(string value)
    {
        // Get first line length for the counter
        var firstLineEnd = value.IndexOf('\n');
        CommitMessageLength = firstLineEnd >= 0 ? firstLineEnd : value.Length;
        OnPropertyChanged(nameof(IsSubjectTooLong));
    }

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

        await LoadDataAsync(terminalTab);

        IsOpen = true;
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
        IsOpen = false;
        SelectedGitFile = null;
        DiffText = "";
        CommitMessage = "";
        IsAmend = false;
        InvisibleChangeInfo = null;
        DiffExplanation = null;
        FileDiffExplanation = null;
        ChangesViewMode = "Working";
        BranchChangedFiles.Clear();
        SelectedBranchFile = null;
        BranchDiffText = "";
        BaseBranchName = "";
        _currentTerminalTab = null;
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
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        // Get modified files and separate them into staged/unstaged by their IsStaged property
        var allFiles = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);
        var staged = allFiles.Where(f => f.IsStaged).ToList();
        var unstaged = allFiles.Where(f => !f.IsStaged).ToList();

        StagedFiles = new ObservableCollection<GitFileStatus>(staged);
        UnstagedFiles = new ObservableCollection<GitFileStatus>(unstaged);

        // Also update combined list for compatibility
        GitFiles = new ObservableCollection<GitFileStatus>(allFiles);

        IsEmptyStateVisible = !StagedFiles.Any() && !UnstagedFiles.Any();

        // Rebuild tree views if in tree mode
        if (IsTreeView)
        {
            BuildFileTrees();
        }

        // Check for merge in progress
        IsMergeInProgress = await _gitStatusService.IsMergeInProgressAsync(workingDirectory);
        ConflictCount = allFiles.Count(f => f.Status == GitFileStatusType.Conflicted);

        // Update computed properties
        OnPropertyChanged(nameof(FileChangeSummary));

        // Update command states
        StageAllCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
        CommitCommand.NotifyCanExecuteChanged();

        SelectedGitFile = null;
        DiffText = "";
        CurrentParsedDiff = null;

        // Select first unstaged file if any, otherwise first staged
        if (UnstagedFiles.Any())
        {
            SelectedGitFile = UnstagedFiles.First();
        }
        else if (StagedFiles.Any())
        {
            SelectedGitFile = StagedFiles.First();
        }
    }

    partial void OnSelectedGitFileChanged(GitFileStatus? value)
    {
        FileDiffExplanation = null;
        UpdateButtonsEnabledState();
        LoadDiffForSelectedFileAsync(value);
    }

    private async void LoadDiffForSelectedFileAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            DiffText = "";
            CurrentParsedDiff = null;
            InvisibleChangeInfo = null;
            return;
        }

        // Handle submodules specially - don't try to load a diff as it can hang
        if (file.IsSubmodule)
        {
            DiffText = $"Submodule: {file.FilePath}\n\nDiff preview is not available for submodules.\n\nTo view submodule changes, navigate to the submodule directory\nand use git commands directly.";
            CurrentParsedDiff = null;
            InvisibleChangeInfo = null;
            return;
        }

        // Skip binary files - diff is not meaningful
        if (IsBinaryFile(file.FilePath))
        {
            DiffText = $"Binary file {file.FileName} differs";
            CurrentParsedDiff = null;
            InvisibleChangeInfo = null;
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;

        // Skip large files to prevent UI hangs
        var fullPath = System.IO.Path.Combine(workingDirectory, file.FilePath);
        if (_fileSystem.FileExists(fullPath))
        {
            try
            {
                var fileInfo = new System.IO.FileInfo(fullPath);
                if (fileInfo.Length > MaxDiffFileSize)
                {
                    DiffText = $"File too large to display ({fileInfo.Length / 1024.0 / 1024.0:F1} MB)";
                    CurrentParsedDiff = null;
                    InvisibleChangeInfo = null;
                    return;
                }
            }
            catch { /* ignore - file might be deleted */ }
        }

        var diff = await _gitStatusService.GetFileDiffAsync(workingDirectory, file.FilePath, file.IsStaged);

        if (!string.IsNullOrEmpty(diff))
        {
            DiffText = diff;
            CurrentParsedDiff = _diffParserService.Parse(diff);

            // Detect invisible changes (EOL, BOM, trailing newline)
            var info = _invisibleChangeService.Detect(diff);
            if (info is { HasEolChange: true, IsEntirelyInvisible: true })
            {
                var diagnosis = await _invisibleChangeService.DiagnoseEolIssueAsync(workingDirectory, file.FilePath);
                if (diagnosis != null) info.Diagnosis = diagnosis;
            }
            InvisibleChangeInfo = info;
        }
        else
        {
            DiffText = "";
            CurrentParsedDiff = null;
            InvisibleChangeInfo = null;
        }
    }

    private void UpdateButtonsEnabledState()
    {
        PreviewFileCommand.NotifyCanExecuteChanged();
        EditFileCommand.NotifyCanExecuteChanged();
        ExploreFileCommand.NotifyCanExecuteChanged();
    }

    public bool CanPreviewFile => SelectedGitFile != null && SelectedGitFile.Status != GitFileStatusType.Deleted && !SelectedGitFile.IsSubmodule;
    [RelayCommand(CanExecute = nameof(CanPreviewFile))]
    private void PreviewFile()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var fullPath = System.IO.Path.Combine(_currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);
        FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs { FilePath = fullPath });
        Close();
    }

    public bool CanEditFile => SelectedGitFile != null && SelectedGitFile.Status != GitFileStatusType.Deleted && !SelectedGitFile.IsSubmodule;
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
                // Reveal the file in the file manager
                _processService.RevealInFolder(fullPath);
            }
            else
            {
                // Open folder in file manager
                _processService.OpenFolder(directory!);
            }
        }
    }

    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
    public event EventHandler<FileEditRequestedEventArgs>? FileEditRequested;

    #region Staging Commands

    [RelayCommand]
    private async Task StageFileAsync(GitFileStatus file)
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var result = await _gitStatusService.StageFileAsync(_currentTerminalTab.Pair.WorkingDirectory, file.FilePath);
        if (result.Success)
        {
            await RefreshGitFilesAsync();
        }
        else
        {
            _toastService.Show($"Failed to stage {file.FileName}", ToastType.Error);
        }
    }

    [RelayCommand]
    private async Task UnstageFileAsync(GitFileStatus file)
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var result = await _gitStatusService.UnstageFileAsync(_currentTerminalTab.Pair.WorkingDirectory, file.FilePath);
        if (result.Success)
        {
            await RefreshGitFilesAsync();
        }
        else
        {
            _toastService.Show($"Failed to unstage {file.FileName}", ToastType.Error);
        }
    }

    public bool CanStageAll => UnstagedFiles.Any();
    [RelayCommand(CanExecute = nameof(CanStageAll))]
    private async Task StageAllAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var result = await _gitStatusService.StageAllAsync(_currentTerminalTab.Pair.WorkingDirectory);
        if (result.Success)
        {
            await RefreshGitFilesAsync();
        }
        else
        {
            _toastService.Show("Failed to stage all files", ToastType.Error);
        }
    }

    public bool CanUnstageAll => StagedFiles.Any();
    [RelayCommand(CanExecute = nameof(CanUnstageAll))]
    private async Task UnstageAllAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var result = await _gitStatusService.UnstageAllAsync(_currentTerminalTab.Pair.WorkingDirectory);
        if (result.Success)
        {
            await RefreshGitFilesAsync();
        }
        else
        {
            _toastService.Show("Failed to unstage all files", ToastType.Error);
        }
    }

    [RelayCommand]
    private async Task DiscardChangesAsync(GitFileStatus file)
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        // Confirm discard
        var confirmed = _dialogService.ShowConfirmation(
            $"Discard changes to {file.FileName}?\n\nThis will permanently delete your changes.",
            "Discard Changes");

        if (!confirmed) return;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;

        // For untracked files, delete the file instead of git checkout
        if (file.Status == GitFileStatusType.Untracked)
        {
            var fullPath = System.IO.Path.Combine(workingDirectory, file.FilePath);
            if (_fileSystem.FileExists(fullPath))
                _fileSystem.DeleteFile(fullPath);

            _toastService.Show($"Deleted untracked file {file.FileName}", ToastType.Success);
            await RefreshGitFilesAsync();
            return;
        }

        var result = await _gitStatusService.DiscardChangesAsync(workingDirectory, file.FilePath);
        if (result.Success)
        {
            _toastService.Show($"Discarded changes to {file.FileName}", ToastType.Success);
            await RefreshGitFilesAsync();
        }
        else
        {
            _toastService.Show($"Failed to discard changes to {file.FileName}", ToastType.Error);
        }
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

    #region Commit Commands

    public bool CanCommit => StagedFiles.Any() && (!string.IsNullOrWhiteSpace(CommitMessage) || IsAmend);

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        using var toast = _toastService.ShowProgress("Creating commit...");

        var result = await _gitStatusService.CreateCommitAsync(
            _currentTerminalTab.Pair.WorkingDirectory,
            CommitMessage,
            IsAmend);

        if (result.Success)
        {
            toast.Complete("Commit created");
            CommitMessage = "";
            IsAmend = false;
            await RefreshGitFilesAsync();
        }
        else
        {
            toast.Fail(result.Error ?? "Commit failed");
        }
    }

    [RelayCommand]
    private void InsertCommitPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(CommitMessage))
        {
            CommitMessage = $"{prefix}: ";
        }
        else if (!CommitMessage.StartsWith($"{prefix}:"))
        {
            // Replace existing prefix if any
            var colonIndex = CommitMessage.IndexOf(':');
            if (colonIndex > 0 && colonIndex < 15) // Assume prefix is short
            {
                CommitMessage = $"{prefix}:{CommitMessage.Substring(colonIndex + 1)}";
            }
            else
            {
                CommitMessage = $"{prefix}: {CommitMessage}";
            }
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

    #endregion

    #region Tree View

    [ObservableProperty]
    private bool _isTreeView;

    [ObservableProperty]
    private ObservableCollection<FileTreeNode> _stagedTree = [];

    [ObservableProperty]
    private ObservableCollection<FileTreeNode> _unstagedTree = [];

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

    #region Branch Changes Mode

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

    [ObservableProperty]
    private bool _isLoading;

    public bool IsWorkingView => ChangesViewMode == "Working";
    public bool IsBranchView => ChangesViewMode == "Branch";

    [RelayCommand]
    private void SetChangesViewMode(string mode)
    {
        ChangesViewMode = mode;
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

    private async Task LoadBranchChangesAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;

        // Auto-detect base branch
        if (string.IsNullOrEmpty(BaseBranchName))
        {
            var config = _configurationService.Load();
            var dirSettings = config.DirectorySettings.TryGetValue(workingDirectory.ToLowerInvariant(), out var ds) ? ds : null;
            var keyBranchPatterns = dirSettings?.KeyBranchOverrides ?? config.Settings.KeyBranches;

            var keyBranches = await _gitStatusService.GetKeyBranchesAsync(workingDirectory, keyBranchPatterns);
            var currentBranch = _currentTerminalTab.GitStatus?.BranchName;

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

    #region Invisible Changes

    [RelayCommand]
    private async Task FixInvisibleChangesAsync()
    {
        if (InvisibleChangeInfo == null || SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
            return;

        var info = InvisibleChangeInfo;
        var filePath = SelectedGitFile.FilePath;
        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;

        try
        {
            if (info.Diagnosis != null)
            {
                var success = await _invisibleChangeService.RunDiagnosisFixAsync(workingDirectory, info.Diagnosis);
                if (!success)
                {
                    _toastService.Show("Fix command failed", ToastType.Warning);
                    return;
                }
            }
            else if (info.IsEntirelyInvisible)
            {
                // For purely invisible changes, discard is the most reliable fix
                await _gitStatusService.DiscardChangesAsync(workingDirectory, filePath);
            }
            else
            {
                // For mixed changes (visible + invisible), use byte-level fix
                await _invisibleChangeService.FixAsync(workingDirectory, filePath, info);
            }

            _toastService.Show("Invisible changes reverted", ToastType.Success);
            InvisibleChangeInfo = null;
            // Refresh the file list
            await RefreshGitFilesAsync();
        }
        catch (Exception ex)
        {
            // If fix fails, try to diagnose why
            if (info.HasEolChange)
            {
                var diagnosis = await _invisibleChangeService.DiagnoseEolIssueAsync(workingDirectory, filePath);
                if (diagnosis != null)
                {
                    info.Diagnosis = diagnosis;
                    InvisibleChangeInfo = null;
                    InvisibleChangeInfo = info; // Force re-notify
                }
                else
                {
                    _toastService.Show("Could not resolve invisible changes", ToastType.Warning);
                }
            }
            else
            {
                _toastService.Show($"Fix failed: {ex.Message}", ToastType.Warning);
            }
        }
    }

    #endregion

    #region AI Assistance

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExplanation))]
    private string? _diffExplanation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFileDiffExplanation))]
    private string? _fileDiffExplanation;

    [ObservableProperty]
    private bool _isExplainingDiff;

    [ObservableProperty]
    private bool _isExplainingFileDiff;

    [ObservableProperty]
    private bool _isGeneratingCommitMessage;

    public bool HasExplanation => !string.IsNullOrEmpty(DiffExplanation);
    public bool HasFileDiffExplanation => !string.IsNullOrEmpty(FileDiffExplanation);

    [RelayCommand]
    private async Task GenerateCommitMessageAsync()
    {
        if (!StagedFiles.Any()) return;
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var diff = await _gitStatusService.GetStagedDiffAsync(workingDirectory);
        if (string.IsNullOrWhiteSpace(diff))
        {
            _toastService.Show("No staged changes", ToastType.Warning);
            return;
        }
        if (diff.Length > 20_000) diff = diff[..20_000] + "\n\n[... diff truncated ...]";

        IsGeneratingCommitMessage = true;
        try
        {
            if (_aiExecutionService.IsAiAvailable())
            {
                try
                {
                    var prompt = $"""
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

                    var result = await _aiExecutionService.ExecuteAsync(prompt, workingDirectory, "Generating commit message");
                    if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                    {
                        var message = result.Output.Trim();
                        if (message.StartsWith("```"))
                        {
                            var lines = message.Split('\n');
                            message = string.Join('\n', lines
                                .SkipWhile(l => l.StartsWith("```"))
                                .TakeWhile(l => !l.StartsWith("```")));
                        }
                        CommitMessage = message.Trim();
                        return;
                    }
                }
                catch
                {
                    // AI unavailable, fall through to heuristic
                }
            }

            // Fallback: heuristic-based generation
            GenerateHeuristicMessage();
        }
        finally
        {
            IsGeneratingCommitMessage = false;
        }
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

    [RelayCommand]
    private async Task ExplainChangesAsync()
    {
        if (!StagedFiles.Any()) return;
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;
        if (!_aiExecutionService.IsAiAvailable()) return;

        var diff = await _gitStatusService.GetStagedDiffAsync(_currentTerminalTab.Pair.WorkingDirectory);
        if (string.IsNullOrWhiteSpace(diff))
        {
            _toastService.Show("No staged changes to explain", ToastType.Warning);
            return;
        }
        if (diff.Length > 20_000) diff = diff[..20_000] + "\n\n[... diff truncated ...]";

        IsExplainingDiff = true;
        try
        {
            var prompt = $"""
                Explain the following git diff in 2-4 sentences of plain English.
                Describe what changed and why (infer from context).
                No bullet points, no markdown, no code fences.

                <diff>
                {diff}
                </diff>
                """;

            var result = await _aiExecutionService.ExecuteAsync(prompt, _currentTerminalTab.Pair.WorkingDirectory, "Explaining changes");
            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                DiffExplanation = result.Output.Trim();
        }
        finally
        {
            IsExplainingDiff = false;
        }
    }

    [RelayCommand]
    private void DismissExplanation() => DiffExplanation = null;

    [RelayCommand]
    private async Task CopyDiffExplanationAsync()
    {
        if (!string.IsNullOrEmpty(DiffExplanation))
        {
            await _clipboardService.SetTextAsync(DiffExplanation);
            _toastService.Show("Copied to clipboard", ToastType.Success);
        }
    }

    [RelayCommand]
    private async Task ExplainFileDiffAsync()
    {
        if (string.IsNullOrWhiteSpace(DiffText)) return;
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;
        if (!_aiExecutionService.IsAiAvailable()) return;

        var diffContent = DiffText;
        if (diffContent.Length > 20_000) diffContent = diffContent[..20_000] + "\n\n[... diff truncated ...]";

        IsExplainingFileDiff = true;
        try
        {
            var prompt = $"""
                Explain the following git diff for a single file in 2-4 sentences of plain English.
                Describe what changed and why (infer from context).
                No bullet points, no markdown, no code fences.

                <diff>
                {diffContent}
                </diff>
                """;

            var result = await _aiExecutionService.ExecuteAsync(prompt, _currentTerminalTab.Pair.WorkingDirectory, "Explaining file diff");
            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                FileDiffExplanation = result.Output.Trim();
        }
        finally
        {
            IsExplainingFileDiff = false;
        }
    }

    [RelayCommand]
    private void DismissFileDiffExplanation() => FileDiffExplanation = null;

    [RelayCommand]
    private async Task CopyFileDiffExplanationAsync()
    {
        if (!string.IsNullOrEmpty(FileDiffExplanation))
        {
            await _clipboardService.SetTextAsync(FileDiffExplanation);
            _toastService.Show("Copied to clipboard", ToastType.Success);
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

    #region Quick Stash

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

    #endregion

    #region Add to .gitignore

    [RelayCommand]
    private async Task AddToGitignoreByPathAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var pattern = "/" + file.FilePath.Replace('\\', '/');
        await AppendToGitignoreAsync(workingDirectory, pattern, file.FileName);
    }

    [RelayCommand]
    private async Task AddToGitignoreByExtensionAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var ext = System.IO.Path.GetExtension(file.FilePath);
        if (string.IsNullOrEmpty(ext)) return;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var pattern = "*" + ext;
        await AppendToGitignoreAsync(workingDirectory, pattern, pattern);
    }

    [RelayCommand]
    private async Task AddToGitignoreByDirectoryAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var dir = System.IO.Path.GetDirectoryName(file.FilePath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(dir)) return;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var pattern = "/" + dir + "/";
        await AppendToGitignoreAsync(workingDirectory, pattern, dir);
    }

    private async Task AppendToGitignoreAsync(string workingDirectory, string pattern, string displayName)
    {
        var gitignorePath = System.IO.Path.Combine(workingDirectory, ".gitignore");

        try
        {
            // Check if pattern already exists
            if (_fileSystem.FileExists(gitignorePath))
            {
                var content = _fileSystem.ReadAllText(gitignorePath);
                var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
                if (lines.Any(l => l.Trim() == pattern))
                {
                    _toastService.Show($"'{pattern}' already in .gitignore", ToastType.Info);
                    return;
                }
            }

            // Append pattern (ensure newline before if file doesn't end with one)
            var appendText = pattern + "\n";
            if (_fileSystem.FileExists(gitignorePath))
            {
                var existing = _fileSystem.ReadAllText(gitignorePath);
                if (existing.Length > 0 && !existing.EndsWith("\n"))
                    appendText = "\n" + appendText;
            }

            _fileSystem.AppendAllText(gitignorePath, appendText);
            _toastService.Show($"Added '{displayName}' to .gitignore", ToastType.Success);
            await RefreshGitFilesAsync();
        }
        catch (Exception ex)
        {
            _toastService.Show($"Failed to update .gitignore: {ex.Message}", ToastType.Error);
        }
    }

    #endregion
}
