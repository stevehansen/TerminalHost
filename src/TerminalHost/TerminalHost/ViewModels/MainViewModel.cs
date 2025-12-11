using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Services;
using MessageBox = System.Windows.MessageBox;

namespace TerminalHost.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ProfileRegistry _profileRegistry;
    private readonly SessionManager _sessionManager;
    private readonly TerminalControlFactory _terminalFactory;
    private readonly ConfigurationService _configService;
    private readonly GitStatusService _gitStatusService;
    private readonly DispatcherTimer _gitStatusTimer;

    [ObservableProperty]
    private ObservableCollection<TerminalPairTabViewModel> _tabs = new();

    [ObservableProperty]
    private TerminalPairTabViewModel? _selectedTab;

    public MainViewModel(ProfileRegistry profileRegistry, SessionManager sessionManager, TerminalControlFactory terminalFactory, ConfigurationService configService)
    {
        _profileRegistry = profileRegistry;
        _sessionManager = sessionManager;
        _terminalFactory = terminalFactory;
        _configService = configService;
        _gitStatusService = new GitStatusService();

        // Set up timer for periodic git status refresh (every 5 seconds)
        _gitStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _gitStatusTimer.Tick += async (_, _) => await RefreshSelectedTabGitStatusAsync();
    }

    public void Initialize()
    {
        // Restore previously open folders
        RestoreOpenFolders();

        // Start git status refresh timer
        _gitStatusTimer.Start();
    }

    private async Task RefreshSelectedTabGitStatusAsync()
    {
        if (SelectedTab == null) return;

        try
        {
            var status = await _gitStatusService.GetGitStatusAsync(SelectedTab.Pair.WorkingDirectory);
            SelectedTab.GitStatus = status;
        }
        catch
        {
            // Silently ignore git status errors
        }
    }

    private async Task RefreshTabGitStatusAsync(TerminalPairTabViewModel tab)
    {
        try
        {
            var status = await _gitStatusService.GetGitStatusAsync(tab.Pair.WorkingDirectory);
            tab.GitStatus = status;
        }
        catch
        {
            // Silently ignore git status errors
        }
    }

    private void RestoreOpenFolders()
    {
        var config = _configService.Load();
        foreach (var folder in config.OpenFolders)
        {
            if (Directory.Exists(folder))
            {
                OpenProjectTab(folder);
            }
        }
    }

    private void SaveOpenFolders()
    {
        var config = _configService.Load();
        config.OpenFolders = Tabs.Select(t => t.Pair.WorkingDirectory).ToList();
        _configService.Save(config);
    }

    private void SaveDirectorySettings(TerminalPairTabViewModel tab)
    {
        var config = _configService.Load();
        var normalizedPath = NormalizePath(tab.Pair.WorkingDirectory);

        config.DirectorySettings[normalizedPath] = new DirectorySettings
        {
            IsSplitView = tab.IsSplitView,
            SplitRatio = tab.SplitRatio,
            ActiveTerminal = tab.ActiveTerminal.ToString()
        };

        _configService.Save(config);
    }

    private DirectorySettings? GetDirectorySettings(string workingDirectory)
    {
        var config = _configService.Load();
        var normalizedPath = NormalizePath(workingDirectory);

        return config.DirectorySettings.TryGetValue(normalizedPath, out var settings) ? settings : null;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
    }

    [RelayCommand]
    private void OpenNewProject()
    {
        try
        {
            // Use folder browser dialog
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Project Directory",
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };

            var result = dialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK)
            {
                OpenProjectTab(dialog.SelectedPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainViewModel] Error opening project: {ex.Message}");
            MessageBox.Show($"Error opening project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void OpenProjectTab(string workingDirectory)
    {
        try
        {
            // Normalize the path for comparison
            workingDirectory = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!Directory.Exists(workingDirectory))
            {
                MessageBox.Show($"Directory not found: {workingDirectory}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Check if we already have a tab open for this directory
            var existingTab = Tabs.FirstOrDefault(t =>
                string.Equals(
                    Path.GetFullPath(t.Pair.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    workingDirectory,
                    StringComparison.OrdinalIgnoreCase));

            if (existingTab != null)
            {
                // Focus the existing tab instead of creating a new one
                SelectedTab = existingTab;
                return;
            }

            var settings = _profileRegistry.Settings;

            // Create profiles for custom command and shell
            var customProfile = new Profile
            {
                Id = "custom",
                Name = settings.CustomCommandName,
                Command = settings.CustomCommand,
                WorkingDir = workingDirectory,
                Icon = settings.CustomCommandIcon
            };

            var shellProfile = new Profile
            {
                Id = "shell",
                Name = settings.ShellCommandName,
                Command = settings.ShellCommand,
                WorkingDir = workingDirectory,
                Icon = settings.ShellCommandIcon
            };

            // Create the terminal pair
            var pair = new TerminalPair(workingDirectory, customProfile, shellProfile);

            // Create terminal controls for both
            var customControl = _terminalFactory.CreateTerminalControl(pair.CustomTerminal);
            var shellControl = _terminalFactory.CreateTerminalControl(pair.ShellTerminal);

            // Create view model
            var tabViewModel = new TerminalPairTabViewModel(pair, settings.CustomCommandIcon, settings.ShellCommandIcon);
            tabViewModel.SetTerminalControls(customControl, shellControl);
            tabViewModel.CloseRequested += OnTabCloseRequested;
            tabViewModel.SettingsChanged += OnTabSettingsChanged;

            // Restore per-directory settings if available
            var dirSettings = GetDirectorySettings(workingDirectory);
            if (dirSettings != null)
            {
                tabViewModel.IsSplitView = dirSettings.IsSplitView;
                tabViewModel.SplitRatio = dirSettings.SplitRatio;
                if (Enum.TryParse<ActiveTerminal>(dirSettings.ActiveTerminal, out var activeTerminal))
                {
                    tabViewModel.ActiveTerminal = activeTerminal;
                    pair.ActiveTerminal = activeTerminal;
                }
            }

            // Track sessions
            _sessionManager.TrackSession(pair.CustomTerminal);
            _sessionManager.TrackSession(pair.ShellTerminal);

            Tabs.Add(tabViewModel);
            SelectedTab = tabViewModel;

            // Fetch git status for the new tab
            _ = RefreshTabGitStatusAsync(tabViewModel);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainViewModel] Error creating terminal: {ex.Message}");
            MessageBox.Show($"Error creating terminal: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CloseTab(TerminalPairTabViewModel? tab)
    {
        if (tab == null) return;

        var hasRunning = tab.Pair.CustomTerminal.IsProcessRunning() || tab.Pair.ShellTerminal.IsProcessRunning();

        if (hasRunning && _profileRegistry.Settings.ConfirmOnClose)
        {
            var result = MessageBox.Show(
                $"Terminals in '{tab.Title}' are still running. Close anyway?",
                "Confirm Close",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;
        }

        tab.CloseRequested -= OnTabCloseRequested;
        tab.SettingsChanged -= OnTabSettingsChanged;
        _sessionManager.CloseSession(tab.Pair.CustomTerminal);
        _sessionManager.CloseSession(tab.Pair.ShellTerminal);
        tab.Pair.Dispose();
        Tabs.Remove(tab);

        if (SelectedTab == tab && Tabs.Count > 0)
        {
            SelectedTab = Tabs[^1];
        }
    }

    [RelayCommand]
    private void SwitchActiveTerminal()
    {
        SelectedTab?.SwitchTerminalCommand.Execute(null);
    }

    [RelayCommand]
    private void ToggleSplitView()
    {
        SelectedTab?.ToggleSplitViewCommand.Execute(null);
    }

    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is TerminalPairTabViewModel tab)
        {
            CloseTab(tab);
        }
    }

    private void OnTabSettingsChanged(object? sender, EventArgs e)
    {
        if (sender is TerminalPairTabViewModel tab)
        {
            SaveDirectorySettings(tab);
        }
    }

    public void Shutdown()
    {
        // Stop git status refresh
        _gitStatusTimer.Stop();

        // Save open folders before closing
        SaveOpenFolders();

        _sessionManager.CloseAllSessions();
        foreach (var tab in Tabs)
        {
            tab.Pair.Dispose();
        }
    }
}
