using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class FileExplorerViewModel : ObservableObject, IDisposable
{
    private readonly IFileExplorerService _fileExplorerService;
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IClipboardService _clipboardService;
    private IDisposable? _fileWatcher;
    private System.Timers.Timer? _refreshDebounceTimer;
    private string? _selectedPathBeforeRefresh;
    private bool _isDisposed;
    private bool _isRefreshing;

    [ObservableProperty]
    private string _rootPath = "";

    [ObservableProperty]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private string _lastChangedFile = "";

    [ObservableProperty]
    private FileSystemNode? _rootNode;

    [ObservableProperty]
    private FileSystemNode? _selectedNode;

    [ObservableProperty]
    private bool _showHiddenFiles;

    [ObservableProperty]
    private bool _isLoading;

    public FileExplorerViewModel(
        IFileExplorerService fileExplorerService,
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IFileSystem fileSystem,
        IProcessService processService,
        IDispatcherService dispatcherService,
        IClipboardService clipboardService)
    {
        _fileExplorerService = fileExplorerService;
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _processService = processService;
        _dispatcherService = dispatcherService;
        _clipboardService = clipboardService;
    }

    public async Task InitializeAsync(string workingDirectory)
    {
        RootPath = workingDirectory;

        // Create root node
        RootNode = new FileSystemNode
        {
            Name = Path.GetFileName(workingDirectory) ?? workingDirectory,
            FullPath = workingDirectory,
            IsDirectory = true,
            IsExpanded = true,
            ChildrenLoaded = false
        };

        // Load root children
        await LoadChildrenAsync(RootNode);
        RootNode.IsExpanded = true;

        // Start file watcher
        StartFileWatcher();

        // Initial git status refresh
        await RefreshGitStatusAsync();
    }

    private void StartFileWatcher()
    {
        _fileWatcher?.Dispose();

        if (string.IsNullOrEmpty(RootPath) || !_fileSystem.DirectoryExists(RootPath))
            return;

        _fileWatcher = _fileExplorerService.WatchDirectory(RootPath, OnFileSystemChanged);
    }

    private static bool ShouldIgnoreFileChange(string path)
    {
        // Normalize path for comparison
        var normalizedPath = path.Replace('/', '\\').ToLowerInvariant();

        // Ignore .git folder and its contents
        if (normalizedPath.Contains("\\.git\\") || normalizedPath.EndsWith("\\.git"))
            return true;

        // Ignore common build/temp directories
        var ignoredFolders = new[] { "\\bin\\", "\\obj\\", "\\node_modules\\", "\\.vs\\", "\\packages\\", "\\__pycache__\\", "\\.idea\\", "\\.vscode\\" };
        foreach (var folder in ignoredFolders)
        {
            if (normalizedPath.Contains(folder))
                return true;
        }

        // Ignore common temp file patterns
        var ignoredExtensions = new[] { ".tmp", ".temp", ".log", ".suo", ".user", ".cache" };
        foreach (var ext in ignoredExtensions)
        {
            if (normalizedPath.EndsWith(ext))
                return true;
        }

        // Ignore files starting with ~ (temp files)
        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("~") || fileName.StartsWith(".#"))
            return true;

        return false;
    }

    private void OnFileSystemChanged(FileSystemWatcherEventArgs e)
    {
        if (_isDisposed || _isRefreshing) return;

        // Ignore common noise patterns
        if (ShouldIgnoreFileChange(e.Path))
            return;

        // Store the changed file path for debugging
        var changedPath = e.Path;

        // Debounce file changes - only show indicator after changes settle
        _refreshDebounceTimer?.Stop();
        _refreshDebounceTimer?.Dispose();

        _refreshDebounceTimer = new System.Timers.Timer(2000); // 2 second debounce
        _refreshDebounceTimer.Elapsed += async (s, _) =>
        {
            _refreshDebounceTimer?.Stop();
            if (_isDisposed) return;

            await _dispatcherService.InvokeAsync(async () =>
            {
                if (_isDisposed) return;
                // Mark that file system has changed - user can manually refresh
                HasPendingChanges = true;
                LastChangedFile = changedPath;
                // Refresh git status
                await RefreshGitStatusAsync();
            });
        };
        _refreshDebounceTimer.AutoReset = false;
        _refreshDebounceTimer.Start();
    }

    private async Task RefreshCurrentFolderAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;

        try
        {
            // Store selection
            _selectedPathBeforeRefresh = SelectedNode?.FullPath;

            // Refresh the root node's children while preserving expanded state
            if (RootNode != null)
            {
                await RefreshNodeChildrenAsync(RootNode);
            }

            // Restore selection
            if (_selectedPathBeforeRefresh != null)
            {
                RestoreSelection(RootNode, _selectedPathBeforeRefresh);
            }

            HasPendingChanges = false;
        }
        finally
        {
            _isRefreshing = false;
        }

        // Refresh git status
        await RefreshGitStatusAsync();
    }

    private async Task RefreshNodeChildrenAsync(FileSystemNode node)
    {
        if (!node.IsDirectory || !node.ChildrenLoaded)
            return;

        var expandedPaths = new HashSet<string>();
        CollectExpandedPaths(node, expandedPaths);

        // Reload children
        var children = await _fileExplorerService.GetChildrenAsync(node.FullPath, ShowHiddenFiles);

        node.Children.Clear();
        foreach (var child in children)
        {
            child.Parent = node;

            // Restore expanded state
            if (child.IsDirectory && expandedPaths.Contains(child.FullPath))
            {
                await LoadChildrenAsync(child);
                child.IsExpanded = true;
                await RefreshNodeChildrenAsync(child);
            }
            else if (child.IsDirectory)
            {
                // Check if has children for lazy loading
                var childPath = child.FullPath;
                var hasChildren = await Task.Run(() =>
                {
                    try
                    {
                        var dirs = _fileSystem.GetDirectories(childPath);
                        var files = _fileSystem.GetFiles(childPath);
                        return dirs.Length > 0 || files.Length > 0;
                    }
                    catch { return false; }
                });

                if (hasChildren)
                {
                    child.Children.Add(FileSystemNode.CreateDummy());
                }
            }

            node.Children.Add(child);
        }
    }

    private static void CollectExpandedPaths(FileSystemNode node, HashSet<string> paths)
    {
        if (node.IsExpanded && node.IsDirectory)
        {
            paths.Add(node.FullPath);
            foreach (var child in node.Children.Where(c => !c.IsLoading))
            {
                CollectExpandedPaths(child, paths);
            }
        }
    }

    private void RestoreSelection(FileSystemNode? node, string targetPath)
    {
        if (node == null) return;

        if (node.FullPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
        {
            SelectedNode = node;
            return;
        }

        foreach (var child in node.Children.Where(c => !c.IsLoading))
        {
            RestoreSelection(child, targetPath);
        }
    }

    public async Task LoadChildrenAsync(FileSystemNode node)
    {
        if (!node.IsDirectory || node.ChildrenLoaded)
            return;

        node.IsLoading = true;

        // Remove dummy node
        node.Children.Clear();

        var children = await _fileExplorerService.GetChildrenAsync(node.FullPath, ShowHiddenFiles);

        foreach (var child in children)
        {
            child.Parent = node;

            // Add dummy child for directories (lazy loading)
            if (child.IsDirectory)
            {
                var childPath = child.FullPath;
                var hasChildren = await Task.Run(() =>
                {
                    try
                    {
                        var dirs = _fileSystem.GetDirectories(childPath);
                        var files = _fileSystem.GetFiles(childPath);
                        return dirs.Length > 0 || files.Length > 0;
                    }
                    catch { return false; }
                });

                if (hasChildren)
                {
                    child.Children.Add(FileSystemNode.CreateDummy());
                }
            }

            node.Children.Add(child);
        }

        node.ChildrenLoaded = true;
        node.IsLoading = false;

        // Apply git status to new children
        await RefreshGitStatusAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (RootNode == null) return;

        IsLoading = true;
        await RefreshCurrentFolderAsync();
        IsLoading = false;
    }

    [RelayCommand]
    public async Task RefreshGitStatusAsync()
    {
        if (string.IsNullOrEmpty(RootPath) || RootNode == null)
            return;

        try
        {
            var gitFiles = await _gitStatusService.GetModifiedFilesAsync(RootPath);
            _fileExplorerService.ApplyGitStatus(
                GetAllNodes(RootNode),
                gitFiles,
                RootPath);
        }
        catch
        {
            // Ignore git status errors (might not be a git repo)
        }
    }

    private static IEnumerable<FileSystemNode> GetAllNodes(FileSystemNode root)
    {
        yield return root;
        foreach (var child in root.Children.Where(c => !c.IsLoading))
        {
            foreach (var node in GetAllNodes(child))
            {
                yield return node;
            }
        }
    }

    partial void OnShowHiddenFilesChanged(bool value)
    {
        _ = RefreshAsync();
    }

    // Commands
    [RelayCommand]
    private async Task ExpandNodeAsync(FileSystemNode node)
    {
        if (!node.IsDirectory) return;

        if (!node.ChildrenLoaded)
        {
            await LoadChildrenAsync(node);
        }

        node.IsExpanded = true;
    }

    [RelayCommand]
    private static void CollapseNode(FileSystemNode node)
    {
        if (!node.IsDirectory) return;
        node.IsExpanded = false;
    }

    public bool CanCreateFile => !string.IsNullOrEmpty(RootPath);

    [RelayCommand(CanExecute = nameof(CanCreateFile))]
    private async Task NewFileAsync()
    {
        var parentPath = SelectedNode?.IsDirectory == true
            ? SelectedNode.FullPath
            : (SelectedNode?.Parent?.FullPath ?? RootPath);

        // Calculate relative path for display
        var relativePath = GetRelativePath(parentPath);
        var locationHint = string.IsNullOrEmpty(relativePath) ? "/" : $"/{relativePath}/";

        // Ask user for file name
        var fileName = _dialogService.ShowInput(
            $"Location: {locationHint}\n\nEnter the file name (use / for nested paths):",
            "New File",
            "NewFile.txt");

        if (string.IsNullOrWhiteSpace(fileName))
            return;

        // Normalize slashes to backslashes for Windows
        fileName = fileName.Replace('/', '\\').Trim();

        // Remove leading/trailing slashes
        fileName = fileName.Trim('\\');

        if (string.IsNullOrWhiteSpace(fileName))
            return;

        var newPath = Path.Combine(parentPath, fileName);

        // Check if file already exists
        if (_fileSystem.FileExists(newPath))
        {
            _dialogService.ShowError($"A file with this name already exists:\n{newPath}", "New File");
            return;
        }

        // Create parent directories if needed (for deep paths like "wwwroot/robots.txt")
        var parentDir = Path.GetDirectoryName(newPath);
        if (!string.IsNullOrEmpty(parentDir) && !_fileSystem.DirectoryExists(parentDir))
        {
            try
            {
                _fileSystem.CreateDirectory(parentDir);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to create parent directory:\n{ex.Message}", "New File");
                return;
            }
        }

        var success = await _fileExplorerService.CreateFileAsync(newPath);
        if (success)
        {
            await RefreshAsync();
            // Select the new file
            SelectNodeByPath(newPath);
        }
        else
        {
            _dialogService.ShowError("Failed to create file.", "New File");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateFile))]
    private async Task NewFolderAsync()
    {
        var parentPath = SelectedNode?.IsDirectory == true
            ? SelectedNode.FullPath
            : (SelectedNode?.Parent?.FullPath ?? RootPath);

        // Calculate relative path for display
        var relativePath = GetRelativePath(parentPath);
        var locationHint = string.IsNullOrEmpty(relativePath) ? "/" : $"/{relativePath}/";

        // Ask user for folder name
        var folderName = _dialogService.ShowInput(
            $"Location: {locationHint}\n\nEnter the folder name (use / for nested paths):",
            "New Folder",
            "NewFolder");

        if (string.IsNullOrWhiteSpace(folderName))
            return;

        // Normalize slashes to backslashes for Windows
        folderName = folderName.Replace('/', '\\').Trim();

        // Remove leading/trailing slashes
        folderName = folderName.Trim('\\');

        if (string.IsNullOrWhiteSpace(folderName))
            return;

        var newPath = Path.Combine(parentPath, folderName);

        // Check if folder already exists
        if (_fileSystem.DirectoryExists(newPath))
        {
            _dialogService.ShowError($"A folder with this name already exists:\n{newPath}", "New Folder");
            return;
        }

        var success = await _fileExplorerService.CreateDirectoryAsync(newPath);
        if (success)
        {
            await RefreshAsync();
            SelectNodeByPath(newPath);
        }
        else
        {
            _dialogService.ShowError("Failed to create folder.", "New Folder");
        }
    }

    public bool CanRename => SelectedNode != null && SelectedNode != RootNode;
    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename()
    {
        if (SelectedNode == null) return;
        RenameRequested?.Invoke(this, SelectedNode);
    }

    public async Task PerformRenameAsync(FileSystemNode node, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;

        var directory = Path.GetDirectoryName(node.FullPath)!;
        var newPath = Path.Combine(directory, newName);

        if (node.FullPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
            return;

        var success = await _fileExplorerService.RenameAsync(node.FullPath, newPath);
        if (success)
        {
            await RefreshAsync();
            SelectNodeByPath(newPath);
        }
        else
        {
            _dialogService.ShowError("Failed to rename. The name may already exist.", "Rename");
        }
    }

    public bool CanDelete => SelectedNode != null && SelectedNode != RootNode;
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (SelectedNode == null) return;

        var itemType = SelectedNode.IsDirectory ? "folder" : "file";
        var message = $"Are you sure you want to delete the {itemType} '{SelectedNode.Name}'?";

        if (SelectedNode.IsDirectory)
        {
            message += "\n\nThis will delete all contents inside the folder.";
        }

        if (!_dialogService.ShowConfirmation(message, "Delete"))
            return;

        var success = await _fileExplorerService.DeleteAsync(SelectedNode.FullPath);
        if (success)
        {
            await RefreshAsync();
        }
        else
        {
            _dialogService.ShowError($"Failed to delete {itemType}.", "Delete");
        }
    }

    public bool CanCopyPath => SelectedNode != null;
    [RelayCommand(CanExecute = nameof(CanCopyPath))]
    private async Task CopyPathAsync()
    {
        if (SelectedNode == null) return;

        try
        {
            await _clipboardService.SetTextAsync(SelectedNode.FullPath);
        }
        catch
        {
            // Clipboard access can fail
        }
    }

    public bool CanCdToFolder => SelectedNode?.IsDirectory == true;
    [RelayCommand(CanExecute = nameof(CanCdToFolder))]
    private void CdToFolder()
    {
        if (SelectedNode?.IsDirectory != true) return;
        CdToShellRequested?.Invoke(this, SelectedNode.FullPath);
    }

    public bool CanOpenExternal => SelectedNode != null;
    [RelayCommand(CanExecute = nameof(CanOpenExternal))]
    private void OpenExternal()
    {
        if (SelectedNode == null) return;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = SelectedNode.FullPath,
                UseShellExecute = true
            };
            _processService.Start(psi);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to open: {ex.Message}", "Open External");
        }
    }

    public bool CanOpenInViewer => SelectedNode != null && !SelectedNode.IsDirectory;
    [RelayCommand(CanExecute = nameof(CanOpenInViewer))]
    private void OpenInViewer()
    {
        if (SelectedNode?.IsDirectory != false) return;

        // Open in popup viewer
        FileViewerRequested?.Invoke(this, new FileViewerRequestedEventArgs
        {
            FilePath = SelectedNode.FullPath,
            Mode = FileViewerMode.Preview
        });
    }

    [RelayCommand(CanExecute = nameof(CanOpenInViewer))]
    private void EditInViewer()
    {
        if (SelectedNode?.IsDirectory != false) return;

        // Open in popup viewer in edit mode
        FileViewerRequested?.Invoke(this, new FileViewerRequestedEventArgs
        {
            FilePath = SelectedNode.FullPath,
            Mode = FileViewerMode.Edit
        });
    }

    [RelayCommand(CanExecute = nameof(CanOpenInViewer))]
    private void PopOutViewer()
    {
        if (SelectedNode?.IsDirectory != false) return;

        // Open in detached window
        PopOutRequested?.Invoke(this, new FileViewerRequestedEventArgs
        {
            FilePath = SelectedNode.FullPath,
            Mode = FileViewerMode.Preview
        });
    }

    public bool CanExploreInExplorer => SelectedNode != null;
    [RelayCommand(CanExecute = nameof(CanExploreInExplorer))]
    private void ExploreInExplorer()
    {
        if (SelectedNode == null) return;

        try
        {
            if (SelectedNode.IsDirectory)
            {
                _processService.OpenFolder(SelectedNode.FullPath);
            }
            else
            {
                _processService.RevealInFinder(SelectedNode.FullPath);
            }
        }
        catch
        {
            // Ignore explorer errors
        }
    }

    private void SelectNodeByPath(string path)
    {
        if (RootNode == null) return;
        RestoreSelection(RootNode, path);
    }

    private string GetRelativePath(string fullPath)
    {
        if (string.IsNullOrEmpty(RootPath) || string.IsNullOrEmpty(fullPath))
            return string.Empty;

        // Normalize paths
        var rootNormalized = RootPath.TrimEnd('\\', '/');
        var pathNormalized = fullPath.TrimEnd('\\', '/');

        if (pathNormalized.Equals(rootNormalized, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (pathNormalized.StartsWith(rootNormalized + "\\", StringComparison.OrdinalIgnoreCase) ||
            pathNormalized.StartsWith(rootNormalized + "/", StringComparison.OrdinalIgnoreCase))
        {
            return pathNormalized.Substring(rootNormalized.Length + 1).Replace('\\', '/');
        }

        return fullPath.Replace('\\', '/');
    }

    partial void OnSelectedNodeChanged(FileSystemNode? value)
    {
        // Update command states
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        CdToFolderCommand.NotifyCanExecuteChanged();
        OpenExternalCommand.NotifyCanExecuteChanged();
        OpenInViewerCommand.NotifyCanExecuteChanged();
        EditInViewerCommand.NotifyCanExecuteChanged();
        PopOutViewerCommand.NotifyCanExecuteChanged();
        ExploreInExplorerCommand.NotifyCanExecuteChanged();
    }

    // Events
    public event EventHandler<FileViewerRequestedEventArgs>? FileViewerRequested;
    public event EventHandler<FileViewerRequestedEventArgs>? PopOutRequested;
    public event EventHandler<string>? CdToShellRequested;
    public event EventHandler<FileSystemNode>? RenameRequested;

    public void Dispose()
    {
        _isDisposed = true;
        _fileWatcher?.Dispose();
        _refreshDebounceTimer?.Dispose();
    }
}

public class FileViewerRequestedEventArgs : EventArgs
{
    public required string FilePath { get; init; }
    public FileViewerMode Mode { get; init; } = FileViewerMode.Preview;
}

public enum FileViewerMode
{
    Preview,
    Edit,
    SideBySide
}
