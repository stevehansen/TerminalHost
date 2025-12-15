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
    private readonly IProfileRegistry _profileRegistry;
    private readonly ISessionManager _sessionManager;
    private readonly ITerminalControlFactory _terminalFactory;
    private readonly IConfigurationService _configService;
    private readonly IStatisticsService _statisticsService;
    private readonly IGitStatusService _gitStatusService;
    private readonly ILinkDetectionService _linkDetectionService;
    private readonly IProjectDetectionService _projectDetectionService;
    private readonly IRunUrlDetectionService _runUrlDetectionService;
    private readonly DetectedLinksViewModel _detectedLinksViewModel;
    private readonly DispatcherTimer _gitStatusTimer;
    private readonly DispatcherTimer _activityTimer;
    private readonly DispatcherTimer _linkDetectionTimer;
    private readonly DispatcherTimer _runUrlDetectionTimer;

    /// <summary>
    /// The link detection service for scanning terminal output for clickable links.
    /// </summary>
    public ILinkDetectionService LinkDetectionService => _linkDetectionService;

    /// <summary>
    /// The run URL detection service for detecting localhost URLs from run output.
    /// </summary>
    public IRunUrlDetectionService RunUrlDetectionService => _runUrlDetectionService;

    /// <summary>
    /// The project detection service for auto-detecting project types.
    /// </summary>
    public IProjectDetectionService ProjectDetectionService => _projectDetectionService;

    /// <summary>
    /// The terminal control factory for creating terminal controls.
    /// </summary>
    public ITerminalControlFactory TerminalFactory => _terminalFactory;

    /// <summary>
    /// The session manager for tracking terminal sessions.
    /// </summary>
    public ISessionManager SessionManager => _sessionManager;

    [ObservableProperty]
    private ObservableCollection<ITabViewModel> _tabs = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private ITabViewModel? _selectedTab;

    [ObservableProperty]
    private ObservableCollection<QuickCommand> _quickCommands = new();

    [ObservableProperty]
    private string _dropdownSearchText = "";

    private ObservableCollection<ITabViewModel> _filteredDropdownTabs = new();
    public ReadOnlyObservableCollection<ITabViewModel> FilteredDropdownTabs { get; }

    [ObservableProperty]
    private bool _isTabDropdownOpen;

    [ObservableProperty]
    private string _switcherSearchText = "";

    private ObservableCollection<ITabViewModel> _filteredSwitcherTabs = new();
    public ReadOnlyObservableCollection<ITabViewModel> FilteredSwitcherTabs { get; }

    [ObservableProperty]
    private bool _isTabSwitcherOpen;

    [ObservableProperty]
    private bool _isHelpOpen;

    // Command Palette Properties
    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    [ObservableProperty]
    private string _paletteSearchText = "";

    private ObservableCollection<PaletteCommand> _allPaletteCommands = new(); // Stores all commands
    private ObservableCollection<PaletteCommand> _filteredPaletteCommands = new();
    public ReadOnlyObservableCollection<PaletteCommand> FilteredPaletteCommands { get; }

    [ObservableProperty]
    private PaletteCommand? _selectedPaletteCommand;

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

    public MainViewModel(
        IProfileRegistry profileRegistry, 
        ISessionManager sessionManager, 
        ITerminalControlFactory terminalFactory, 
        IConfigurationService configService, 
        IStatisticsService statisticsService,
        IGitStatusService gitStatusService,
        ILinkDetectionService linkDetectionService,
        IProjectDetectionService projectDetectionService,
        IRunUrlDetectionService runUrlDetectionService,
        DetectedLinksViewModel detectedLinksViewModel)
    {
        _profileRegistry = profileRegistry;
        _sessionManager = sessionManager;
        _terminalFactory = terminalFactory;
        _configService = configService;
        _statisticsService = statisticsService;
        _gitStatusService = gitStatusService;
        _linkDetectionService = linkDetectionService;
        _projectDetectionService = projectDetectionService;
        _runUrlDetectionService = runUrlDetectionService;
        _detectedLinksViewModel = detectedLinksViewModel;

        FilteredDropdownTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filteredDropdownTabs);
        UpdateFilteredDropdownTabs(); // Initial population

        FilteredSwitcherTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filteredSwitcherTabs);
        UpdateFilteredSwitcherTabs(); // Initial population

        FilteredPaletteCommands = new ReadOnlyObservableCollection<PaletteCommand>(_filteredPaletteCommands);
        InitializeCommandPalette(); // Initialize commands once

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

    partial void OnDropdownSearchTextChanged(string value)
    {
        UpdateFilteredDropdownTabs();
    }

    partial void OnSwitcherSearchTextChanged(string value)
    {
        UpdateFilteredSwitcherTabs();
    }

    partial void OnTabsChanged(ObservableCollection<ITabViewModel> value)
    {
        UpdateFilteredDropdownTabs();
        UpdateFilteredSwitcherTabs();
    }

    partial void OnSelectedTabChanged(ITabViewModel? value)
    {
        // If the selected tab changes, and the dropdown is open, close it.
        if (IsTabDropdownOpen && value != null)
        {
            IsTabDropdownOpen = false;
        }
        if (IsTabSwitcherOpen && value != null)
        {
            IsTabSwitcherOpen = false;
        }
    }

    partial void OnIsTabDropdownOpenChanged(bool value)
    {
        if (value)
        {
            DropdownSearchText = "";
            UpdateFilteredDropdownTabs();
        }
    }

    partial void OnIsTabSwitcherOpenChanged(bool value)
    {
        if (value)
        {
            SwitcherSearchText = "";
            UpdateFilteredSwitcherTabs();
        }
    }

    private void UpdateFilteredDropdownTabs()
    {
        _filteredDropdownTabs.Clear();
        if (string.IsNullOrEmpty(DropdownSearchText))
        {
            foreach (var tab in Tabs)
            {
                _filteredDropdownTabs.Add(tab);
            }
        }
        else
        {
            var searchText = DropdownSearchText.ToLower();
            foreach (var tab in Tabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText)))
            {
                _filteredDropdownTabs.Add(tab);
            }
        }
    }

    private void UpdateFilteredSwitcherTabs()
    {
        _filteredSwitcherTabs.Clear();
        if (string.IsNullOrEmpty(SwitcherSearchText))
        {
            foreach (var tab in Tabs)
            {
                _filteredSwitcherTabs.Add(tab);
            }
        }
        else
        {
            var searchText = SwitcherSearchText.ToLower();
            foreach (var tab in Tabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText)))
            {
                _filteredSwitcherTabs.Add(tab);
            }
        }
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

        // Also update profile terminal tabs
        foreach (var tab in Tabs.OfType<ProfileTerminalTabViewModel>())
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

            if (result == System.Windows.Forms.DialogResult.OK)
            {
                OpenProjectTab(dialog.SelectedPath);
            }
        }
        catch (Exception ex)
        {
            DialogService.ShowError($"Error opening project: {ex.Message}");
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
                DialogService.ShowError($"Directory not found: {workingDirectory}");
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
            DialogService.ShowError($"Error creating terminal: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens a new tab with a single terminal running the specified profile.
    /// </summary>
    /// <param name="profile">The profile to launch.</param>
    /// <param name="workingDirectory">Optional working directory. If null, uses the profile's WorkingDir.</param>
    public void OpenProfileTab(Profile profile, string? workingDirectory = null)
    {
        try
        {
            // Determine working directory
            var effectiveWorkingDir = workingDirectory;
            if (string.IsNullOrWhiteSpace(effectiveWorkingDir))
            {
                effectiveWorkingDir = profile.GetExpandedWorkingDir();
            }

            // If still empty, use user profile directory
            if (string.IsNullOrWhiteSpace(effectiveWorkingDir))
            {
                effectiveWorkingDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            // Normalize path
            effectiveWorkingDir = Path.GetFullPath(effectiveWorkingDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!Directory.Exists(effectiveWorkingDir))
            {
                DialogService.ShowError($"Directory not found: {effectiveWorkingDir}");
                return;
            }

            // Clone the profile with the working directory set
            var profileWithDir = new Profile
            {
                Id = profile.Id,
                Name = profile.Name,
                Command = profile.Command,
                WorkingDir = effectiveWorkingDir,
                Icon = profile.Icon,
                Shortcut = profile.Shortcut,
                AutoStart = profile.AutoStart
            };

            // Create view model
            var tabViewModel = new ProfileTerminalTabViewModel(profileWithDir, effectiveWorkingDir, _statisticsService);

            // Create terminal control
            var terminalControl = _terminalFactory.CreateTerminalControl(tabViewModel.Session);
            tabViewModel.SetTerminalControl(terminalControl);

            // Subscribe to events
            tabViewModel.CloseRequested += OnTabCloseRequested;

            // Track session
            _sessionManager.TrackSession(tabViewModel.Session);

            // Add tab and select it
            Tabs.Add(tabViewModel);
            SelectedTab = tabViewModel;
        }
        catch (Exception ex)
        {
            DialogService.ShowError($"Error launching profile: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens a profile tab with a folder picker to select the working directory.
    /// </summary>
    /// <param name="profile">The profile to launch.</param>
    public void OpenProfileTabWithPicker(Profile profile)
    {
        try
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = $"Select Working Directory for {profile.Name}",
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };

            // Set initial directory to profile's configured directory if it exists
            var initialDir = profile.GetExpandedWorkingDir();
            if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
            {
                dialog.InitialDirectory = initialDir;
            }

            var result = dialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK)
            {
                OpenProfileTab(profile, dialog.SelectedPath);
            }
        }
        catch (Exception ex)
        {
            DialogService.ShowError($"Error opening folder picker: {ex.Message}");
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
                if (!DialogService.ShowConfirmation(
                    $"Terminals in '{terminalTab.Title}' are still running. Close anyway?",
                    "Confirm Close"))
                    return;
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
            profilesTab.ProfileLaunchRequested -= OnProfileLaunchRequested;
        }
        else if (tab is StatisticsTabViewModel statsTab)
        {
            statsTab.CloseRequested -= OnTabCloseRequested;
        }
        else if (tab is ProfileTerminalTabViewModel profileTab)
        {
            var hasRunning = profileTab.Session.IsProcessRunning();

            if (hasRunning && _profileRegistry.Settings.ConfirmOnClose)
            {
                if (!DialogService.ShowConfirmation(
                    $"Terminal '{profileTab.Title}' is still running. Close anyway?",
                    "Confirm Close"))
                    return;
            }

            profileTab.CloseRequested -= OnTabCloseRequested;
            _sessionManager.CloseSession(profileTab.Session);
            profileTab.Session.Dispose();
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
        profilesTab.ProfileLaunchRequested += OnProfileLaunchRequested;
        Tabs.Add(profilesTab);
        SelectedTab = profilesTab;
    }

    private void OnProfileLaunchRequested(object? sender, ProfileLaunchEventArgs e)
    {
        if (e.PickFolder)
        {
            OpenProfileTabWithPicker(e.Profile);
        }
        else
        {
            OpenProfileTab(e.Profile);
        }
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
            DialogService.ShowError($"An error occurred while opening the statistics view:\n\n{ex.Message}");
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

    [RelayCommand]
    private void CycleTab(bool forward)
    {
        if (Tabs.Count <= 1) return;

        var currentIndex = SelectedTab != null
            ? Tabs.IndexOf(SelectedTab)
            : 0;

        int newIndex;
        if (forward)
        {
            newIndex = (currentIndex + 1) % Tabs.Count;
        }
        else
        {
            newIndex = (currentIndex - 1 + Tabs.Count) % Tabs.Count;
        }

        SelectedTab = Tabs[newIndex];
    }

    public event EventHandler? ScratchPadRequested;
    public event EventHandler? GitChangesRequested;
    public event EventHandler? SetupRequested;

    [RelayCommand]
    private void OpenSetup()
    {
        SetupRequested?.Invoke(this, EventArgs.Empty);
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

    [RelayCommand]
    private void OpenHelp()
    {
        IsHelpOpen = true;
    }

    [RelayCommand]
    private void OpenTabDropdown()
    {
        IsTabDropdownOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanOpenDetectedLinks))]
    private async Task OpenDetectedLinks()
    {
        if (SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            await _detectedLinksViewModel.OpenAsync(terminalTab);
        }
    }

    private bool CanOpenDetectedLinks() => SelectedTab is TerminalPairTabViewModel;

    [RelayCommand]
    private void CloseHelp()
    {
        IsHelpOpen = false;
    }

    partial void OnPaletteSearchTextChanged(string value)
    {
        FilterPaletteCommands();
    }

    partial void OnIsCommandPaletteOpenChanged(bool value)
    {
        if (value)
        {
            PaletteSearchText = "";
            FilterPaletteCommands();
            if (FilteredPaletteCommands.Any())
            {
                SelectedPaletteCommand = FilteredPaletteCommands.First();
            }
        }
    }

    private void InitializeCommandPalette()
    {
        _allPaletteCommands = new ObservableCollection<PaletteCommand>
        {
            // Tab/Project commands
            new PaletteCommand
            {
                Id = "new-project",
                Name = "New Project",
                Description = "Open folder as new project",
                Shortcut = "Ctrl+N",
                Icon = "📁",
                Category = "Project",
                Execute = () => OpenNewProjectCommand.Execute(null)
            },
            new PaletteCommand
            {
                Id = "close-tab",
                Name = "Close Tab",
                Description = "Close current tab",
                Shortcut = "Ctrl+W",
                Icon = "✕",
                Category = "Tab",
                Execute = () => { if (SelectedTab != null) CloseTabCommand.Execute(SelectedTab); }
            },
            new PaletteCommand
            {
                Id = "tab-switcher",
                Name = "Switch Tab",
                Description = "Search and switch tabs",
                Shortcut = "Ctrl+Shift+T",
                Icon = "🔍",
                Category = "Tab",
                Execute = () => { IsTabSwitcherOpen = true; SwitcherSearchText = ""; }
            },

            // File commands
            new PaletteCommand
            {
                Id = "file-preview",
                Name = "Preview File",
                Description = "Open file preview",
                Shortcut = "Ctrl+O",
                Icon = "👁",
                Category = "File",
                Execute = () => FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs { FilePath = "", Line = 0, Column = 0}) // Needs to be improved
            },
            new PaletteCommand
            {
                Id = "file-edit",
                Name = "Edit File",
                Description = "Open file in editor",
                Shortcut = "Ctrl+Shift+E",
                Icon = "✏️",
                Category = "File",
                Execute = () => { /* Needs to be improved */ }
            },
            new PaletteCommand
            {
                Id = "open-explorer",
                Name = "Open in Explorer",
                Description = "Open folder in file explorer",
                Shortcut = "Ctrl+E",
                Icon = "📂",
                Category = "File",
                Execute = () => OpenInExplorerCommand.Execute(null),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Terminal commands
            new PaletteCommand
            {
                Id = "switch-terminal",
                Name = "Switch Terminal",
                Description = "Toggle between custom and shell",
                Shortcut = "Ctrl+`",
                Icon = "⇄",
                Category = "Terminal",
                Execute = () => SwitchActiveTerminalCommand.Execute(null),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Settings
            new PaletteCommand
            {
                Id = "settings",
                Name = "Settings",
                Description = "Open settings editor",
                Shortcut = "Ctrl+,",
                Icon = "⚙️",
                Category = "Settings",
                Execute = () => OpenSettingsCommand.Execute(null)
            },
            new PaletteCommand
            {
                Id = "profiles",
                Name = "Profiles",
                Description = "Manage terminal profiles",
                Shortcut = "Ctrl+P",
                Icon = "👤",
                Category = "Settings",
                Execute = () => OpenProfilesCommand.Execute(null)
            },
            new PaletteCommand
            {
                Id = "setup",
                Name = "Setup",
                Description = "Check dependencies and setup",
                Icon = "🔧",
                Category = "Settings",
                Execute = () => OpenSetupCommand.Execute(null)
            },

            // Help
            new PaletteCommand
            {
                Id = "help",
                Name = "Help",
                Description = "Show keyboard shortcuts",
                Shortcut = "F1",
                Icon = "❓",
                Category = "Help",
                Execute = () => IsHelpOpen = true
            },

            // Scratch Pad
            new PaletteCommand
            {
                Id = "scratch-pad",
                Name = "Scratch Pad",
                Description = "Open notes panel",
                Shortcut = "Ctrl+Shift+N",
                Icon = "📝",
                Category = "Tools",
                Execute = () => OpenScratchPadCommand.Execute(null)
            },

            // Statistics
            new PaletteCommand
            {
                Id = "statistics",
                Name = "Statistics",
                Description = "View usage statistics",
                Icon = "📊",
                Category = "Tools",
                Execute = () => OpenStatisticsCommand.Execute(null)
            },

            // Git
            new PaletteCommand
            {
                Id = "git-changes",
                Name = "Git Changes",
                Description = "View modified files and diffs",
                Shortcut = "Ctrl+G",
                Icon = "📋",
                Category = "Git",
                Execute = () => GitChangesRequested?.Invoke(this, EventArgs.Empty), // Needs to be improved
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new PaletteCommand
            {
                Id = "git-branches",
                Name = "Git Branches",
                Description = "Switch, create, or delete branches",
                Shortcut = "Ctrl+B",
                Icon = "🌿",
                Category = "Git",
                Execute = () => { /* Needs to be improved */ },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Run commands
            new PaletteCommand
            {
                Id = "run-start",
                Name = "Run: Start",
                Description = "Start the project",
                Shortcut = "F5",
                Icon = "▶",
                Category = "Run",
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab && tab.CanRun) tab.StartRunCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel { CanRun: true }
            },
            new PaletteCommand
            {
                Id = "run-stop",
                Name = "Run: Stop",
                Description = "Stop the running project",
                Shortcut = "Shift+F5",
                Icon = "⏹",
                Category = "Run",
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab && tab.CanStop) tab.StopRunCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel { CanStop: true }
            },
            new PaletteCommand
            {
                Id = "run-restart",
                Name = "Run: Restart",
                Description = "Restart the running project",
                Icon = "🔄",
                Category = "Run",
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.RestartRunCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel { RunState: RunState.Running }
            },
            new PaletteCommand
            {
                Id = "run-toggle-terminal",
                Name = "Run: Toggle Terminal",
                Description = "Show/hide run terminal panel",
                Icon = "📺",
                Category = "Run",
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.ToggleRunTerminalCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new PaletteCommand
            {
                Id = "run-open-url",
                Name = "Run: Open URL",
                Description = "Open detected localhost URL in browser",
                Icon = "🌐",
                Category = "Run",
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab && !string.IsNullOrEmpty(tab.DetectedRunUrl)) RunUrlDetectionService.OpenInBrowser(tab.DetectedRunUrl); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel { HasDetectedRunUrl: true }
            }
        };
    }

    private void FilterPaletteCommands()
    {
        _filteredPaletteCommands.Clear();
        var searchText = PaletteSearchText?.ToLower() ?? "";

        // Get static commands
        var filtered = _allPaletteCommands
            .Where(c => c.CanExecute == null || c.CanExecute()) // Evaluate CanExecute on the spot
            .Where(c =>
                string.IsNullOrEmpty(searchText) ||
                c.Name.ToLower().Contains(searchText) ||
                (c.Description?.ToLower().Contains(searchText) ?? false) ||
                c.Category.ToLower().Contains(searchText))
            .ToList();

        foreach (var command in filtered)
        {
            _filteredPaletteCommands.Add(command);
        }

        // Add dynamic profile launch commands
        foreach (var profile in _profileRegistry.Profiles)
        {
            var profileName = $"Launch: {profile.Name}";
            var matchesSearch = string.IsNullOrEmpty(searchText) ||
                               profileName.ToLower().Contains(searchText) ||
                               "profile".Contains(searchText) ||
                               "launch".Contains(searchText);

            if (matchesSearch)
            {
                var capturedProfile = profile; // Capture for closure
                _filteredPaletteCommands.Add(new PaletteCommand
                {
                    Id = $"launch-profile-{profile.Id}",
                    Name = profileName,
                    Description = profile.Command,
                    Shortcut = profile.Shortcut ?? "",
                    Icon = profile.Icon ?? "▶",
                    Category = "Profile",
                    Execute = () => OpenProfileTab(capturedProfile)
                });
            }
        }

        if (FilteredPaletteCommands.Any())
        {
            SelectedPaletteCommand = FilteredPaletteCommands.First();
        }
        else
        {
            SelectedPaletteCommand = null;
        }
    }

    [RelayCommand]
    private void ExecuteSelectedPaletteCommand()
    {
        if (SelectedPaletteCommand != null)
        {
            IsCommandPaletteOpen = false;
            SelectedPaletteCommand.Execute();
        }
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

public class RunTerminalRequestedEventArgs : EventArgs
{
    public required TerminalPairTabViewModel Tab { get; init; }
    public required Domain.RunConfiguration Configuration { get; init; }
    public bool IsStop { get; init; }
}

