using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ProfileRegistry _profileRegistry;
    private readonly SessionManager _sessionManager;
    private readonly TerminalControlFactory _terminalFactory;
    private readonly ConfigurationService _configService;
    private readonly StatisticsService _statisticsService;
    private readonly GitStatusService _gitStatusService;
    private readonly LinkDetectionService _linkDetectionService;
    private readonly ProjectDetectionService _projectDetectionService;
    private readonly RunUrlDetectionService _runUrlDetectionService;
    private readonly DispatcherTimer _gitStatusTimer;
    private readonly DispatcherTimer _activityTimer;
    private readonly DispatcherTimer _linkDetectionTimer;
    private readonly DispatcherTimer _runUrlDetectionTimer;

    /// <summary>
    /// The link detection service for scanning terminal output for clickable links.
    /// </summary>
    public LinkDetectionService LinkDetectionService => _linkDetectionService;

    /// <summary>
    /// The run URL detection service for detecting localhost URLs from run output.
    /// </summary>
    public RunUrlDetectionService RunUrlDetectionService => _runUrlDetectionService;

    /// <summary>
    /// The project detection service for auto-detecting project types.
    /// </summary>
    public ProjectDetectionService ProjectDetectionService => _projectDetectionService;

    /// <summary>
    /// The terminal control factory for creating terminal controls.
    /// </summary>
    public TerminalControlFactory TerminalFactory => _terminalFactory;

    /// <summary>
    /// The session manager for tracking terminal sessions.
    /// </summary>
    public SessionManager SessionManager => _sessionManager;

    [ObservableProperty]
    private ObservableCollection<ITabViewModel> _tabs = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private ITabViewModel? _selectedTab;

    [ObservableProperty]
    private ObservableCollection<QuickCommand> _quickCommands = new();

    public event EventHandler? ConfigReloaded;
    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
    public event EventHandler<RunTerminalRequestedEventArgs>? RunTerminalRequested;

    public string WindowTitle
    {
        get
        {
            if (SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                var gitBranch = terminalTab.GitStatus?.IsGitRepository == true
                    ? $" ({terminalTab.GitStatus.BranchName})"
                    : "";
                return $"{terminalTab.Title}{gitBranch} - TerminalHost";
            }
            else if (SelectedTab is SettingsTabViewModel)
            {
                return "Settings - TerminalHost";
            }
            return "TerminalHost";
        }
    }

    public MainViewModel(ProfileRegistry profileRegistry, SessionManager sessionManager, TerminalControlFactory terminalFactory, ConfigurationService configService, StatisticsService statisticsService)
    {
        _profileRegistry = profileRegistry;
        _sessionManager = sessionManager;
        _terminalFactory = terminalFactory;
        _configService = configService;
        _statisticsService = statisticsService;
        _gitStatusService = new GitStatusService();
        _linkDetectionService = new LinkDetectionService(profileRegistry);
        _projectDetectionService = new ProjectDetectionService(profileRegistry);
        _runUrlDetectionService = new RunUrlDetectionService();

        // Set up timer for periodic git status refresh (every 5 seconds)
        _gitStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _gitStatusTimer.Tick += async (_, _) => await RefreshSelectedTabGitStatusAsync();

        // Set up timer for activity state refresh (every 1 second to detect idle transitions)
        _activityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _activityTimer.Tick += (_, _) => RefreshActivityState();

        // Set up timer for link detection refresh (every 3 seconds)
        _linkDetectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _linkDetectionTimer.Tick += (_, _) => RefreshDetectedLinks();

        // Set up timer for run URL detection (every 2 seconds, only when running)
        _runUrlDetectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _runUrlDetectionTimer.Tick += (_, _) => RefreshRunUrlDetection();
    }

    public void Initialize()
    {
        // Load quick commands from config
        LoadQuickCommands();

        // Restore previously open folders
        RestoreOpenFolders();

        // Start git status refresh timer
        _gitStatusTimer.Start();

        // Start activity refresh timer
        _activityTimer.Start();

        // Start link detection timer
        _linkDetectionTimer.Start();

        // Start run URL detection timer
        _runUrlDetectionTimer.Start();
    }

    private void LoadQuickCommands()
    {
        var config = _configService.Load();
        QuickCommands = new ObservableCollection<QuickCommand>(config.QuickCommands);
    }

    private async Task RefreshSelectedTabGitStatusAsync()
    {
        if (SelectedTab is not TerminalPairTabViewModel terminalTab) return;

        try
        {
            var status = await _gitStatusService.GetGitStatusAsync(terminalTab.Pair.WorkingDirectory);
            terminalTab.GitStatus = status;
            // Update window title when git status changes
            OnPropertyChanged(nameof(WindowTitle));
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

    private void RefreshActivityState()
    {
        // Update activity state for all terminal tabs (to detect idle transitions)
        foreach (var tab in Tabs.OfType<TerminalPairTabViewModel>())
        {
            tab.UpdateActivityState();
        }
    }

    private void RefreshDetectedLinks()
    {
        // Only refresh the selected tab to keep it lightweight
        if (SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            terminalTab.UpdateDetectedLinks(_linkDetectionService);
        }
    }

    private void RefreshRunUrlDetection()
    {
        // Only scan when there's a running project
        if (SelectedTab is not TerminalPairTabViewModel terminalTab)
            return;

        if (terminalTab.RunState != RunState.Running && terminalTab.RunState != RunState.Starting)
            return;

        if (terminalTab.Pair.RunTerminal == null)
            return;

        // Don't re-detect if we already have a URL
        if (!string.IsNullOrEmpty(terminalTab.DetectedRunUrl))
            return;

        // Get recent output from run terminal
        var output = terminalTab.Pair.RunTerminal.GetRecentOutput(5000);
        if (string.IsNullOrEmpty(output))
            return;

        // Get the URL pattern from the active configuration
        var urlPattern = terminalTab.ActiveRunConfiguration?.UrlPattern;

        // Detect URL
        var url = _runUrlDetectionService.DetectUrl(output, urlPattern);
        if (!string.IsNullOrEmpty(url))
        {
            terminalTab.DetectedRunUrl = url;
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
        config.OpenFolders = Tabs.OfType<TerminalPairTabViewModel>().Select(t => t.Pair.WorkingDirectory).ToList();
        _configService.Save(config);
    }

    private void SaveDirectorySettings(TerminalPairTabViewModel tab)
    {
        var config = _configService.Load();
        var normalizedPath = NormalizePath(tab.Pair.WorkingDirectory);

        // Get existing settings or create new
        if (!config.DirectorySettings.TryGetValue(normalizedPath, out var settings))
        {
            settings = new DirectorySettings();
        }

        // Update basic settings
        settings.IsSplitView = tab.IsSplitView;
        settings.SplitRatio = tab.SplitRatio;
        settings.ActiveTerminal = tab.ActiveTerminal.ToString();

        // Update run settings
        settings.IsRunTerminalVisible = tab.IsRunTerminalVisible;
        settings.RunSplitRatio = tab.RunSplitRatio;
        settings.ActiveRunConfigurationId = tab.ActiveRunConfiguration?.Id;
        settings.RunConfigurations = tab.RunConfigurations.ToList();

        config.DirectorySettings[normalizedPath] = settings;
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

            if (result == DialogResult.OK)
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
            var existingTab = Tabs.OfType<TerminalPairTabViewModel>().FirstOrDefault(t =>
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
            var pair = new TerminalPair(workingDirectory, customProfile, shellProfile, _statisticsService);

            // Create terminal controls for both
            var customControl = _terminalFactory.CreateTerminalControl(pair.CustomTerminal);
            var shellControl = _terminalFactory.CreateTerminalControl(pair.ShellTerminal);

            // Create view model
            var tabViewModel = new TerminalPairTabViewModel(pair, settings.CustomCommandIcon, settings.ShellCommandIcon, _statisticsService);
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

                // Restore run settings
                tabViewModel.IsRunTerminalVisible = dirSettings.IsRunTerminalVisible;
                tabViewModel.RunSplitRatio = dirSettings.RunSplitRatio;
            }

            // Initialize run configurations (from settings or auto-detect)
            InitializeRunConfigurations(tabViewModel, workingDirectory, dirSettings);

            // Track sessions
            _sessionManager.TrackSession(pair.CustomTerminal);
            _sessionManager.TrackSession(pair.ShellTerminal);

            // Subscribe to link click events
            pair.CustomTerminal.LinkClicked += (s, text) => HandleLinkClick(text, workingDirectory);
            pair.ShellTerminal.LinkClicked += (s, text) => HandleLinkClick(text, workingDirectory);

            // Subscribe to run terminal events
            tabViewModel.RunStartRequested += OnRunStartRequested;
            tabViewModel.RunStopRequested += OnRunStopRequested;

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
    private void CloseTab(ITabViewModel? tab)
    {
        if (tab == null) return;

        if (tab is TerminalPairTabViewModel terminalTab)
        {
            var hasRunning = terminalTab.Pair.CustomTerminal.IsProcessRunning() || terminalTab.Pair.ShellTerminal.IsProcessRunning();

            if (hasRunning && _profileRegistry.Settings.ConfirmOnClose)
            {
                var result = MessageBox.Show(
                    $"Terminals in '{terminalTab.Title}' are still running. Close anyway?",
                    "Confirm Close",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;
            }

            terminalTab.CloseRequested -= OnTabCloseRequested;
            terminalTab.SettingsChanged -= OnTabSettingsChanged;
            terminalTab.RunStartRequested -= OnRunStartRequested;
            terminalTab.RunStopRequested -= OnRunStopRequested;
            _sessionManager.CloseSession(terminalTab.Pair.CustomTerminal);
            _sessionManager.CloseSession(terminalTab.Pair.ShellTerminal);
            if (terminalTab.Pair.RunTerminal != null)
            {
                _sessionManager.CloseSession(terminalTab.Pair.RunTerminal);
            }
            terminalTab.Pair.Dispose();
        }
        else if (tab is SettingsTabViewModel settingsTab)
        {
            settingsTab.CloseRequested -= OnTabCloseRequested;
            settingsTab.ConfigSaved -= OnConfigSaved;
        }
        else if (tab is ProfilesTabViewModel profilesTab)
        {
            profilesTab.CloseRequested -= OnTabCloseRequested;
        }
        else if (tab is StatisticsTabViewModel statsTab)
        {
            statsTab.CloseRequested -= OnTabCloseRequested;
        }

        Tabs.Remove(tab);

        if (SelectedTab == tab && Tabs.Count > 0)
        {
            SelectedTab = Tabs[^1];
        }
    }

    [RelayCommand]
    private void SwitchActiveTerminal()
    {
        if (SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            terminalTab.SwitchTerminalCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void ExecuteQuickCommand(QuickCommand? command)
    {
        if (command == null || SelectedTab is not TerminalPairTabViewModel terminalTab) return;

        // Switch to the target terminal
        if (command.Target == QuickCommandTarget.Custom)
        {
            terminalTab.ShowCustomTerminalCommand.Execute(null);
        }
        else
        {
            terminalTab.ShowShellTerminalCommand.Execute(null);
        }

        var targetSession = command.Target == QuickCommandTarget.Custom
            ? terminalTab.Pair.CustomTerminal
            : terminalTab.Pair.ShellTerminal;

        targetSession.SendText(command.Text, command.AppendNewline, command.NewlineChar, command.UseUserInput);

        // Focus the terminal
        targetSession.Focus();
    }

    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is ITabViewModel tab)
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

    private void OnRunStartRequested(object? sender, Domain.RunConfiguration configuration)
    {
        if (sender is TerminalPairTabViewModel tab)
        {
            RunTerminalRequested?.Invoke(this, new RunTerminalRequestedEventArgs
            {
                Tab = tab,
                Configuration = configuration,
                IsStop = false
            });
        }
    }

    private void OnRunStopRequested(object? sender, EventArgs e)
    {
        if (sender is TerminalPairTabViewModel tab && tab.ActiveRunConfiguration != null)
        {
            RunTerminalRequested?.Invoke(this, new RunTerminalRequestedEventArgs
            {
                Tab = tab,
                Configuration = tab.ActiveRunConfiguration,
                IsStop = true
            });
        }
    }

    private void InitializeRunConfigurations(TerminalPairTabViewModel tab, string workingDirectory, DirectorySettings? dirSettings)
    {
        List<Domain.RunConfiguration> configs;

        if (dirSettings != null && dirSettings.RunConfigurations.Count > 0)
        {
            // Use saved configurations
            configs = dirSettings.RunConfigurations;
        }
        else
        {
            // Auto-detect project type and create configurations
            configs = _projectDetectionService.GetOrCreateConfigurations(
                workingDirectory,
                dirSettings ?? new DirectorySettings());
        }

        tab.InitializeRunConfigurations(configs, dirSettings?.ActiveRunConfigurationId);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        // Check if settings tab already exists
        var existingSettings = Tabs.OfType<SettingsTabViewModel>().FirstOrDefault();
        if (existingSettings != null)
        {
            SelectedTab = existingSettings;
            return;
        }

        // Create new settings tab
        var settingsTab = new SettingsTabViewModel(_configService);
        settingsTab.CloseRequested += OnTabCloseRequested;
        settingsTab.ConfigSaved += OnConfigSaved;
        Tabs.Add(settingsTab);
        SelectedTab = settingsTab;
    }

    [RelayCommand]
    private void OpenProfiles()
    {
        // Check if profiles tab already exists
        var existingProfiles = Tabs.OfType<ProfilesTabViewModel>().FirstOrDefault();
        if (existingProfiles != null)
        {
            SelectedTab = existingProfiles;
            return;
        }

        // Create new profiles tab
        var profilesTab = new ProfilesTabViewModel(_profileRegistry);
        profilesTab.CloseRequested += OnTabCloseRequested;
        Tabs.Add(profilesTab);
        SelectedTab = profilesTab;
    }

    [RelayCommand]
    private void OpenStatistics()
    {
        try
        {
            // Check if statistics tab already exists
            var existingStats = Tabs.OfType<StatisticsTabViewModel>().FirstOrDefault();
            if (existingStats != null)
            {
                SelectedTab = existingStats;
                // Also refresh the stats when focusing the existing tab
                existingStats.LoadStatsCommand.Execute(null);
                return;
            }

            // Create new statistics tab
            var statsTab = new StatisticsTabViewModel(_statisticsService);
            statsTab.CloseRequested += OnTabCloseRequested;
            Tabs.Add(statsTab);
            SelectedTab = statsTab;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"An error occurred while opening the statistics view:\n\n{ex}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnConfigSaved(object? sender, EventArgs e)
    {
        // Reload quick commands when config is saved
        LoadQuickCommands();

        // Notify that config has been reloaded (for system tray, etc.)
        ConfigReloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Handles Ctrl+Click link detection from terminals.
    /// </summary>
    private void HandleLinkClick(string recentOutput, string workingDirectory)
    {
        if (string.IsNullOrEmpty(recentOutput)) return;

        // Try to find a link in the recent output
        // We scan the output looking for URL patterns, file paths, or custom patterns
        var lines = recentOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Start from the end (most recent) and work backwards
        foreach (var line in lines.Reverse())
        {
            var cleanLine = line.Trim();
            if (string.IsNullOrEmpty(cleanLine)) continue;

            // Try each "word" in the line
            var words = cleanLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                var link = _linkDetectionService.DetectLink(word, workingDirectory);
                if (link != null)
                {
                    HandleDetectedLink(link);
                    return;
                }
            }

            // Also try the whole line in case it's a file path with spaces
            var linkFromLine = _linkDetectionService.DetectLink(cleanLine, workingDirectory);
            if (linkFromLine != null)
            {
                HandleDetectedLink(linkFromLine);
                return;
            }
        }

        // No link found - could show a tooltip or status message
        Console.WriteLine("[MainViewModel] No clickable link found in recent output");
    }

    private void HandleDetectedLink(string link)
    {
        // Check if it's a file path that we should show in preview
        if (LinkDetectionService.IsFilePath(link))
        {
            // Parse for line/column numbers
            var (path, line, column) = FilePreviewService.ParseFilePathWithPosition(link);

            // Fire event for MainWindow to show preview
            FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs
            {
                FilePath = path,
                Line = line,
                Column = column
            });
        }
        else
        {
            // It's a URL or something else - open normally
            _linkDetectionService.OpenLink(link);
        }
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (SelectedTab is not TerminalPairTabViewModel terminalTab) return;

        var folder = terminalTab.Pair.WorkingDirectory;
        if (Directory.Exists(folder))
        {
            Process.Start("explorer.exe", folder);
        }
    }

    public event EventHandler? HelpRequested;
    public event EventHandler? ScratchPadRequested;
    public event EventHandler? GitChangesRequested;

    [RelayCommand]
    private void OpenHelp()
    {
        HelpRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenScratchPad()
    {
        ScratchPadRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenGitChanges()
    {
        GitChangesRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Shutdown()
    {
        // Stop timers
        _gitStatusTimer.Stop();
        _activityTimer.Stop();
        _linkDetectionTimer.Stop();
        _runUrlDetectionTimer.Stop();

        // Save open folders before closing
        SaveOpenFolders();

        _sessionManager.CloseAllSessions();
        foreach (var tab in Tabs.OfType<TerminalPairTabViewModel>())
        {
            tab.Pair.Dispose();
        }
    }
}

public class FilePreviewRequestedEventArgs : EventArgs
{
    public required string FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
}

public class RunTerminalRequestedEventArgs : EventArgs
{
    public required TerminalPairTabViewModel Tab { get; init; }
    public required Domain.RunConfiguration Configuration { get; init; }
    public bool IsStop { get; init; }
}
