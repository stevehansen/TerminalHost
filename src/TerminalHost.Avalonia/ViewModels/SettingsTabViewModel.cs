using System.Collections.ObjectModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;

namespace TerminalHost.ViewModels;

/// <summary>
/// View mode for the settings editor.
/// </summary>
public enum SettingsViewMode
{
    Rich,
    Raw
}

/// <summary>
/// Settings section for navigation.
/// </summary>
public enum SettingsSection
{
    General,
    Terminals,
    AiAssistants,
    Profiles,
    QuickCommands,
    LinkPatterns,
    ProjectTypes,
    ClaudeCommands,
    DirectorySettings,
    ApiWebhooks,
    Channels,
    Containers,
    StatusOverlay
}

public partial class SettingsTabViewModel : ObservableObject, ITabViewModel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfigurationService _configService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private readonly IProcessService? _processService;
    private readonly IClipboardService? _clipboardService;
    private readonly IContainerService? _containerService;
    private string _originalJson = "";

    [ObservableProperty]
    private string _title = "Settings";

    [ObservableProperty]
    private string _jsonText = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isDirty;

    // Flag to prevent syncing during load
    private bool _isLoading;

    // View mode (Rich or Raw)
    [ObservableProperty]
    private SettingsViewMode _viewMode = SettingsViewMode.Rich;

    // Selected section for sidebar navigation
    [ObservableProperty]
    private SettingsSection _selectedSection = SettingsSection.General;

    // Rich mode settings properties
    [ObservableProperty]
    private bool _confirmOnClose;

    [ObservableProperty]
    private bool _showInSystemTray;

    [ObservableProperty]
    private WorkspaceSortMode _workspaceSortMode;

    public int WorkspaceSortModeIndex
    {
        get => (int)WorkspaceSortMode;
        set { WorkspaceSortMode = (WorkspaceSortMode)value; OnPropertyChanged(); }
    }

    [ObservableProperty]
    private bool _touchMode;

    [ObservableProperty]
    private GitTrackingMode _gitTrackingMode;

    public int GitTrackingModeIndex
    {
        get => (int)GitTrackingMode;
        set { GitTrackingMode = (GitTrackingMode)value; OnPropertyChanged(); }
    }

    [ObservableProperty]
    private bool _gitAutoFetch = true;

    [ObservableProperty]
    private int _gitAutoFetchIntervalSeconds = 60;

    [ObservableProperty]
    private string _customCommand = "";

    [ObservableProperty]
    private string _customCommandName = "";

    [ObservableProperty]
    private string _customCommandIcon = "";

    [ObservableProperty]
    private string _shellCommand = "";

    [ObservableProperty]
    private string _shellCommandName = "";

    [ObservableProperty]
    private string _shellCommandIcon = "";

    // Custom paths for terminal PATH environment
    [ObservableProperty]
    private ObservableCollection<string> _customPaths = [];

    [ObservableProperty]
    private string? _selectedCustomPath;

    [ObservableProperty]
    private string _newCustomPath = "";

    // Quick commands collection
    [ObservableProperty]
    private ObservableCollection<QuickCommand> _quickCommands = [];

    [ObservableProperty]
    private QuickCommand? _selectedQuickCommand;

    // Quick command editing properties
    [ObservableProperty]
    private string _editQcLabel = "";

    [ObservableProperty]
    private string _editQcIcon = "";

    [ObservableProperty]
    private string _editQcText = "";

    [ObservableProperty]
    private QuickCommandTarget _editQcTarget = QuickCommandTarget.Custom;

    /// <summary>
    /// Index-based property for ComboBox binding (Avalonia doesn't support SelectedValuePath).
    /// Maps: 0 = Custom, 1 = Shell
    /// </summary>
    public int EditQcTargetIndex
    {
        get => (int)EditQcTarget;
        set
        {
            if (EditQcTarget != (QuickCommandTarget)value)
            {
                EditQcTarget = (QuickCommandTarget)value;
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    private string _editQcShortcut = "";

    [ObservableProperty]
    private bool _isCapturingShortcut;

    [ObservableProperty]
    private string _qcShortcutWarning = "";

    [ObservableProperty]
    private bool _editQcAppendNewline = true;

    [ObservableProperty]
    private bool _editQcUseUserInput = false;

    // AI Assistants collection
    [ObservableProperty]
    private ObservableCollection<AiAssistant> _aiAssistants = [];

    [ObservableProperty]
    private AiAssistant? _selectedAiAssistant;

    // AI Assistant editing properties
    [ObservableProperty]
    private string _editAiName = "";

    [ObservableProperty]
    private string _editAiCommand = "";

    [ObservableProperty]
    private string _editAiIcon = "";

    [ObservableProperty]
    private string _editAiDetectionCommand = "";

    [ObservableProperty]
    private bool _editAiEnabled;

    [ObservableProperty]
    private bool _editAiIsDefault;

    // Profiles collection
    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = [];

    [ObservableProperty]
    private Profile? _selectedProfile;

    // Profile editing properties
    [ObservableProperty]
    private string _editProfileName = "";

    [ObservableProperty]
    private string _editProfileCommand = "";

    [ObservableProperty]
    private string _editProfileIcon = "";

    [ObservableProperty]
    private string _editProfileShortcut = "";

    [ObservableProperty]
    private string _profileShortcutWarning = "";

    [ObservableProperty]
    private bool _editProfileAutoStart;

    // Link patterns collection
    [ObservableProperty]
    private ObservableCollection<LinkPattern> _linkPatterns = [];

    [ObservableProperty]
    private LinkPattern? _selectedLinkPattern;

    // Project types collection
    [ObservableProperty]
    private ObservableCollection<ProjectType> _projectTypes = [];

    [ObservableProperty]
    private ProjectType? _selectedProjectType;

    // Link pattern editing properties
    [ObservableProperty]
    private string _editLpName = "";

    [ObservableProperty]
    private string _editLpPattern = "";

    [ObservableProperty]
    private string _editLpUrlTemplate = "";

    [ObservableProperty]
    private bool _editLpEnabled = true;

    [ObservableProperty]
    private int _editLpPriority = 0;

    // Regex test properties
    [ObservableProperty]
    private string _regexTestInput = "";

    [ObservableProperty]
    private string _regexTestMatch = "";

    [ObservableProperty]
    private string _regexTestUrl = "";

    [ObservableProperty]
    private bool _regexTestIsValid = true;

    [ObservableProperty]
    private string _regexTestError = "";

    // Project type editing properties
    [ObservableProperty]
    private string _editPtId = "";

    [ObservableProperty]
    private string _editPtName = "";

    [ObservableProperty]
    private string _editPtDetectFiles = "";

    [ObservableProperty]
    private string _editPtDefaultCommand = "";

    [ObservableProperty]
    private string _editPtWatchCommand = "";

    [ObservableProperty]
    private string _editPtUrlPattern = "";

    [ObservableProperty]
    private int _editPtPriority = 0;

    // Directory settings
    [ObservableProperty]
    private ObservableCollection<string> _directories = [];

    [ObservableProperty]
    private string? _selectedDirectory;

    [ObservableProperty]
    private DirectorySettings? _currentDirectorySettings;

    // Directory settings editing properties
    [ObservableProperty]
    private TerminalLayoutMode _editDsLayoutMode = TerminalLayoutMode.HorizontalSplit;

    [ObservableProperty]
    private double _editDsSplitRatio = 0.6;

    [ObservableProperty]
    private bool _editDsShowRunTerminal = false;

    [ObservableProperty]
    private double _editDsRunSplitRatio = 0.3;

    [ObservableProperty]
    private bool _editDsShowExplorer = false;

    [ObservableProperty]
    private double _editDsExplorerRatio = 0.25;

    [ObservableProperty]
    private bool _editDsShowLeftPanel = false;

    [ObservableProperty]
    private double _editDsLeftPanelRatio = 0.25;

    // AI Assistant selection for the directory
    [ObservableProperty]
    private AiAssistant? _editDsAiAssistant;

    // Run configurations for the selected directory
    [ObservableProperty]
    private ObservableCollection<RunConfiguration> _runConfigurations = [];

    [ObservableProperty]
    private RunConfiguration? _selectedRunConfiguration;

    // Run configuration editing properties
    [ObservableProperty]
    private string _editRcName = "";

    [ObservableProperty]
    private string _editRcCommand = "";

    [ObservableProperty]
    private bool _editRcIsDefault = false;

    [ObservableProperty]
    private string _editRcUrlPattern = "";

    // API & Webhooks settings
    [ObservableProperty]
    private bool _apiEnabled;

    [ObservableProperty]
    private int _apiPort = 19280;

    [ObservableProperty]
    private string _apiBindAddress = "127.0.0.1";

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private bool _apiEnableSse = true;

    [ObservableProperty]
    private string _apiCorsOrigins = "http://localhost:*";

    [ObservableProperty]
    private bool _apiEnableWebhooks;

    [ObservableProperty]
    private ObservableCollection<WebhookEndpoint> _apiWebhooks = [];

    [ObservableProperty]
    private WebhookEndpoint? _selectedWebhook;

    // Webhook editing properties
    [ObservableProperty]
    private string _editWhName = "";

    [ObservableProperty]
    private string _editWhUrl = "";

    [ObservableProperty]
    private string _editWhSecret = "";

    [ObservableProperty]
    private string _editWhEvents = "*";

    [ObservableProperty]
    private int _editWhDebounceMs = 500;

    [ObservableProperty]
    private int _editWhBatchWindowMs = 0;

    [ObservableProperty]
    private int _editWhMaxRetries = 3;

    [ObservableProperty]
    private bool _editWhEnabled = true;

    // Channel settings
    [ObservableProperty]
    private bool _channelEnabled;

    [ObservableProperty]
    private string _channelServerPath = "";

    [ObservableProperty]
    private string _channelEventFilters = "repo.git_status_changed, repo.branch_switched, repo.commit, channel.user_message";

    [ObservableProperty]
    private bool _channelUseDevelopmentFlag = true;

    [ObservableProperty]
    private bool _channelAutoRegisterMcp = true;

    // Container settings
    [ObservableProperty]
    private bool _containerEnabled;

    [ObservableProperty]
    private string _containerDockerPath = "docker";

    [ObservableProperty]
    private string _containerImageName = "terminalhost-workspace";

    [ObservableProperty]
    private string _containerImageTag = "latest";

    [ObservableProperty]
    private bool _containerMountSsh;

    [ObservableProperty]
    private bool _containerMountGhCli = true;

    [ObservableProperty]
    private bool _containerAutoApprove = true;

    [ObservableProperty]
    private string _containerNetworkMode = "bridge";

    [ObservableProperty]
    private bool _containerStopOnExit = true;

    // Image status
    [ObservableProperty]
    private string _containerImageStatus = "Unknown";

    [ObservableProperty]
    private bool _containerImageStale;

    // Reference volumes
    [ObservableProperty]
    private ObservableCollection<ReferenceVolume> _containerReferenceVolumes = [];

    [ObservableProperty]
    private ReferenceVolume? _selectedReferenceVolume;

    [ObservableProperty]
    private string _editRefVolHostPath = "";

    [ObservableProperty]
    private string _editRefVolName = "";

    // Active containers
    [ObservableProperty]
    private ObservableCollection<ContainerInfo> _activeContainers = [];

    [ObservableProperty]
    private ContainerInfo? _selectedContainer;

    // MCP Collab integration
    [ObservableProperty]
    private bool _mcpCollabInstalled;

    [ObservableProperty]
    private bool _mcpCollabDetecting;

    [ObservableProperty]
    private string _mcpCollabStatus = "";

    // Status Overlay settings
    [ObservableProperty]
    private bool _overlayAutoShowOnUnfocus;

    [ObservableProperty]
    private int _overlaySizeIndex;

    [ObservableProperty]
    private double _overlayOpacity = 0.9;

    public string McpCollabUrl => $"http://localhost:{ApiPort}/api/mcp";

    public bool IsApiKeyRequired => ApiBindAddress != "127.0.0.1" && ApiBindAddress != "localhost";

    public string TabIcon => "\u2699"; // Gear symbol
    public string WorkingDirectory => "Settings";
    public bool IsCloseable => true;
    public bool CanDuplicate => false;
    public bool IsAnyTerminalActive => false;
    public bool HasUnreadActivity => false;
    public bool IsSelected { get; set; }
    public bool IsVisibleInFocusMode => true; // Settings always visible
    public bool ShowActivitySpinner => false; // No terminal activity
    public bool ShowCompletedIndicator => false; // No terminal activity
    public bool IsWaitingForInput => false; // No terminal
    public bool ShowWaitingIndicator => false; // No terminal
    public bool ShowClaudeTaskIndicator => false; // Not a project tab
    public bool IsTerminalInitialized => true; // No terminal to initialize
    public Task InitializeTerminalsAsync() => Task.CompletedTask; // No-op
    public void UpdateFocusModeVisibility(bool isFocusModeEnabled, IReadOnlyList<string> currentTaskProjects) { } // No-op, always visible
    public void ClearUnreadActivity() { }
    public string DisplayTitle => Title;

    public event EventHandler? CloseRequested;
    public event EventHandler? JsonTextReloaded;
    public event EventHandler? ConfigSaved;

    public SettingsTabViewModel(IConfigurationService configService, IDialogService dialogService, IToastService toastService,
        IProcessService? processService = null, IClipboardService? clipboardService = null,
        IContainerService? containerService = null)
    {
        _configService = configService;
        _dialogService = dialogService;
        _toastService = toastService;
        _processService = processService;
        _clipboardService = clipboardService;
        _containerService = containerService;
        LoadSettings();
    }

    public void LoadSettings()
    {
        _isLoading = true;
        try
        {
            _originalJson = _configService.LoadRawJson();
            JsonText = _originalJson;
            IsDirty = false;
            ErrorMessage = "";
            HasError = false;

            // Load rich mode properties from JSON
            LoadRichModeProperties();

            // Fire-and-forget MCP collab detection
            if (_processService != null)
            {
                _ = DetectMcpCollabAsync();
            }

            JsonTextReloaded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void LoadRichModeProperties()
    {
        if (string.IsNullOrEmpty(JsonText)) return;

        try
        {
            var config = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions);

            if (config == null) return;

            // General settings
            ConfirmOnClose = config.Settings.ConfirmOnClose;
            ShowInSystemTray = config.Settings.ShowInSystemTray;
            WorkspaceSortMode = config.Settings.WorkspaceSortMode;
            TouchMode = config.Settings.TouchMode;
            GitTrackingMode = config.Settings.GitTrackingMode;
            GitAutoFetch = config.Settings.GitAutoFetch;
            GitAutoFetchIntervalSeconds = config.Settings.GitAutoFetchIntervalSeconds;

            // Terminal settings
            CustomCommand = config.Settings.CustomCommand;
            CustomCommandName = config.Settings.CustomCommandName;
            CustomCommandIcon = config.Settings.CustomCommandIcon;
            ShellCommand = config.Settings.ShellCommand;
            ShellCommandName = config.Settings.ShellCommandName;
            ShellCommandIcon = config.Settings.ShellCommandIcon;

            // Custom paths
            CustomPaths = new ObservableCollection<string>(config.Settings.CustomPaths);

            // Profiles
            Profiles = new ObservableCollection<Profile>(config.Profiles);

            // AI Assistants
            var aiAssistants = config.AiAssistants.Count > 0
                ? config.AiAssistants
                : AiAssistant.GetDefaults();
            AiAssistants = new ObservableCollection<AiAssistant>(aiAssistants);

            // Quick commands
            QuickCommands = new ObservableCollection<QuickCommand>(config.QuickCommands);

            // Link patterns
            LinkPatterns = new ObservableCollection<LinkPattern>(config.LinkPatterns);

            // Project types
            ProjectTypes = new ObservableCollection<ProjectType>(config.ProjectTypes);

            // API & Webhooks settings
            ApiEnabled = config.Settings.Api.Enabled;
            ApiPort = config.Settings.Api.Port;
            ApiBindAddress = config.Settings.Api.BindAddress;
            ApiKey = config.Settings.Api.ApiKey ?? "";
            ApiEnableSse = config.Settings.Api.EnableSse;
            ApiCorsOrigins = string.Join(", ", config.Settings.Api.CorsOrigins);
            ApiEnableWebhooks = config.Settings.Api.EnableWebhooks;
            ApiWebhooks = new ObservableCollection<WebhookEndpoint>(config.Settings.Api.Webhooks);

            // Channel settings
            ChannelEnabled = config.Settings.Channel.Enabled;
            ChannelServerPath = config.Settings.Channel.ChannelServerPath ?? "";
            ChannelEventFilters = string.Join(", ", config.Settings.Channel.EventFilters);
            ChannelUseDevelopmentFlag = config.Settings.Channel.UseDevelopmentFlag;
            ChannelAutoRegisterMcp = config.Settings.Channel.AutoRegisterMcp;

            // Container settings
            ContainerEnabled = config.Settings.Container.Enabled;
            ContainerDockerPath = config.Settings.Container.DockerPath;
            ContainerImageName = config.Settings.Container.ImageName;
            ContainerImageTag = config.Settings.Container.ImageTag;
            ContainerMountSsh = config.Settings.Container.MountSsh;
            ContainerMountGhCli = config.Settings.Container.MountGhCli;
            ContainerAutoApprove = config.Settings.Container.AutoApproveInContainer;
            ContainerNetworkMode = config.Settings.Container.NetworkMode;
            ContainerStopOnExit = config.Settings.Container.StopContainersOnExit;
            ContainerReferenceVolumes = new ObservableCollection<ReferenceVolume>(config.Settings.Container.ReferenceVolumes);
            _ = RefreshContainersAsync();

            // Status Overlay settings
            OverlayAutoShowOnUnfocus = config.Settings.StatusOverlay.AutoShowOnUnfocus;
            OverlaySizeIndex = (int)config.Settings.StatusOverlay.Size;
            OverlayOpacity = config.Settings.StatusOverlay.Opacity;

            // Directory settings - include both open folders and folders with saved settings
            var allDirectories = config.OpenFolders
                .Union(config.DirectorySettings.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k);
            Directories = new ObservableCollection<string>(allDirectories);
            if (Directories.Count > 0 && SelectedDirectory == null)
            {
                SelectedDirectory = Directories.First();
            }
            UpdateCurrentDirectorySettings();
        }
        catch (JsonException)
        {
            // If JSON is invalid, rich mode properties won't be updated
        }
    }

    private void SyncRichModeToJson()
    {
        try
        {
            var config = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions) ?? new AppConfiguration();

            // General settings
            config.Settings.ConfirmOnClose = ConfirmOnClose;
            config.Settings.ShowInSystemTray = ShowInSystemTray;
            config.Settings.WorkspaceSortMode = WorkspaceSortMode;
            config.Settings.TouchMode = TouchMode;
            config.Settings.GitTrackingMode = GitTrackingMode;
            config.Settings.GitAutoFetch = GitAutoFetch;
            config.Settings.GitAutoFetchIntervalSeconds = GitAutoFetchIntervalSeconds;

            // Terminal settings
            config.Settings.CustomCommand = CustomCommand;
            config.Settings.CustomCommandName = CustomCommandName;
            config.Settings.CustomCommandIcon = CustomCommandIcon;
            config.Settings.ShellCommand = ShellCommand;
            config.Settings.ShellCommandName = ShellCommandName;
            config.Settings.ShellCommandIcon = ShellCommandIcon;

            // Custom paths
            config.Settings.CustomPaths = CustomPaths.ToList();

            // Profiles
            config.Profiles = Profiles.ToList();

            // AI Assistants
            config.AiAssistants = AiAssistants.ToList();

            // Quick commands
            config.QuickCommands = QuickCommands.ToList();

            // Link patterns
            config.LinkPatterns = LinkPatterns.ToList();

            // Project types
            config.ProjectTypes = ProjectTypes.ToList();

            // API & Webhooks settings
            config.Settings.Api.Enabled = ApiEnabled;
            config.Settings.Api.Port = ApiPort;
            config.Settings.Api.BindAddress = ApiBindAddress;
            config.Settings.Api.ApiKey = string.IsNullOrEmpty(ApiKey) ? null : ApiKey;
            config.Settings.Api.EnableSse = ApiEnableSse;
            config.Settings.Api.CorsOrigins = ApiCorsOrigins
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            config.Settings.Api.EnableWebhooks = ApiEnableWebhooks;
            config.Settings.Api.Webhooks = ApiWebhooks.ToList();

            // Channel settings
            config.Settings.Channel.Enabled = ChannelEnabled;
            config.Settings.Channel.ChannelServerPath = string.IsNullOrEmpty(ChannelServerPath) ? null : ChannelServerPath;
            config.Settings.Channel.EventFilters = ChannelEventFilters
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            config.Settings.Channel.UseDevelopmentFlag = ChannelUseDevelopmentFlag;
            config.Settings.Channel.AutoRegisterMcp = ChannelAutoRegisterMcp;

            // Container settings
            config.Settings.Container.Enabled = ContainerEnabled;
            config.Settings.Container.DockerPath = ContainerDockerPath;
            config.Settings.Container.ImageName = ContainerImageName;
            config.Settings.Container.ImageTag = ContainerImageTag;
            config.Settings.Container.MountSsh = ContainerMountSsh;
            config.Settings.Container.MountGhCli = ContainerMountGhCli;
            config.Settings.Container.AutoApproveInContainer = ContainerAutoApprove;
            config.Settings.Container.NetworkMode = ContainerNetworkMode;
            config.Settings.Container.StopContainersOnExit = ContainerStopOnExit;
            config.Settings.Container.ReferenceVolumes = ContainerReferenceVolumes.ToList();

            // Status Overlay settings
            config.Settings.StatusOverlay.AutoShowOnUnfocus = OverlayAutoShowOnUnfocus;
            config.Settings.StatusOverlay.Size = (StatusOverlaySize)OverlaySizeIndex;
            config.Settings.StatusOverlay.Opacity = OverlayOpacity;

            // Directory settings (update current if selected)
            if (SelectedDirectory != null && CurrentDirectorySettings != null)
            {
                var normalizedPath = NormalizePath(SelectedDirectory);
                config.DirectorySettings[normalizedPath] = CurrentDirectorySettings;
            }

            // Re-serialize
            JsonText = JsonSerializer.Serialize(config, JsonOptions);
            IsDirty = JsonText != _originalJson;
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"Cannot sync settings: {ex.Message}";
            HasError = true;
        }
    }

    private void UpdateCurrentDirectorySettings()
    {
        if (SelectedDirectory == null)
        {
            CurrentDirectorySettings = null;
            return;
        }

        try
        {
            var config = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions);
            var normalizedPath = NormalizePath(SelectedDirectory);

            if (config?.DirectorySettings.TryGetValue(normalizedPath, out var settings) == true)
            {
                CurrentDirectorySettings = settings;
            }
            else
            {
                CurrentDirectorySettings = new DirectorySettings();
            }
        }
        catch
        {
            CurrentDirectorySettings = new DirectorySettings();
        }
    }

    /// <summary>
    /// Normalizes a path to match the format used by MainViewModel for DirectorySettings keys.
    /// </summary>
    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
    }

    /// <summary>
    /// Mark settings as dirty when any rich mode property changes.
    /// </summary>
    private void MarkDirtyFromRichMode()
    {
        if (_isLoading) return;

        if (ViewMode == SettingsViewMode.Rich)
        {
            SyncRichModeToJson();
        }
    }

    // Property change handlers for rich mode - mark dirty
    partial void OnConfirmOnCloseChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnShowInSystemTrayChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnWorkspaceSortModeChanged(WorkspaceSortMode value) { OnPropertyChanged(nameof(WorkspaceSortModeIndex)); MarkDirtyFromRichMode(); }
    partial void OnTouchModeChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnGitTrackingModeChanged(GitTrackingMode value) { OnPropertyChanged(nameof(GitTrackingModeIndex)); MarkDirtyFromRichMode(); }
    partial void OnGitAutoFetchChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnGitAutoFetchIntervalSecondsChanged(int value) => MarkDirtyFromRichMode();
    partial void OnCustomCommandChanged(string value) => MarkDirtyFromRichMode();
    partial void OnCustomCommandNameChanged(string value) => MarkDirtyFromRichMode();
    partial void OnCustomCommandIconChanged(string value) => MarkDirtyFromRichMode();
    partial void OnShellCommandChanged(string value) => MarkDirtyFromRichMode();
    partial void OnShellCommandNameChanged(string value) => MarkDirtyFromRichMode();
    partial void OnShellCommandIconChanged(string value) => MarkDirtyFromRichMode();

    // API & Webhooks change handlers
    partial void OnApiEnabledChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnApiPortChanged(int value) { OnPropertyChanged(nameof(McpCollabUrl)); MarkDirtyFromRichMode(); }
    partial void OnApiBindAddressChanged(string value) { OnPropertyChanged(nameof(IsApiKeyRequired)); MarkDirtyFromRichMode(); }
    partial void OnApiKeyChanged(string value) => MarkDirtyFromRichMode();
    partial void OnApiEnableSseChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnApiCorsOriginsChanged(string value) => MarkDirtyFromRichMode();
    partial void OnApiEnableWebhooksChanged(bool value) => MarkDirtyFromRichMode();

    // Channel change handlers
    partial void OnChannelEnabledChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnChannelServerPathChanged(string value) => MarkDirtyFromRichMode();
    partial void OnChannelEventFiltersChanged(string value) => MarkDirtyFromRichMode();
    partial void OnChannelUseDevelopmentFlagChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnChannelAutoRegisterMcpChanged(bool value) => MarkDirtyFromRichMode();

    // Container change handlers
    partial void OnContainerEnabledChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnContainerDockerPathChanged(string value) => MarkDirtyFromRichMode();
    partial void OnContainerImageNameChanged(string value) => MarkDirtyFromRichMode();
    partial void OnContainerImageTagChanged(string value) => MarkDirtyFromRichMode();
    partial void OnContainerMountSshChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnContainerMountGhCliChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnContainerAutoApproveChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnContainerNetworkModeChanged(string value) => MarkDirtyFromRichMode();
    partial void OnContainerStopOnExitChanged(bool value) => MarkDirtyFromRichMode();

    partial void OnSelectedReferenceVolumeChanged(ReferenceVolume? value)
    {
        if (value != null) { EditRefVolHostPath = value.HostPath; EditRefVolName = value.Name; }
        else { EditRefVolHostPath = ""; EditRefVolName = ""; }
    }
    partial void OnOverlayAutoShowOnUnfocusChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnOverlaySizeIndexChanged(int value) => MarkDirtyFromRichMode();
    partial void OnOverlayOpacityChanged(double value) => MarkDirtyFromRichMode();

    partial void OnSelectedWebhookChanged(WebhookEndpoint? value)
    {
        if (value != null)
        {
            EditWhName = value.Name;
            EditWhUrl = value.Url;
            EditWhSecret = value.Secret ?? "";
            EditWhEvents = string.Join(", ", value.Events);
            EditWhDebounceMs = value.DebounceMs;
            EditWhBatchWindowMs = value.BatchWindowMs;
            EditWhMaxRetries = value.MaxRetries;
            EditWhEnabled = value.Enabled;
        }
    }

    [RelayCommand]
    private void AddWebhook()
    {
        var webhook = new WebhookEndpoint { Name = "New Webhook" };
        ApiWebhooks.Add(webhook);
        SelectedWebhook = webhook;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void RemoveWebhook()
    {
        if (SelectedWebhook == null) return;
        ApiWebhooks.Remove(SelectedWebhook);
        SelectedWebhook = ApiWebhooks.FirstOrDefault();
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyWebhook()
    {
        if (SelectedWebhook == null) return;
        SelectedWebhook.Name = EditWhName;
        SelectedWebhook.Url = EditWhUrl;
        SelectedWebhook.Secret = string.IsNullOrEmpty(EditWhSecret) ? null : EditWhSecret;
        SelectedWebhook.Events = EditWhEvents
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        SelectedWebhook.DebounceMs = EditWhDebounceMs;
        SelectedWebhook.BatchWindowMs = EditWhBatchWindowMs;
        SelectedWebhook.MaxRetries = EditWhMaxRetries;
        SelectedWebhook.Enabled = EditWhEnabled;
        SyncRichModeToJson();
    }

    // Container commands

    [RelayCommand]
    private void AddReferenceVolume()
    {
        var vol = new ReferenceVolume { HostPath = "", Name = "new-volume" };
        ContainerReferenceVolumes.Add(vol);
        SelectedReferenceVolume = vol;
        MarkDirtyFromRichMode();
    }

    [RelayCommand]
    private void DeleteReferenceVolume()
    {
        if (SelectedReferenceVolume == null) return;
        var index = ContainerReferenceVolumes.IndexOf(SelectedReferenceVolume);
        ContainerReferenceVolumes.Remove(SelectedReferenceVolume);
        if (ContainerReferenceVolumes.Count > 0)
            SelectedReferenceVolume = ContainerReferenceVolumes[Math.Min(index, ContainerReferenceVolumes.Count - 1)];
        else
            SelectedReferenceVolume = null;
        MarkDirtyFromRichMode();
    }

    [RelayCommand]
    private void ApplyReferenceVolume()
    {
        if (SelectedReferenceVolume == null) return;
        SelectedReferenceVolume.HostPath = EditRefVolHostPath;
        SelectedReferenceVolume.Name = EditRefVolName;
        var index = ContainerReferenceVolumes.IndexOf(SelectedReferenceVolume);
        if (index >= 0)
        {
            var item = SelectedReferenceVolume;
            ContainerReferenceVolumes.RemoveAt(index);
            ContainerReferenceVolumes.Insert(index, item);
            SelectedReferenceVolume = item;
        }
        MarkDirtyFromRichMode();
    }

    [RelayCommand]
    private async Task RefreshContainersAsync()
    {
        if (_containerService == null) return;
        try
        {
            var containers = await _containerService.ListContainersAsync();
            ActiveContainers = new ObservableCollection<ContainerInfo>(containers);
        }
        catch { ActiveContainers = []; }
    }

    [RelayCommand]
    private async Task StopContainerAsync(ContainerInfo? container)
    {
        if (_containerService == null || container == null) return;
        try
        {
            var dockerPath = _configService.Load().Settings.Container.DockerPath;
            await _processService!.RunAsync(dockerPath, $"stop {container.Name}", timeout: TimeSpan.FromSeconds(30));
            _toastService.Show($"Stopped {container.Name}", ToastType.Success);
            await RefreshContainersAsync();
        }
        catch (Exception ex) { _toastService.Show($"Failed to stop: {ex.Message}", ToastType.Error); }
    }

    [RelayCommand]
    private async Task RemoveContainerAsync(ContainerInfo? container)
    {
        if (_containerService == null || container == null) return;
        try
        {
            var dockerPath = _configService.Load().Settings.Container.DockerPath;
            await _processService!.RunAsync(dockerPath, $"stop {container.Name}", timeout: TimeSpan.FromSeconds(30));
            await _processService!.RunAsync(dockerPath, $"rm {container.Name}", timeout: TimeSpan.FromSeconds(10));
            _toastService.Show($"Removed {container.Name}", ToastType.Success);
            await RefreshContainersAsync();
        }
        catch (Exception ex) { _toastService.Show($"Failed to remove: {ex.Message}", ToastType.Error); }
    }

    [RelayCommand]
    private async Task CleanStoppedContainersAsync()
    {
        if (_containerService == null) return;
        try
        {
            var count = await _containerService.CleanStoppedContainersAsync();
            _toastService.Show(count > 0 ? $"Removed {count} stopped container(s)" : "No stopped containers", ToastType.Success);
            await RefreshContainersAsync();
        }
        catch (Exception ex) { _toastService.Show($"Failed to clean: {ex.Message}", ToastType.Error); }
    }

    [RelayCommand]
    private void EditDockerfile()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TerminalHost", "container");
        var dockerfilePath = Path.Combine(configDir, "Dockerfile");
        _containerService?.EnsureDockerfileExists();
        if (File.Exists(dockerfilePath))
            _processService?.Start(dockerfilePath);
        else
            _toastService.Show("Dockerfile not found", ToastType.Warning);
    }

    [RelayCommand]
    private void RefreshImageStatus()
    {
        if (_containerService == null) return;
        var status = _containerService.CheckDockerfileStatus();
        ContainerImageStatus = status switch
        {
            DockerfileStatus.Missing => "Not Built",
            DockerfileStatus.UpToDate => "Up to Date",
            DockerfileStatus.Stale => "Update Available",
            DockerfileStatus.UserModified => "Custom (user modified)",
            _ => "Unknown"
        };
        ContainerImageStale = status == DockerfileStatus.Stale;
    }

    [RelayCommand]
    private async Task RebuildContainerImageAsync()
    {
        if (_containerService == null) return;
        var status = _containerService.CheckDockerfileStatus();
        if (status == DockerfileStatus.Stale)
            _containerService.UpdateDockerfileToLatest();
        using var toast = _toastService.ShowProgress("Building Docker image...");
        try
        {
            var success = await _containerService.BuildImageAsync(line =>
            {
                if (!string.IsNullOrWhiteSpace(line))
                    toast.Update(line.Length > 80 ? line[..80] + "..." : line);
            });
            if (success) { toast.Complete("Docker image built successfully"); RefreshImageStatus(); }
            else toast.Fail("Image build failed — check Docker Desktop logs");
        }
        catch (Exception ex) { toast.Fail($"Build failed: {ex.Message}"); }
    }

    // MCP Collab integration commands

    private static (string shell, string argFormat) GetShellCommand(string command)
    {
        if (OperatingSystem.IsWindows())
            return ("powershell.exe", $"-NoProfile -Command \"{command}\"");

        // Use login shell (-l) to pick up user's PATH (e.g. ~/.local/bin for claude)
        // GUI apps on macOS have a minimal PATH that excludes user additions
        return ("/bin/zsh", $"-l -c \"{command.Replace("\"", "\\\"")}\"");
    }

    [RelayCommand]
    private async Task DetectMcpCollabAsync()
    {
        if (_processService == null) return;

        McpCollabDetecting = true;
        try
        {
            var (shell, args) = GetShellCommand("claude mcp list");
            var (exitCode, output, error) = await _processService.RunAsync(shell, args);

            var combined = output + error;
            McpCollabInstalled = combined.Contains("terminalhost-collab", StringComparison.OrdinalIgnoreCase);
            McpCollabStatus = McpCollabInstalled ? "Installed" : "Not registered";
        }
        catch
        {
            McpCollabInstalled = false;
            McpCollabStatus = "Detection failed";
        }
        finally
        {
            McpCollabDetecting = false;
        }
    }

    [RelayCommand]
    private async Task InstallMcpCollabAsync()
    {
        if (_processService == null) return;

        try
        {
            var url = McpCollabUrl;
            var (shell, args) = GetShellCommand($"claude mcp add --transport http terminalhost-collab {url} -s user");
            var (exitCode, output, error) = await _processService.RunAsync(shell, args);

            if (exitCode == 0)
            {
                _toastService.Show("MCP Collab server registered in Claude Code", ToastType.Success);
            }
            else
            {
                _toastService.Show($"MCP registration failed: {error}", ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            _toastService.Show($"MCP registration failed: {ex.Message}", ToastType.Error);
        }

        await DetectMcpCollabAsync();
    }

    [RelayCommand]
    private async Task UninstallMcpCollabAsync()
    {
        if (_processService == null) return;

        try
        {
            var (shell, args) = GetShellCommand("claude mcp remove terminalhost-collab -s user");
            var (exitCode, output, error) = await _processService.RunAsync(shell, args);

            if (exitCode == 0)
            {
                _toastService.Show("MCP Collab server removed from Claude Code", ToastType.Success);
            }
            else
            {
                _toastService.Show($"MCP removal failed: {error}", ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            _toastService.Show($"MCP removal failed: {ex.Message}", ToastType.Error);
        }

        await DetectMcpCollabAsync();
    }

    [RelayCommand]
    private async Task CopyMcpCollabUrlAsync()
    {
        if (_clipboardService == null) return;

        await _clipboardService.SetTextAsync(McpCollabUrl);
        _toastService.Show("MCP URL copied to clipboard", ToastType.Success);
    }

    // Notify EditQcTargetIndex when EditQcTarget changes (for ComboBox binding)
    partial void OnEditQcTargetChanged(QuickCommandTarget value) => OnPropertyChanged(nameof(EditQcTargetIndex));

    // Check for shortcut conflicts when quick command shortcut changes
    partial void OnEditQcShortcutChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            QcShortcutWarning = "";
            return;
        }

        var conflict = ShortcutConflictService.GetConflict(
            value,
            QuickCommands,
            Profiles,
            excludeQuickCommandId: SelectedQuickCommand?.Id);

        QcShortcutWarning = conflict ?? "";
    }

    // Check for shortcut conflicts when profile shortcut changes
    partial void OnEditProfileShortcutChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ProfileShortcutWarning = "";
            return;
        }

        var conflict = ShortcutConflictService.GetConflict(
            value,
            QuickCommands,
            Profiles,
            excludeProfileName: SelectedProfile?.Name);

        ProfileShortcutWarning = conflict ?? "";
    }

    partial void OnViewModeChanged(SettingsViewMode value)
    {
        if (value == SettingsViewMode.Rich)
        {
            // Switching to Rich mode - reload properties from JSON
            LoadRichModeProperties();
        }
        else
        {
            // Switching to Raw mode - reload the JSON editor to show current state
            JsonTextReloaded?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OnTextChanged(string currentText)
    {
        JsonText = currentText;
        IsDirty = currentText != _originalJson;

        // Clear error when user starts editing
        if (HasError)
        {
            ErrorMessage = "";
            HasError = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var (success, error, warning) = _configService.SaveRawJson(JsonText);

        if (success)
        {
            _originalJson = JsonText;
            IsDirty = false;
            ErrorMessage = "";
            HasError = false;
            ConfigSaved?.Invoke(this, EventArgs.Empty);

            // Show warning dialog if there are warnings (save succeeded but with issues)
            if (!string.IsNullOrEmpty(warning))
            {
                _dialogService.ShowWarning($"Settings saved with warnings:\n\n{warning}", "Configuration Warning");
            }
            else
            {
                // Show success toast when saved without warnings
                _toastService.Show("Settings saved", ToastType.Success);
            }
        }
        else
        {
            ErrorMessage = error ?? "Unknown error";
            HasError = true;
        }
    }

    [RelayCommand]
    private void Reload()
    {
        LoadSettings();
    }

    [RelayCommand]
    private void Format()
    {
        try
        {
            // Parse and re-serialize with indentation
            var parsed = JsonSerializer.Deserialize<JsonElement>(JsonText);
            var formatted = JsonSerializer.Serialize(parsed, JsonOptions);

            JsonText = formatted;
            IsDirty = formatted != _originalJson;
            ErrorMessage = "";
            HasError = false;
            JsonTextReloaded?.Invoke(this, EventArgs.Empty);
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"Cannot format: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ResetQuickCommands()
    {
        try
        {
            // Create a fresh config to get default quick commands
            var defaultConfig = new AppConfiguration();

            if (ViewMode == SettingsViewMode.Rich)
            {
                // In rich mode, update the collection directly
                QuickCommands = new ObservableCollection<QuickCommand>(defaultConfig.QuickCommands);
                SelectedQuickCommand = null;
                SyncRichModeToJson();
            }
            else
            {
                // In raw mode, update the JSON
                var parsed = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions);

                if (parsed == null)
                {
                    ErrorMessage = "Cannot parse current configuration";
                    HasError = true;
                    return;
                }

                // Replace just the quick commands
                parsed.QuickCommands = defaultConfig.QuickCommands;

                // Re-serialize
                var updatedJson = JsonSerializer.Serialize(parsed, JsonOptions);

                JsonText = updatedJson;
                IsDirty = updatedJson != _originalJson;
                JsonTextReloaded?.Invoke(this, EventArgs.Empty);
            }

            ErrorMessage = "";
            HasError = false;
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"Cannot reset quick commands: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private void ResetProjectTypes()
    {
        try
        {
            var defaultProjectTypes = ProjectType.GetDefaults();

            if (ViewMode == SettingsViewMode.Rich)
            {
                ProjectTypes = new ObservableCollection<ProjectType>(defaultProjectTypes);
                SelectedProjectType = null;
                SyncRichModeToJson();
            }
            else
            {
                var parsed = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions);

                if (parsed == null)
                {
                    ErrorMessage = "Cannot parse current configuration";
                    HasError = true;
                    return;
                }

                parsed.ProjectTypes = defaultProjectTypes;

                var updatedJson = JsonSerializer.Serialize(parsed, JsonOptions);

                JsonText = updatedJson;
                IsDirty = updatedJson != _originalJson;
                JsonTextReloaded?.Invoke(this, EventArgs.Empty);
            }

            ErrorMessage = "";
            HasError = false;
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"Cannot reset project types: {ex.Message}";
            HasError = true;
        }
    }

    // Quick command management
    partial void OnSelectedQuickCommandChanged(QuickCommand? value)
    {
        if (value != null)
        {
            EditQcLabel = value.Label;
            EditQcIcon = value.Icon;
            EditQcText = value.Text;
            EditQcTarget = value.Target;
            EditQcShortcut = value.Shortcut ?? "";
            EditQcAppendNewline = value.AppendNewline;
            EditQcUseUserInput = value.UseUserInput;
        }
        else
        {
            EditQcLabel = "";
            EditQcIcon = "";
            EditQcText = "";
            EditQcTarget = QuickCommandTarget.Custom;
            EditQcShortcut = "";
            EditQcAppendNewline = true;
            EditQcUseUserInput = false;
        }
    }

    [RelayCommand]
    private void AddQuickCommand()
    {
        var newCommand = new QuickCommand
        {
            Id = Guid.NewGuid().ToString(),
            Label = "New Command",
            Icon = "",
            Text = "",
            Target = QuickCommandTarget.Custom,
            AppendNewline = true
        };

        QuickCommands.Add(newCommand);
        SelectedQuickCommand = newCommand;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void DeleteQuickCommand()
    {
        if (SelectedQuickCommand == null) return;

        var index = QuickCommands.IndexOf(SelectedQuickCommand);
        QuickCommands.Remove(SelectedQuickCommand);

        // Select next or previous item
        if (QuickCommands.Count > 0)
        {
            SelectedQuickCommand = QuickCommands[Math.Min(index, QuickCommands.Count - 1)];
        }
        else
        {
            SelectedQuickCommand = null;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyQuickCommand()
    {
        if (SelectedQuickCommand == null) return;

        SelectedQuickCommand.Label = EditQcLabel;
        SelectedQuickCommand.Icon = EditQcIcon;
        SelectedQuickCommand.Text = EditQcText;
        SelectedQuickCommand.Target = EditQcTarget;
        SelectedQuickCommand.Shortcut = string.IsNullOrWhiteSpace(EditQcShortcut) ? null : EditQcShortcut;
        SelectedQuickCommand.AppendNewline = EditQcAppendNewline;
        SelectedQuickCommand.UseUserInput = EditQcUseUserInput;

        // Force collection refresh to update the list display
        var index = QuickCommands.IndexOf(SelectedQuickCommand);
        if (index >= 0)
        {
            var cmd = SelectedQuickCommand;
            QuickCommands.RemoveAt(index);
            QuickCommands.Insert(index, cmd);
            SelectedQuickCommand = cmd;
        }

        SyncRichModeToJson();
        Save();
    }

    [RelayCommand]
    private void MoveQuickCommandUp()
    {
        if (SelectedQuickCommand == null) return;

        var index = QuickCommands.IndexOf(SelectedQuickCommand);
        if (index > 0)
        {
            var cmd = SelectedQuickCommand;
            QuickCommands.RemoveAt(index);
            QuickCommands.Insert(index - 1, cmd);
            SelectedQuickCommand = cmd;
            SyncRichModeToJson();
        }
    }

    [RelayCommand]
    private void MoveQuickCommandDown()
    {
        if (SelectedQuickCommand == null) return;

        var index = QuickCommands.IndexOf(SelectedQuickCommand);
        if (index < QuickCommands.Count - 1)
        {
            var cmd = SelectedQuickCommand;
            QuickCommands.RemoveAt(index);
            QuickCommands.Insert(index + 1, cmd);
            SelectedQuickCommand = cmd;
            SyncRichModeToJson();
        }
    }

    [RelayCommand]
    private void ToggleShortcutCapture()
    {
        IsCapturingShortcut = !IsCapturingShortcut;
    }

    [RelayCommand]
    private void ClearShortcut()
    {
        EditQcShortcut = "";
        IsCapturingShortcut = false;
    }

    /// <summary>
    /// Called from the view when a key is pressed while capturing shortcuts.
    /// </summary>
    public void CaptureShortcut(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        if (!IsCapturingShortcut) return;

        // Ignore modifier-only key presses
        if (key == Avalonia.Input.Key.LeftCtrl || key == Avalonia.Input.Key.RightCtrl ||
            key == Avalonia.Input.Key.LeftAlt || key == Avalonia.Input.Key.RightAlt ||
            key == Avalonia.Input.Key.LeftShift || key == Avalonia.Input.Key.RightShift ||
            key == Avalonia.Input.Key.LWin || key == Avalonia.Input.Key.RWin)
        {
            return;
        }

        // Require at least one modifier
        if (modifiers == Avalonia.Input.KeyModifiers.None)
        {
            return;
        }

        // Format the shortcut string
        EditQcShortcut = MainWindow.FormatShortcut(key, modifiers);
        IsCapturingShortcut = false;
    }

    // Custom paths management
    [RelayCommand]
    private void AddCustomPath()
    {
        if (string.IsNullOrWhiteSpace(NewCustomPath)) return;

        var path = NewCustomPath.Trim();
        if (!CustomPaths.Contains(path))
        {
            CustomPaths.Add(path);
            NewCustomPath = "";
            SyncRichModeToJson();
        }
    }

    [RelayCommand]
    private void RemoveCustomPath()
    {
        if (SelectedCustomPath == null) return;

        var index = CustomPaths.IndexOf(SelectedCustomPath);
        CustomPaths.Remove(SelectedCustomPath);

        if (CustomPaths.Count > 0)
        {
            SelectedCustomPath = CustomPaths[Math.Min(index, CustomPaths.Count - 1)];
        }
        else
        {
            SelectedCustomPath = null;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void BrowseCustomPath()
    {
        // This would be called from the view's code-behind for folder picking
    }

    // Profile management
    partial void OnSelectedProfileChanged(Profile? value)
    {
        if (value != null)
        {
            EditProfileName = value.Name;
            EditProfileCommand = value.Command;
            EditProfileIcon = value.Icon ?? "";
            EditProfileShortcut = value.Shortcut ?? "";
            EditProfileAutoStart = value.AutoStart;
        }
        else
        {
            EditProfileName = "";
            EditProfileCommand = "";
            EditProfileIcon = "";
            EditProfileShortcut = "";
            EditProfileAutoStart = false;
        }
    }

    [RelayCommand]
    private void AddProfile()
    {
        var newProfile = new Profile
        {
            Id = $"profile-{DateTime.Now:yyyyMMddHHmmss}",
            Name = "New Profile",
            Command = GetDefaultShell(),
            WorkingDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Icon = "",
            AutoStart = false
        };

        Profiles.Add(newProfile);
        SelectedProfile = newProfile;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null) return;

        var index = Profiles.IndexOf(SelectedProfile);
        Profiles.Remove(SelectedProfile);

        if (Profiles.Count > 0)
        {
            SelectedProfile = Profiles[Math.Min(index, Profiles.Count - 1)];
        }
        else
        {
            SelectedProfile = null;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyProfile()
    {
        if (SelectedProfile == null) return;

        SelectedProfile.Name = EditProfileName;
        SelectedProfile.Command = EditProfileCommand;
        SelectedProfile.Icon = string.IsNullOrWhiteSpace(EditProfileIcon) ? null : EditProfileIcon;
        SelectedProfile.Shortcut = string.IsNullOrWhiteSpace(EditProfileShortcut) ? null : EditProfileShortcut;
        SelectedProfile.AutoStart = EditProfileAutoStart;

        // Force collection refresh
        var index = Profiles.IndexOf(SelectedProfile);
        if (index >= 0)
        {
            var profile = SelectedProfile;
            Profiles.RemoveAt(index);
            Profiles.Insert(index, profile);
            SelectedProfile = profile;
        }

        SyncRichModeToJson();
        Save();
    }

    [RelayCommand]
    private void MoveProfileUp()
    {
        if (SelectedProfile == null) return;

        var index = Profiles.IndexOf(SelectedProfile);
        if (index > 0)
        {
            var profile = SelectedProfile;
            Profiles.RemoveAt(index);
            Profiles.Insert(index - 1, profile);
            SelectedProfile = profile;
            SyncRichModeToJson();
        }
    }

    [RelayCommand]
    private void MoveProfileDown()
    {
        if (SelectedProfile == null) return;

        var index = Profiles.IndexOf(SelectedProfile);
        if (index < Profiles.Count - 1)
        {
            var profile = SelectedProfile;
            Profiles.RemoveAt(index);
            Profiles.Insert(index + 1, profile);
            SelectedProfile = profile;
            SyncRichModeToJson();
        }
    }

    // AI Assistant management
    partial void OnSelectedAiAssistantChanged(AiAssistant? value)
    {
        if (value != null)
        {
            EditAiName = value.Name;
            EditAiCommand = value.Command;
            EditAiIcon = value.Icon;
            EditAiDetectionCommand = value.DetectionCommand ?? "";
            EditAiEnabled = value.Enabled;
            EditAiIsDefault = value.IsDefault;
        }
        else
        {
            EditAiName = "";
            EditAiCommand = "";
            EditAiIcon = "";
            EditAiDetectionCommand = "";
            EditAiEnabled = false;
            EditAiIsDefault = false;
        }
    }

    [RelayCommand]
    private void AddAiAssistant()
    {
        var newAi = new AiAssistant
        {
            Id = Guid.NewGuid().ToString(),
            Name = "New AI Assistant",
            Command = "",
            Icon = "AI",
            Enabled = false,
            IsDefault = false
        };

        AiAssistants.Add(newAi);
        SelectedAiAssistant = newAi;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void DeleteAiAssistant()
    {
        if (SelectedAiAssistant == null) return;

        // Don't allow deleting if it's the last enabled assistant
        if (SelectedAiAssistant.Enabled && AiAssistants.Count(a => a.Enabled) <= 1)
        {
            _dialogService.ShowWarning("Cannot delete the last enabled AI assistant.", "Warning");
            return;
        }

        var index = AiAssistants.IndexOf(SelectedAiAssistant);
        AiAssistants.Remove(SelectedAiAssistant);

        if (AiAssistants.Count > 0)
        {
            SelectedAiAssistant = AiAssistants[Math.Min(index, AiAssistants.Count - 1)];
        }
        else
        {
            SelectedAiAssistant = null;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyAiAssistant()
    {
        if (SelectedAiAssistant == null) return;

        // If making this the default, clear other defaults
        if (EditAiIsDefault && !SelectedAiAssistant.IsDefault)
        {
            foreach (var ai in AiAssistants)
            {
                ai.IsDefault = false;
            }
        }

        SelectedAiAssistant.Name = EditAiName;
        SelectedAiAssistant.Command = EditAiCommand;
        SelectedAiAssistant.Icon = EditAiIcon;
        SelectedAiAssistant.DetectionCommand = string.IsNullOrWhiteSpace(EditAiDetectionCommand) ? null : EditAiDetectionCommand;
        SelectedAiAssistant.Enabled = EditAiEnabled;
        SelectedAiAssistant.IsDefault = EditAiIsDefault;

        // Ensure at least one is enabled
        if (!AiAssistants.Any(a => a.Enabled))
        {
            SelectedAiAssistant.Enabled = true;
            EditAiEnabled = true;
        }

        // Force collection refresh
        var index = AiAssistants.IndexOf(SelectedAiAssistant);
        if (index >= 0)
        {
            var ai = SelectedAiAssistant;
            AiAssistants.RemoveAt(index);
            AiAssistants.Insert(index, ai);
            SelectedAiAssistant = ai;
        }

        SyncRichModeToJson();
        Save();
    }

    [RelayCommand]
    private void ResetAiAssistants()
    {
        AiAssistants = new ObservableCollection<AiAssistant>(AiAssistant.GetDefaults());
        SelectedAiAssistant = null;
        SyncRichModeToJson();
    }

    // Link pattern management
    partial void OnSelectedLinkPatternChanged(LinkPattern? value)
    {
        if (value != null)
        {
            EditLpName = value.Name;
            EditLpPattern = value.Pattern;
            EditLpUrlTemplate = value.UrlTemplate;
            EditLpEnabled = value.Enabled;
            EditLpPriority = value.Priority;
            // Clear test results
            RegexTestInput = "";
            RegexTestMatch = "";
            RegexTestUrl = "";
        }
        else
        {
            EditLpName = "";
            EditLpPattern = "";
            EditLpUrlTemplate = "";
            EditLpEnabled = true;
            EditLpPriority = 0;
        }
        UpdateRegexTest();
    }

    partial void OnEditLpPatternChanged(string value) => UpdateRegexTest();
    partial void OnEditLpUrlTemplateChanged(string value) => UpdateRegexTest();
    partial void OnRegexTestInputChanged(string value) => UpdateRegexTest();

    private void UpdateRegexTest()
    {
        if (string.IsNullOrEmpty(EditLpPattern))
        {
            RegexTestIsValid = true;
            RegexTestError = "";
            RegexTestMatch = "";
            RegexTestUrl = "";
            return;
        }

        try
        {
            var regex = new Regex(EditLpPattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            RegexTestIsValid = true;
            RegexTestError = "";

            if (!string.IsNullOrEmpty(RegexTestInput))
            {
                var match = regex.Match(RegexTestInput);
                if (match.Success)
                {
                    RegexTestMatch = match.Value;
                    // Build URL from template
                    var url = EditLpUrlTemplate ?? "";
                    url = url.Replace("$0", match.Value);
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        url = url.Replace($"${i}", match.Groups[i].Value);
                    }
                    RegexTestUrl = url;
                }
                else
                {
                    RegexTestMatch = "(no match)";
                    RegexTestUrl = "";
                }
            }
            else
            {
                RegexTestMatch = "";
                RegexTestUrl = "";
            }
        }
        catch (RegexParseException ex)
        {
            RegexTestIsValid = false;
            RegexTestError = ex.Message;
            RegexTestMatch = "";
            RegexTestUrl = "";
        }
        catch (RegexMatchTimeoutException)
        {
            RegexTestIsValid = false;
            RegexTestError = "Pattern timed out (too complex)";
            RegexTestMatch = "";
            RegexTestUrl = "";
        }
    }

    [RelayCommand]
    private void AddLinkPattern()
    {
        var newPattern = new LinkPattern
        {
            Id = Guid.NewGuid().ToString(),
            Name = "New Pattern",
            Pattern = "",
            UrlTemplate = "",
            Enabled = true,
            Priority = 0
        };

        LinkPatterns.Add(newPattern);
        SelectedLinkPattern = newPattern;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void DeleteLinkPattern()
    {
        if (SelectedLinkPattern == null) return;

        var index = LinkPatterns.IndexOf(SelectedLinkPattern);
        LinkPatterns.Remove(SelectedLinkPattern);

        if (LinkPatterns.Count > 0)
        {
            SelectedLinkPattern = LinkPatterns[Math.Min(index, LinkPatterns.Count - 1)];
        }
        else
        {
            SelectedLinkPattern = null;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyLinkPattern()
    {
        if (SelectedLinkPattern == null) return;

        SelectedLinkPattern.Name = EditLpName;
        SelectedLinkPattern.Pattern = EditLpPattern;
        SelectedLinkPattern.UrlTemplate = EditLpUrlTemplate;
        SelectedLinkPattern.Enabled = EditLpEnabled;
        SelectedLinkPattern.Priority = EditLpPriority;

        // Force collection refresh
        var index = LinkPatterns.IndexOf(SelectedLinkPattern);
        if (index >= 0)
        {
            var pattern = SelectedLinkPattern;
            LinkPatterns.RemoveAt(index);
            LinkPatterns.Insert(index, pattern);
            SelectedLinkPattern = pattern;
        }

        SyncRichModeToJson();
        Save();
    }

    [RelayCommand]
    private void MoveLinkPatternUp()
    {
        if (SelectedLinkPattern == null) return;

        var index = LinkPatterns.IndexOf(SelectedLinkPattern);
        if (index > 0)
        {
            var pattern = SelectedLinkPattern;
            LinkPatterns.RemoveAt(index);
            LinkPatterns.Insert(index - 1, pattern);
            SelectedLinkPattern = pattern;
            SyncRichModeToJson();
        }
    }

    [RelayCommand]
    private void MoveLinkPatternDown()
    {
        if (SelectedLinkPattern == null) return;

        var index = LinkPatterns.IndexOf(SelectedLinkPattern);
        if (index < LinkPatterns.Count - 1)
        {
            var pattern = SelectedLinkPattern;
            LinkPatterns.RemoveAt(index);
            LinkPatterns.Insert(index + 1, pattern);
            SelectedLinkPattern = pattern;
            SyncRichModeToJson();
        }
    }

    // Project type management
    partial void OnSelectedProjectTypeChanged(ProjectType? value)
    {
        if (value != null)
        {
            EditPtId = value.Id;
            EditPtName = value.Name;
            EditPtDetectFiles = string.Join("\n", value.DetectFiles);
            EditPtDefaultCommand = value.DefaultCommand;
            EditPtWatchCommand = value.WatchCommand ?? "";
            EditPtUrlPattern = value.UrlPattern ?? "";
            EditPtPriority = value.Priority;
        }
        else
        {
            EditPtId = "";
            EditPtName = "";
            EditPtDetectFiles = "";
            EditPtDefaultCommand = "";
            EditPtWatchCommand = "";
            EditPtUrlPattern = "";
            EditPtPriority = 0;
        }
    }

    [RelayCommand]
    private void AddProjectType()
    {
        var newType = new ProjectType
        {
            Id = $"custom-{DateTime.Now:yyyyMMddHHmmss}",
            Name = "New Project Type",
            DetectFiles = [],
            DefaultCommand = "",
            Priority = 0
        };

        ProjectTypes.Add(newType);
        SelectedProjectType = newType;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void DeleteProjectType()
    {
        if (SelectedProjectType == null) return;

        var index = ProjectTypes.IndexOf(SelectedProjectType);
        ProjectTypes.Remove(SelectedProjectType);

        if (ProjectTypes.Count > 0)
        {
            SelectedProjectType = ProjectTypes[Math.Min(index, ProjectTypes.Count - 1)];
        }
        else
        {
            SelectedProjectType = null;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyProjectType()
    {
        if (SelectedProjectType == null) return;

        SelectedProjectType.Id = EditPtId;
        SelectedProjectType.Name = EditPtName;
        SelectedProjectType.DetectFiles = EditPtDetectFiles
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        SelectedProjectType.DefaultCommand = EditPtDefaultCommand;
        SelectedProjectType.WatchCommand = string.IsNullOrWhiteSpace(EditPtWatchCommand) ? null : EditPtWatchCommand;
        SelectedProjectType.UrlPattern = string.IsNullOrWhiteSpace(EditPtUrlPattern) ? null : EditPtUrlPattern;
        SelectedProjectType.Priority = EditPtPriority;

        // Force collection refresh
        var index = ProjectTypes.IndexOf(SelectedProjectType);
        if (index >= 0)
        {
            var type = SelectedProjectType;
            ProjectTypes.RemoveAt(index);
            ProjectTypes.Insert(index, type);
            SelectedProjectType = type;
        }

        SyncRichModeToJson();
        Save();
    }

    [RelayCommand]
    private void MoveProjectTypeUp()
    {
        if (SelectedProjectType == null) return;

        var index = ProjectTypes.IndexOf(SelectedProjectType);
        if (index > 0)
        {
            var type = SelectedProjectType;
            ProjectTypes.RemoveAt(index);
            ProjectTypes.Insert(index - 1, type);
            SelectedProjectType = type;
            SyncRichModeToJson();
        }
    }

    [RelayCommand]
    private void MoveProjectTypeDown()
    {
        if (SelectedProjectType == null) return;

        var index = ProjectTypes.IndexOf(SelectedProjectType);
        if (index < ProjectTypes.Count - 1)
        {
            var type = SelectedProjectType;
            ProjectTypes.RemoveAt(index);
            ProjectTypes.Insert(index + 1, type);
            SelectedProjectType = type;
            SyncRichModeToJson();
        }
    }

    // Directory settings management
    partial void OnSelectedDirectoryChanged(string? value)
    {
        UpdateCurrentDirectorySettings();
        LoadDirectorySettingsToEdit();
    }

    private void LoadDirectorySettingsToEdit()
    {
        if (CurrentDirectorySettings != null)
        {
            EditDsLayoutMode = CurrentDirectorySettings.LayoutMode;
            EditDsSplitRatio = CurrentDirectorySettings.SplitRatio;
            EditDsShowRunTerminal = CurrentDirectorySettings.IsRunTerminalVisible;
            EditDsRunSplitRatio = CurrentDirectorySettings.RunSplitRatio;
            EditDsShowExplorer = CurrentDirectorySettings.IsExplorerVisible;
            EditDsExplorerRatio = CurrentDirectorySettings.ExplorerSplitRatio;
            EditDsShowLeftPanel = CurrentDirectorySettings.IsLeftPanelVisible;
            EditDsLeftPanelRatio = CurrentDirectorySettings.LeftPanelSplitRatio;
            RunConfigurations = new ObservableCollection<RunConfiguration>(CurrentDirectorySettings.RunConfigurations);

            // Load AI assistant selection
            var aiId = CurrentDirectorySettings.ActiveAiAssistantId;
            EditDsAiAssistant = !string.IsNullOrEmpty(aiId)
                ? AiAssistants.FirstOrDefault(a => a.Id == aiId)
                : AiAssistants.FirstOrDefault(a => a.IsDefault) ?? AiAssistants.FirstOrDefault();
        }
        else
        {
            EditDsLayoutMode = TerminalLayoutMode.HorizontalSplit;
            EditDsSplitRatio = 0.6;
            EditDsShowRunTerminal = false;
            EditDsRunSplitRatio = 0.3;
            EditDsShowExplorer = false;
            EditDsExplorerRatio = 0.25;
            EditDsShowLeftPanel = false;
            EditDsLeftPanelRatio = 0.25;
            RunConfigurations = [];
            EditDsAiAssistant = AiAssistants.FirstOrDefault(a => a.IsDefault) ?? AiAssistants.FirstOrDefault();
        }
        SelectedRunConfiguration = null;
    }

    partial void OnSelectedRunConfigurationChanged(RunConfiguration? value)
    {
        if (value != null)
        {
            EditRcName = value.Name;
            EditRcCommand = value.Command;
            EditRcIsDefault = value.IsDefault;
            EditRcUrlPattern = value.UrlPattern ?? "";
        }
        else
        {
            EditRcName = "";
            EditRcCommand = "";
            EditRcIsDefault = false;
            EditRcUrlPattern = "";
        }
    }

    [RelayCommand]
    private void ApplyDirectorySettings()
    {
        if (SelectedDirectory == null || CurrentDirectorySettings == null) return;

        CurrentDirectorySettings.LayoutMode = EditDsLayoutMode;
        CurrentDirectorySettings.SplitRatio = EditDsSplitRatio;
        CurrentDirectorySettings.IsRunTerminalVisible = EditDsShowRunTerminal;
        CurrentDirectorySettings.RunSplitRatio = EditDsRunSplitRatio;
        CurrentDirectorySettings.IsExplorerVisible = EditDsShowExplorer;
        CurrentDirectorySettings.ExplorerSplitRatio = EditDsExplorerRatio;
        CurrentDirectorySettings.IsLeftPanelVisible = EditDsShowLeftPanel;
        CurrentDirectorySettings.LeftPanelSplitRatio = EditDsLeftPanelRatio;
        CurrentDirectorySettings.RunConfigurations = RunConfigurations.ToList();
        CurrentDirectorySettings.ActiveAiAssistantId = EditDsAiAssistant?.Id;

        SyncRichModeToJson();
        Save();
    }

    [RelayCommand]
    private void DeleteDirectorySettings()
    {
        if (SelectedDirectory == null) return;

        try
        {
            var config = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions);
            var normalizedPath = NormalizePath(SelectedDirectory);
            if (config?.DirectorySettings.ContainsKey(normalizedPath) == true)
            {
                config.DirectorySettings.Remove(normalizedPath);
                JsonText = JsonSerializer.Serialize(config, JsonOptions);
                IsDirty = JsonText != _originalJson;

                // Refresh directories list - include both open folders and folders with saved settings
                var allDirectories = config.OpenFolders
                    .Union(config.DirectorySettings.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(k => k);
                Directories = new ObservableCollection<string>(allDirectories);
                if (Directories.Count > 0)
                {
                    SelectedDirectory = Directories.First();
                }
                else
                {
                    SelectedDirectory = null;
                    CurrentDirectorySettings = null;
                }
            }
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"Cannot delete directory settings: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private void AddRunConfiguration()
    {
        var newConfig = new RunConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = "New Configuration",
            Command = "",
            IsDefault = RunConfigurations.Count == 0
        };

        RunConfigurations.Add(newConfig);
        SelectedRunConfiguration = newConfig;
    }

    [RelayCommand]
    private void DeleteRunConfiguration()
    {
        if (SelectedRunConfiguration == null) return;

        var index = RunConfigurations.IndexOf(SelectedRunConfiguration);
        RunConfigurations.Remove(SelectedRunConfiguration);

        if (RunConfigurations.Count > 0)
        {
            SelectedRunConfiguration = RunConfigurations[Math.Min(index, RunConfigurations.Count - 1)];
        }
        else
        {
            SelectedRunConfiguration = null;
        }
    }

    [RelayCommand]
    private void ApplyRunConfiguration()
    {
        if (SelectedRunConfiguration == null) return;

        SelectedRunConfiguration.Name = EditRcName;
        SelectedRunConfiguration.Command = EditRcCommand;
        SelectedRunConfiguration.UrlPattern = string.IsNullOrWhiteSpace(EditRcUrlPattern) ? null : EditRcUrlPattern;

        // Handle default flag
        if (EditRcIsDefault)
        {
            foreach (var config in RunConfigurations)
            {
                config.IsDefault = config == SelectedRunConfiguration;
            }
        }
        else
        {
            SelectedRunConfiguration.IsDefault = false;
        }

        // Force full collection refresh to update all IsDefault indicators
        var configs = RunConfigurations.ToList();
        var selected = SelectedRunConfiguration;
        RunConfigurations.Clear();
        foreach (var config in configs)
        {
            RunConfigurations.Add(config);
        }
        SelectedRunConfiguration = selected;

        // Update the directory settings with the run configurations
        if (CurrentDirectorySettings != null)
        {
            CurrentDirectorySettings.RunConfigurations = RunConfigurations.ToList();
        }

        SyncRichModeToJson();
        Save();
    }

    private static string GetDefaultShell()
    {
        // Check for environment variable first
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            return shell;

        // macOS defaults
        if (File.Exists("/bin/zsh")) return "/bin/zsh";
        if (File.Exists("/bin/bash")) return "/bin/bash";

        return "/bin/sh";
    }
}