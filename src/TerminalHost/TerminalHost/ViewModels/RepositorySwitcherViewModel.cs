using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Repository Quick Access popup (Ctrl+Shift+O).
/// </summary>
public partial class RepositorySwitcherViewModel : ObservableObject
{
    private readonly IGitHubService _gitHubService;
    private readonly IConfigurationService _configService;
    private readonly MainViewModel _mainViewModel;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;

    private List<RepositoryItem> _allRepositories = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<RepositoryItem> _repositories = [];

    [ObservableProperty]
    private RepositoryItem? _selectedRepository;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // View properties for positioning/sizing the popup
    [ObservableProperty]
    private double _width = 600;

    [ObservableProperty]
    private double _height = 500;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    public RepositorySwitcherViewModel(
        IGitHubService gitHubService,
        IConfigurationService configService,
        MainViewModel mainViewModel,
        IDialogService dialogService,
        IFileSystem fileSystem)
    {
        _gitHubService = gitHubService;
        _configService = configService;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
    }

    [RelayCommand]
    public async Task OpenAsync()
    {
        SearchText = string.Empty;
        StatusMessage = string.Empty;

        await RefreshRepositoriesAsync();

        IsOpen = true;
    }

    [RelayCommand]
    private async Task RefreshRepositoriesAsync()
    {
        IsLoading = true;
        try
        {
            var config = _configService.Load();
            var repos = new List<RepositoryItem>();

            // 1. Get open folders (currently open in tabs)
            var openFolders = _mainViewModel.Tabs
                .OfType<TerminalPairTabViewModel>()
                .Select(t => t.Pair.WorkingDirectory)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 2. Get recent folders from config
            var recentPaths = config.Settings.Repositories.RecentPaths
                .Where(p => _fileSystem.DirectoryExists(p))
                .ToList();

            // 3. Get favorites
            var favorites = config.Settings.Repositories.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Add open folders first
            foreach (var folder in openFolders)
            {
                var fullName = GetRepositoryFullName(folder);
                repos.Add(new RepositoryItem
                {
                    FullName = fullName ?? Path.GetFileName(folder),
                    LocalPath = folder,
                    IsLocal = true,
                    IsOpen = true,
                    IsFavorite = favorites.Contains(fullName ?? folder),
                    LastAccessedAt = DateTime.UtcNow
                });
            }

            // Add recent folders (not already open)
            foreach (var path in recentPaths)
            {
                if (openFolders.Contains(path))
                    continue;

                var fullName = GetRepositoryFullName(path);
                repos.Add(new RepositoryItem
                {
                    FullName = fullName ?? Path.GetFileName(path),
                    LocalPath = path,
                    IsLocal = true,
                    IsOpen = false,
                    IsRecent = true,
                    IsFavorite = favorites.Contains(fullName ?? path)
                });
            }

            // 4. Scan configured paths for git repos
            foreach (var scanPath in config.Settings.Repositories.ScanPaths)
            {
                if (!_fileSystem.DirectoryExists(scanPath))
                    continue;

                try
                {
                    foreach (var dir in _fileSystem.GetDirectories(scanPath))
                    {
                        // Check if it's a git repo
                        var gitDir = Path.Combine(dir, ".git");
                        if (!_fileSystem.DirectoryExists(gitDir))
                            continue;

                        // Skip if already in list
                        if (repos.Any(r => r.LocalPath.Equals(dir, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var fullName = GetRepositoryFullName(dir);
                        repos.Add(new RepositoryItem
                        {
                            FullName = fullName ?? Path.GetFileName(dir),
                            LocalPath = dir,
                            IsLocal = true,
                            IsOpen = false,
                            IsFavorite = favorites.Contains(fullName ?? dir)
                        });
                    }
                }
                catch
                {
                    // Skip directories we can't access
                }
            }

            // 5. Get GitHub repos (if CLI available)
            if (_gitHubService.IsGitHubCliAvailable())
            {
                try
                {
                    var ghRepos = await _gitHubService.GetUserRepositoriesAsync(50);
                    foreach (var ghRepo in ghRepos)
                    {
                        // Skip if already in list (local clone exists)
                        if (repos.Any(r => r.FullName.Equals(ghRepo.FullName, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        repos.Add(new RepositoryItem
                        {
                            FullName = ghRepo.FullName,
                            LocalPath = "",
                            Description = ghRepo.Description,
                            IsLocal = false,
                            IsOpen = false,
                            IsFavorite = favorites.Contains(ghRepo.FullName),
                            LastAccessedAt = ghRepo.LastPushedAt
                        });
                    }
                }
                catch
                {
                    // GitHub API failed, continue with local repos
                }
            }

            _allRepositories = repos;
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var searchTextLower = SearchText.ToLowerInvariant();

        IEnumerable<RepositoryItem> filtered = _allRepositories;
        if (!string.IsNullOrEmpty(searchTextLower))
        {
            filtered = _allRepositories.Where(r =>
                r.FullName.ToLowerInvariant().Contains(searchTextLower) ||
                r.Name.ToLowerInvariant().Contains(searchTextLower) ||
                r.LocalPath.ToLowerInvariant().Contains(searchTextLower) ||
                r.Description.ToLowerInvariant().Contains(searchTextLower));
        }

        // Sort: favorites first, then open, then recent, then local, then by name
        var sorted = filtered
            .OrderByDescending(r => r.IsFavorite)
            .ThenByDescending(r => r.IsOpen)
            .ThenByDescending(r => r.IsRecent)
            .ThenByDescending(r => r.IsLocal)
            .ThenBy(r => r.FullName)
            .ToList();

        Repositories = new ObservableCollection<RepositoryItem>(sorted);

        StatusMessage = Repositories.Any() ? string.Empty : "No repositories found.";
        SelectedRepository = Repositories.FirstOrDefault();

        OpenRepositoryCommand.NotifyCanExecuteChanged();
        CloneRepositoryCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenRepository))]
    private void OpenRepository()
    {
        if (SelectedRepository == null)
            return;

        if (SelectedRepository.IsLocal && !string.IsNullOrEmpty(SelectedRepository.LocalPath))
        {
            // Open the local repository
            _mainViewModel.OpenProjectTab(SelectedRepository.LocalPath);
            UpdateRecentPath(SelectedRepository.LocalPath);
            IsOpen = false;
        }
        else if (!SelectedRepository.IsLocal)
        {
            // Need to clone first
            CloneAndOpenRepository();
        }
    }

    private bool CanOpenRepository() => SelectedRepository != null;

    [RelayCommand(CanExecute = nameof(CanCloneRepository))]
    private async Task CloneRepositoryAsync()
    {
        await CloneAndOpenRepositoryAsync();
    }

    private bool CanCloneRepository() => SelectedRepository != null && !SelectedRepository.IsLocal;

    private async void CloneAndOpenRepository()
    {
        await CloneAndOpenRepositoryAsync();
    }

    private async Task CloneAndOpenRepositoryAsync()
    {
        if (SelectedRepository == null || string.IsNullOrEmpty(SelectedRepository.FullName))
            return;

        if (!_gitHubService.IsGitHubCliAvailable())
        {
            IsOpen = false;
            _dialogService.ShowWarning("GitHub CLI (gh) is not available. Please install it to clone repositories.", "Clone Repository");
            return;
        }

        var config = _configService.Load();
        var cloneDir = config.Settings.Repositories.CloneDirectory;

        if (string.IsNullOrEmpty(cloneDir))
        {
            // Default to user's repos folder or home directory
            cloneDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "repos");
        }

        // Ensure clone directory exists
        if (!_fileSystem.DirectoryExists(cloneDir))
        {
            try
            {
                _fileSystem.CreateDirectory(cloneDir);
            }
            catch
            {
                IsOpen = false;
                _dialogService.ShowWarning($"Could not create clone directory: {cloneDir}", "Clone Repository");
                return;
            }
        }

        IsLoading = true;
        StatusMessage = $"Cloning {SelectedRepository.FullName}...";

        try
        {
            var clonedPath = await _gitHubService.CloneRepositoryAsync(SelectedRepository.FullName, cloneDir);

            if (!string.IsNullOrEmpty(clonedPath))
            {
                _mainViewModel.OpenProjectTab(clonedPath);
                UpdateRecentPath(clonedPath);
                IsOpen = false;
            }
            else
            {
                IsOpen = false;
                _dialogService.ShowWarning($"Failed to clone {SelectedRepository.FullName}", "Clone Repository");
            }
        }
        finally
        {
            IsLoading = false;
            StatusMessage = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleFavorite))]
    private void ToggleFavorite()
    {
        if (SelectedRepository == null)
            return;

        var config = _configService.Load();
        var key = SelectedRepository.FullName;

        if (SelectedRepository.IsFavorite)
        {
            config.Settings.Repositories.Favorites.Remove(key);
            SelectedRepository.IsFavorite = false;
        }
        else
        {
            if (!config.Settings.Repositories.Favorites.Contains(key))
            {
                config.Settings.Repositories.Favorites.Add(key);
            }
            SelectedRepository.IsFavorite = true;
        }

        _configService.Save(config);

        // Re-sort the list
        ApplyFilter();
    }

    private bool CanToggleFavorite() => SelectedRepository != null;

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    private void UpdateRecentPath(string path)
    {
        var config = _configService.Load();
        var recentPaths = config.Settings.Repositories.RecentPaths;
        var maxItems = config.Settings.Repositories.MaxRecentItems;

        // Remove if exists, then add to front
        recentPaths.Remove(path);
        recentPaths.Insert(0, path);

        // Trim to max
        while (recentPaths.Count > maxItems)
        {
            recentPaths.RemoveAt(recentPaths.Count - 1);
        }

        _configService.Save(config);
    }

    private string? GetRepositoryFullName(string localPath)
    {
        // Try to get the GitHub remote URL to extract owner/repo
        try
        {
            var gitConfigPath = Path.Combine(localPath, ".git", "config");
            if (!_fileSystem.FileExists(gitConfigPath))
                return null;

            var content = _fileSystem.ReadAllText(gitConfigPath);

            // Look for GitHub remote URL
            // Pattern: url = https://github.com/owner/repo.git or git@github.com:owner/repo.git
            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("url = ", StringComparison.OrdinalIgnoreCase))
                    continue;

                var url = trimmed.Substring(6).Trim();

                // HTTPS format
                if (url.Contains("github.com/"))
                {
                    var parts = url.Split("github.com/", StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        var repoPath = parts[^1].TrimEnd('/').Replace(".git", "");
                        return repoPath;
                    }
                }

                // SSH format
                if (url.Contains("github.com:"))
                {
                    var parts = url.Split("github.com:", StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        var repoPath = parts[^1].TrimEnd('/').Replace(".git", "");
                        return repoPath;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors reading git config
        }

        return null;
    }
}
